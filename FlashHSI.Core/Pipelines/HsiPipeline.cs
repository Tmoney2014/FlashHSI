using FlashHSI.Core.Classifiers;
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

        /// <summary>
        /// AI가 추가함: White/Dark 레퍼런스를 Feature Extractor에 전달하여 방사 보정 활성화
        /// </summary>
        public void SetCalibration(double[]? white, double[]? dark)
        {
            if (_featureExtractor != null && white != null && dark != null)
            {
                _featureExtractor.SetCalibration(white, dark);
            }
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
            var classifier = new LinearClassifier();
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

            // AI가 수정함: PrepChainOrder가 있으면 Python 학습 순서 그대로 전처리 적용 (패리티 보장)
            // PrepChainOrder 없으면 기존 하드코딩 순서로 폴백 (하위 호환)
            if (config.PrepChainOrder != null && config.PrepChainOrder.Count > 0)
            {
                RegisterProcessorsByChainOrder(config);
            }
            else
            {
                RegisterProcessorsLegacy(config);
            }

            // 5. Configure
            // Note: rawBandCount is usually unknown here until file load. 
            // So we might need to Re-Configure later. But we can set SelectedBands.
            // HsiEngine calls Configure again on file load, so this is fine.
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// Python PrepChainOrder 순서에 따라 전처리기를 등록합니다.
        /// 규칙:
        ///   - SG → 항상 Raw Domain (스펙트럼 평활화)
        ///   - SimpleDeriv / Absorbance → Feature Extractor가 처리 (건너뜀)
        ///   - SNV, MinMax, L2, MinSub, Center → SimpleDeriv/Absorbance 이전이면 Raw, 이후면 Feature Domain
        /// </summary>
        private void RegisterProcessorsByChainOrder(ModelConfig config)
        {
            bool passedExtraction = false;  // SimpleDeriv 또는 Absorbance를 넘었는지 여부

            foreach (string stepName in config.PrepChainOrder)
            {
                switch (stepName)
                {
                    case "SG":
                        // SG는 항상 Raw Domain
                        if (config.Preprocessing.ApplySG)
                            AddRawProcessor(new SavitzkyGolayProcessor(config.Preprocessing.SGWin, config.Preprocessing.SGPoly));
                        break;

                    case "SimpleDeriv":
                    case "Absorbance":
                        // Feature Extractor가 담당 — 마커만 설정
                        passedExtraction = true;
                        break;

                    case "SNV":
                        if (config.Preprocessing.ApplySNV)
                            RegisterByDomain(new SnvProcessor(), passedExtraction);
                        break;

                    case "MinMax":
                        if (config.Preprocessing.ApplyMinMax)
                            RegisterByDomain(new MinMaxProcessor(), passedExtraction);
                        break;

                    case "L2":
                        if (config.Preprocessing.ApplyL2)
                            RegisterByDomain(new L2NormalizeProcessor(), passedExtraction);
                        break;
                }
            }
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// passedExtraction에 따라 Raw 또는 Feature Domain에 전처리기를 등록합니다.
        /// </summary>
        private void RegisterByDomain(IHsiFrameProcessor processor, bool featureDomain)
        {
            if (featureDomain)
                AddProcessor(processor);
            else
                AddRawProcessor(processor);
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// PrepChainOrder 없는 구형 model.json 하위 호환 등록 방식.
        /// SG → Raw Domain, SNV → Raw, MinMax → Raw, L2 → Feature Domain.
        /// </summary>
        private void RegisterProcessorsLegacy(ModelConfig config)
        {
            // Raw Domain
            if (config.Preprocessing.ApplySG)
                AddRawProcessor(new SavitzkyGolayProcessor(config.Preprocessing.SGWin, config.Preprocessing.SGPoly));
            if (config.Preprocessing.ApplySNV) AddRawProcessor(new SnvProcessor());
            if (config.Preprocessing.ApplyMinMax) AddRawProcessor(new MinMaxProcessor());
            
            // Feature Domain (Legacy L2)
            if (config.Preprocessing.ApplyL2) AddProcessor(new L2NormalizeProcessor());
        }
    }
}
