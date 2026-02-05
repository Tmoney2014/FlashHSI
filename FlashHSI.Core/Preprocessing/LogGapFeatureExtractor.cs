using FlashHSI.Core.Interfaces;
using System.Runtime.InteropServices;

namespace FlashHSI.Core.Preprocessing
{
    public unsafe class LogGapFeatureExtractor : IFeatureExtractor
    {
        private int[] _targetIndices = Array.Empty<int>();
        private int[] _gapIndices = Array.Empty<int>();
        private int _count;
        private readonly int _gapShift;
        private const double Epsilon = 1e-6;

        // Calibration Data (Optional)
        private double[]? _whiteRef;
        private double[]? _darkRef;
        private bool _useCalibration;

        public LogGapFeatureExtractor(int gapShift)
        {
            _gapShift = gapShift;
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// Set White/Dark reference for radiometric calibration.
        /// </summary>
        public void SetCalibration(double[] white, double[] dark)
        {
            _whiteRef = white;
            _darkRef = dark;
            _useCalibration = (white != null && dark != null && white.Length > 0);
        }

        public void Configure(List<int> selectedBands, int rawBandCount)
        {
            _count = selectedBands.Count;
            _targetIndices = selectedBands.ToArray();
            _gapIndices = new int[_count];

            for (int i = 0; i < _count; i++)
            {
                // Forward Difference: GapIdx = Target + Shift
                int gapIdx = _targetIndices[i] + _gapShift;
                if (gapIdx >= rawBandCount) gapIdx = rawBandCount - 1; // Clamp to max
                _gapIndices[i] = gapIdx;
            }
        }

        public void Extract(double* input, double* output)
        {
            for (int i = 0; i < _count; i++)
            {
                int tIdx = _targetIndices[i];
                int gIdx = _gapIndices[i];

                double valTarget = input[tIdx];
                double valGap = input[gIdx];

                // Calibration is now handled in Preprocessing Step (before Extraction) or here if Raw.
                // But since input is double*, we assume it might be already calibrated/preprocessed.
                // However, to maintain logic: if Calibration is set, we apply it.
                // Note: If Recalibration was done in RawProcessor, we shouldn't do it here twice.
                // For now, let's keep it but check if we need to apply it.
                // Actually, if we use HsiPipeline with Raw Processors (Reflectance), input is already Reflectance.
                // So _useCalibration should be effectively false in that pipeline config.
                
                if (_useCalibration)
                {
                    double whiteT = _whiteRef![tIdx];
                    double darkT = _darkRef![tIdx];
                    double whiteG = _whiteRef![gIdx];
                    double darkG = _darkRef![gIdx];

                    double denomT = whiteT - darkT;
                    double denomG = whiteG - darkG;

                    valTarget = (denomT > Epsilon) ? (valTarget - darkT) / denomT : Epsilon;
                    valGap = (denomG > Epsilon) ? (valGap - darkG) / denomG : Epsilon;
                }

                // Forward Difference: Log(Gap/Target) -> Band[i+Gap] / Band[i]
                output[i] = Math.Log10((valGap + Epsilon) / (valTarget + Epsilon));
            }
        }
    }
}
