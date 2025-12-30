namespace FlashHSI.Core.Interfaces
{
    /// <summary>
    /// Common interface for Classification Models (LDA, SVM, PLS-DA).
    /// </summary>
    public unsafe interface IClassifier
    {
        /// <summary>
        /// Load model configuration and weights.
        /// </summary>
        void Load(ModelConfig config);

        /// <summary>
        /// Predict class probabilities from input features.
        /// </summary>
        /// <param name="features">Pointer to feature vector</param>
        /// <param name="length">Length of feature vector</param>
        /// <returns>Index of the predicted class, or -1 if Unknown/Background</returns>
        int Predict(double* features, int length);

        void SetThreshold(double threshold);
    }
}
