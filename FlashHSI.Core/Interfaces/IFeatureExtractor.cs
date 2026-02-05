namespace FlashHSI.Core.Interfaces
{
    public unsafe interface IFeatureExtractor
    {
        void Configure(List<int> selectedBands, int rawBandCount);

        /// <summary>
        /// Extract features from raw processed frame into output buffer.
        /// </summary>
        void Extract(double* input, double* output);

        /// <summary>
        /// Set White/Dark reference spectra for radiometric calibration.
        /// If set, Extract will apply: Reflectance = (Raw - Dark) / (White - Dark)
        /// </summary>
        /// <param name="white">White reference spectrum (full band count)</param>
        /// <param name="dark">Dark reference spectrum (full band count)</param>
        void SetCalibration(double[] white, double[] dark);
    }
}
