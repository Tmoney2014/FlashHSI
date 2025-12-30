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

        public LogGapFeatureExtractor(int gapShift)
        {
            _gapShift = gapShift;
        }

        public void Configure(List<int> selectedBands, int rawBandCount)
        {
            _count = selectedBands.Count;
            _targetIndices = selectedBands.ToArray();
            _gapIndices = new int[_count];

            for (int i = 0; i < _count; i++)
            {
                int gapIdx = _targetIndices[i] - _gapShift;
                if (gapIdx < 0) gapIdx = 0; // Boundary handling
                _gapIndices[i] = gapIdx;
            }
        }

        public void Extract(ushort* input, double* output)
        {
            // Vectorizable loop?
            for (int i = 0; i < _count; i++)
            {
                int tIdx = _targetIndices[i];
                int gIdx = _gapIndices[i];

                double valTarget = input[tIdx];
                double valGap = input[gIdx];

                // Formula: Log10((Gap+e)/(Target+e))
                output[i] = Math.Log10((valGap + Epsilon) / (valTarget + Epsilon));
            }
        }
    }
}
