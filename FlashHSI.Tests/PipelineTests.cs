using FlashHSI.Core;
using FlashHSI.Core.Classifiers;
using FlashHSI.Core.Pipelines;
using FlashHSI.Core.Preprocessing;

namespace FlashHSI.Tests
{
    public class PipelineTests
    {
        [Fact]
        public unsafe void Pipeline_Can_Classify_Correctly()
        {
            // Arrange: Setup Config (Gap=0 for simplicity)
            var config = new ModelConfig
            {
                ModelType = "LinearModel",
                SelectedBands = new List<int> { 10 },
                Preprocessing = new PreprocessingConfig { Gap = 0 },
                Weights = new List<List<double>>(),
                Bias = new List<double>()
            };
            // AI가 수정함: LogGap 부호 수정 반영 (log10(Target/Gap))
            // gapShift=-1 → gapIdx=9, input[10]=200(Target), input[9]=100(Gap)
            // Feat = log10(200/100) = log10(2) ≈ +0.301
            // Class 0 Score: -10 * 0.301 = -3.01
            // Class 1 Score: +10 * 0.301 = +3.01 → Winner = Class 1 ✅
            config.Weights.Add(new List<double> { -10.0 }); // Class 0 (Negative Weight)
            config.Weights.Add(new List<double> { 10.0 });  // Class 1 (Positive Weight)
            config.Bias.Add(0.0);
            config.Bias.Add(0.0);

            // Assemble Pipeline manually
            var pipeline = new HsiPipeline();

            // 1. Classifier
            var classifier = new LinearClassifier();
            classifier.Load(config);
            pipeline.SetClassifier(classifier);

            // 2. Feature Extractor (LogGap)
            // gapShift=-1: GapBand = SelectedBand(10) + (-1) = 9
            var extractor = new LogGapFeatureExtractor(gapShift: -1);
            pipeline.SetFeatureExtractor(extractor);

            // Configure
            pipeline.Configure(rawBandCount: 200, selectedBands: config.SelectedBands);

            // Input Data (200 Bands)
            ushort[] input = new ushort[200];
            input[10] = 200; // Target
            input[9] = 100;  // Gap (index 10 + shift(-1) = 9)

            // Act
            int result = -1;
            fixed (ushort* ptr = input)
            {
                result = pipeline.ProcessFrame(ptr, 200);
            }

            // Assert
            Assert.Equal(1, result);
        }
    }
}
