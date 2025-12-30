namespace FlashHSI.Core.Interfaces
{
    public unsafe interface IFeatureExtractor
    {
        void Configure(List<int> selectedBands, int rawBandCount);
        
        /// <summary>
        /// Extract features from raw processed frame into output buffer.
        /// </summary>
        void Extract(ushort* input, double* output);
    }
}
