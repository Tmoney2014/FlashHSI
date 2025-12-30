namespace FlashHSI.Core.Interfaces
{
    /// <summary>
    /// Interface for any hyperspectral data processing step.
    /// Can be a filter, normalization, or transformation.
    /// </summary>
    public unsafe interface IHsiFrameProcessor
    {
        /// <summary>
        /// Process a frame of data in-place or utilizing a buffer.
        /// </summary>
        /// <param name="data">Pointer to the raw or processed data</param>
        /// <param name="length">Number of bands/features</param>
        void Process(double* data, int length);
    }
}
