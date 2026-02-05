using FlashHSI.Core.Interfaces;
using FlashHSI.Core.Preprocessing;

namespace FlashHSI.Core.Pipelines
{
    /// <summary>
    /// Standard Pipeline: Raw UShort -> Feature Extraction (LogGap/Raw) -> Preprocessing (Filters) -> Classifier -> Result
    /// </summary>
    public unsafe class HsiPipeline
    {
        private readonly List<IHsiFrameProcessor> _rawProcessors = new();     // Pre-Extraction (Raw Domain)
        private readonly List<IHsiFrameProcessor> _featureProcessors = new(); // Post-Extraction (Feature Domain)
        
        private IFeatureExtractor? _featureExtractor;
        private IClassifier? _classifier;

        // Configuration
        private int _featureCount;
        private int _rawBandCount;

        public void SetClassifier(IClassifier classifier)
        {
            _classifier = classifier;
        }

        public void SetFeatureExtractor(IFeatureExtractor extractor)
        {
            _featureExtractor = extractor;
        }

        /// <summary>
        /// Adds a processor to the Feature Domain stage (Post-Extraction).
        /// This is the legacy behavior for compatibility.
        /// </summary>
        public void AddProcessor(IHsiFrameProcessor processor)
        {
            _featureProcessors.Add(processor);
        }

        /// <summary>
        /// Adds a processor to the Raw Domain stage (Pre-Extraction).
        /// Use this for smoothing, SNV, Ref-Calibration logic etc.
        /// </summary>
        public void AddRawProcessor(IHsiFrameProcessor processor)
        {
            _rawProcessors.Add(processor);
        }

        public void Configure(int rawBandCount, List<int> selectedBands)
        {
            _rawBandCount = rawBandCount;
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
            
            // 0. Prepare Buffer for Raw Data (Double Precision)
            // Stackalloc is risky if band count is large, but HSI usually < 2000 bands.
            // 2000 bands * 8 bytes = 16KB. Stack is usually 1MB. Safe.
            double* rawBuffer = stackalloc double[_rawBandCount];
            
            // Raw Data Conversion (UShort -> Double)
            // We assume 'length' matches '_rawBandCount' (which should be configured).
            // Robustness: use min length
            int copyLen = Math.Min(length, _rawBandCount);
            for(int i=0; i<copyLen; i++) rawBuffer[i] = rawData[i];

            // 1. Run Raw Processors (Preprocessing)
            // In-place modification of rawBuffer
            foreach (var processor in _rawProcessors)
            {
                processor.Process(rawBuffer, _rawBandCount);
            }

            // 2. Extract Features
            // Feature Buffer
            double* featureBuffer = stackalloc double[_featureCount];
            _featureExtractor.Extract(rawBuffer, featureBuffer);

            // 3. Run Feature Processors (Post-processing)
            foreach (var processor in _featureProcessors)
            {
                processor.Process(featureBuffer, _featureCount);
            }

            // 4. Classification
            return _classifier.Predict(featureBuffer, _featureCount);
        }

        public Type? GetExtractorType()
        {
            return _featureExtractor?.GetType();
        }

        public void LoadModel(ModelConfig config, string rootDir)
        {
            _rawProcessors.Clear();
            _featureProcessors.Clear();

            // 1. Classifier
            var classifier = new FlashHSI.Core.Classifiers.LinearClassifier();
            classifier.Load(config);
            SetClassifier(classifier);

            // 2. Extractor
            int gap = config.Preprocessing.Gap;
            IFeatureExtractor extractor;
            string mode = config.Preprocessing.Mode ?? "";
            
            bool useAbsorbance = mode.Contains("Absorbance", StringComparison.OrdinalIgnoreCase) 
                              || config.Preprocessing.ApplyAbsorbance;

            if (useAbsorbance)
            {
                extractor = new LogGapFeatureExtractor(gap);
            }
            else
            {
                extractor = new RawGapFeatureExtractor(gap);
            }
            SetFeatureExtractor(extractor);

            // 3. Preprocessors (Raw Domain)
            if (config.Preprocessing.ApplySNV) AddRawProcessor(new SnvProcessor());
            if (config.Preprocessing.ApplyMinMax) AddRawProcessor(new MinMaxProcessor());
            
            // 4. Preprocessors (Feature Domain) - Legacy L2
            if (config.Preprocessing.ApplyL2) AddProcessor(new L2NormalizeProcessor());

            // 5. Configure
            // Note: rawBandCount is usually unknown here until file load. 
            // So we might need to Re-Configure later. But we can set SelectedBands.
            // HsiEngine calls Configure again on file load, so this is fine.
        }
    }
}
