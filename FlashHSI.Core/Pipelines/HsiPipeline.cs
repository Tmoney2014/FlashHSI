using FlashHSI.Core.Interfaces;
using FlashHSI.Core.Preprocessing;

namespace FlashHSI.Core.Pipelines
{
    /// <summary>
    /// Standard Pipeline: Raw UShort -> Feature Extraction (LogGap/Raw) -> Preprocessing (Filters) -> Classifier -> Result
    /// </summary>
    public unsafe class HsiPipeline
    {
        private readonly List<IHsiFrameProcessor> _processors = new();
        private IFeatureExtractor? _featureExtractor;
        private IClassifier? _classifier;

        // Configuration
        private int _featureCount;

        public void SetClassifier(IClassifier classifier)
        {
            _classifier = classifier;
        }

        public void SetFeatureExtractor(IFeatureExtractor extractor)
        {
            _featureExtractor = extractor;
        }

        public void AddProcessor(IHsiFrameProcessor processor)
        {
            _processors.Add(processor);
        }

        public void Configure(int rawBandCount, List<int> selectedBands)
        {
            _featureCount = selectedBands.Count;
            if (_featureExtractor != null)
            {
                _featureExtractor.Configure(selectedBands, rawBandCount);
            }
        }

        public void SetThreshold(double threshold)
        {
            _classifier?.SetThreshold(threshold);
        }

        public int ProcessFrame(ushort* rawData, int length)
        {
            if (_classifier == null || _featureExtractor == null) return -1;

            // Stackalloc for features
            // Note: If feature count is large (>1024), stackalloc might risk stack overflow.
            // For typical HSI multispectral (selected bands ~10-100), it's fine.
            double* processingBuffer = stackalloc double[_featureCount];

            // 1. Extract Features (e.g. Log(Gap/Target))
            _featureExtractor.Extract(rawData, processingBuffer);

            // 2. Run Preprocessors (e.g. Normalization on the feature vector)
            foreach (var processor in _processors)
            {
                processor.Process(processingBuffer, _featureCount);
            }

            // 3. Classification
            return _classifier.Predict(processingBuffer, _featureCount);
        }
    }
}
