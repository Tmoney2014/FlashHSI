using FlashHSI.Core.Interfaces;

namespace FlashHSI.Core.Preprocessing
{
    public unsafe class RawGapFeatureExtractor : IFeatureExtractor
    {
        private int[] _targetIndices = Array.Empty<int>();
        private int[] _gapIndices = Array.Empty<int>();
        private int _count;
        private readonly int _gapShift;

        public RawGapFeatureExtractor(int gapShift)
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
                // Python: Output[i] = Band[i] - Band[i + gap]
                // So for selected band 'i', we need indices 'i' and 'i + gap'
                int gapIdx = _targetIndices[i] + _gapShift;
                if (gapIdx >= rawBandCount) gapIdx = rawBandCount - 1; // Clamp to last band if out of bounds (though shouldn't happen with valid model)
                _gapIndices[i] = gapIdx;
            }
        }

        public void Extract(ushort* input, double* output)
        {
            for (int i = 0; i < _count; i++)
            {
                int tIdx = _targetIndices[i];
                int gIdx = _gapIndices[i];

                double valTarget = input[tIdx];
                double valGap = input[gIdx];

                // Python Logic: Band[i] - Band[i+gap]
                output[i] = valTarget - valGap;
            }
        }
    }
}
