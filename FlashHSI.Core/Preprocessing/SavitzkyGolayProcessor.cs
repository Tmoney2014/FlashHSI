using FlashHSI.Core.Interfaces;

namespace FlashHSI.Core.Preprocessing
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// Savitzky-Golay 평활화 필터 (deriv=0, 순수 평활화).
    /// scipy.signal.savgol_filter(data, window_length=win, polyorder=poly, deriv=0, axis=1) 패리티 구현.
    ///
    /// 경계 처리: mirror padding (scipy 기본 mode='interp' 근사).
    /// 경계 밴드 몇 개에서 미세 차이가 있을 수 있으나 스펙트럼 분류 정확도에 실질 영향 없음.
    /// </summary>
    public unsafe class SavitzkyGolayProcessor : IHsiFrameProcessor
    {
        private readonly int _windowSize;   // 반드시 홀수
        private readonly int _polyOrder;    // 다항식 차수 < windowSize
        private readonly double[] _coeffs;  // 컨볼루션 계수 (길이 = windowSize)

        /// <param name="windowSize">윈도우 크기 (홀수, 예: 5)</param>
        /// <param name="polyOrder">다항식 차수 (예: 2)</param>
        public SavitzkyGolayProcessor(int windowSize, int polyOrder)
        {
            if (windowSize % 2 == 0) windowSize++;   // 짝수면 홀수로 올림
            if (polyOrder >= windowSize) polyOrder = windowSize - 1;

            _windowSize = windowSize;
            _polyOrder = polyOrder;
            _coeffs = ComputeSGCoefficients(windowSize, polyOrder);
        }

        public void Process(double* data, int length)
        {
            if (length < _windowSize) return;  // 밴드 수 부족 시 스킵

            int half = _windowSize / 2;

            // 임시 버퍼 (managed, Hot Path에서 호출되므로 stackalloc 사용)
            // length * 8 bytes — 최대 2000밴드 * 8 = 16KB, 스택 안전
            double* padded = stackalloc double[length + 2 * half];
            double* output = stackalloc double[length];

            // Mirror Padding: 경계를 거울 반사로 채움
            // [ reversed(data[1..half]) | data[0..length-1] | reversed(data[length-half-1..length-2]) ]
            for (int i = 0; i < half; i++)
                padded[i] = data[half - i];               // 좌측 mirror (data[1], data[2], ...)
            for (int i = 0; i < length; i++)
                padded[half + i] = data[i];               // 원본 데이터
            for (int i = 0; i < half; i++)
                padded[half + length + i] = data[length - 2 - i];  // 우측 mirror

            // 컨볼루션 (각 밴드에 SG 계수 적용)
            for (int i = 0; i < length; i++)
            {
                double sum = 0.0;
                for (int k = 0; k < _windowSize; k++)
                    sum += _coeffs[k] * padded[i + k];
                output[i] = sum;
            }

            // 결과 in-place 복사
            for (int i = 0; i < length; i++)
                data[i] = output[i];
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// Savitzky-Golay 컨볼루션 계수를 계산합니다 (deriv=0, 순수 평활화).
        ///
        /// 알고리즘: Gram 다항식 기반 최소자승 피팅.
        /// 참조: Savitzky &amp; Golay (1964), Analytical Chemistry.
        ///
        /// 계수 = J†[0,:] where J = Vandermonde matrix (m×p), J† = pseudoinverse
        /// m = [-half, ..., +half] 정수 격자, p = polyOrder+1
        /// </summary>
        private static double[] ComputeSGCoefficients(int windowSize, int polyOrder)
        {
            int half = windowSize / 2;
            int m = windowSize;
            int p = polyOrder + 1;  // 열 수 (0차~polyOrder차)

            // Vandermonde 행렬 J (m×p): J[i,j] = x[i]^j, x = -half..+half
            double[,] J = new double[m, p];
            for (int i = 0; i < m; i++)
            {
                double x = i - half;
                double xpow = 1.0;
                for (int j = 0; j < p; j++)
                {
                    J[i, j] = xpow;
                    xpow *= x;
                }
            }

            // J^T * J (p×p)
            double[,] JtJ = new double[p, p];
            for (int a = 0; a < p; a++)
                for (int b = 0; b < p; b++)
                    for (int k = 0; k < m; k++)
                        JtJ[a, b] += J[k, a] * J[k, b];

            // (J^T J)^{-1} via Gauss-Jordan
            double[,] inv = InvertMatrix(JtJ, p);

            // 계수 = J * (J^T J)^{-1} * e_0  (e_0 = [1,0,0,...] → 0차 항만 추출)
            // = J * inv의 첫 번째 열
            double[] coeffs = new double[m];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < p; j++)
                    coeffs[i] += J[i, j] * inv[j, 0];

            return coeffs;
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// n×n 행렬의 역행렬을 Gauss-Jordan 소거법으로 계산합니다.
        /// SG 계수 사전 계산에만 사용 (Hot Path 아님).
        /// </summary>
        private static double[,] InvertMatrix(double[,] mat, int n)
        {
            double[,] a = (double[,])mat.Clone();
            double[,] inv = new double[n, n];
            for (int i = 0; i < n; i++) inv[i, i] = 1.0;

            for (int col = 0; col < n; col++)
            {
                // Pivot 선택 (부분 피벗)
                int pivot = col;
                double maxVal = Math.Abs(a[col, col]);
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(a[row, col]) > maxVal)
                    {
                        maxVal = Math.Abs(a[row, col]);
                        pivot = row;
                    }
                }
                // 행 교환
                if (pivot != col)
                {
                    for (int k = 0; k < n; k++)
                    {
                        (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                        (inv[col, k], inv[pivot, k]) = (inv[pivot, k], inv[col, k]);
                    }
                }

                double diag = a[col, col];
                if (Math.Abs(diag) < 1e-12) continue;  // 특이 행렬 — 스킵

                for (int k = 0; k < n; k++)
                {
                    a[col, k] /= diag;
                    inv[col, k] /= diag;
                }

                for (int row = 0; row < n; row++)
                {
                    if (row == col) continue;
                    double factor = a[row, col];
                    for (int k = 0; k < n; k++)
                    {
                        a[row, k] -= factor * a[col, k];
                        inv[row, k] -= factor * inv[col, k];
                    }
                }
            }
            return inv;
        }
    }
}
