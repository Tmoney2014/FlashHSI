using FlashHSI.Core.Interfaces;

namespace FlashHSI.Core.Preprocessing
{
    public unsafe class RawGapFeatureExtractor : IFeatureExtractor
    {
        private int[] _targetIndices = Array.Empty<int>();
        private int[] _gapIndices = Array.Empty<int>();
        private int _count;
        private readonly int _gapShift;

        // Calibration Data (Optional)
        private double[]? _whiteRef;
        private double[]? _darkRef;
        private bool _useCalibration;

        public RawGapFeatureExtractor(int gapShift)
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
                // Python: Output[i] = Band[i] - Band[i + gap]
                // So for selected band 'i', we need indices 'i' and 'i + gap'
                int gapIdx = _targetIndices[i] + _gapShift;
                if (gapIdx >= rawBandCount) gapIdx = rawBandCount - 1; // Clamp to last band if out of bounds (though shouldn't happen with valid model)
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

                // Apply Calibration if set
                if (_useCalibration)
                {
                    double whiteT = _whiteRef![tIdx];
                    double darkT = _darkRef![tIdx];
                    double whiteG = _whiteRef![gIdx];
                    double darkG = _darkRef![gIdx];

                    double denomT = whiteT - darkT;
                    double denomG = whiteG - darkG;

                    valTarget = (denomT > 1e-6) ? (valTarget - darkT) / denomT : 0.0;
                    valGap = (denomG > 1e-6) ? (valGap - darkG) / denomG : 0.0;
                }

                // Forward Difference: Band[i+gap] - Band[i]
                output[i] = valGap - valTarget;
            }
        }
    }
}
