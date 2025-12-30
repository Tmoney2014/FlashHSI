using FlashHSI.Core;
using FlashHSI.Core.Classifiers;
using FlashHSI.Core.Pipelines;
using FlashHSI.Core.Preprocessing;
using Newtonsoft.Json;

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
            config.Weights.Add(new List<double> { 10.0 }); // Class 0 (Positive Weight)
            config.Weights.Add(new List<double> { -10.0 }); // Class 1 (Negative Weight)
            config.Bias.Add(0.0);
            config.Bias.Add(0.0);

            // Assemble Pipeline manually
            var pipeline = new HsiPipeline();

            // 1. Classifier
            var classifier = new LinearClassifier();
            classifier.Load(config);
            pipeline.SetClassifier(classifier);

            // 2. Feature Extractor (LogGap)
            var extractor = new LogGapFeatureExtractor(gapShift: 0);
            // Gap=0 means Feature = Log(Target/Target) = Log(1) = 0?
            // Wait, Log(Gap/Target). If Gap=Target, Log(1)=0.
            // Features = 0.
            // Scores = 0.
            // Result? Tie.

            // Let's use Gap=1, Band=10. GapBand=9.
            // Input: Band 10 = 200, Band 9 = 100.
            // Feat = Log(100/200) = -0.693.
            // Class 0 Score: 10 * -0.693 = -6.93
            // Class 1 Score: -10 * -0.693 = +6.93 -> Winner.

            extractor = new LogGapFeatureExtractor(gapShift: 1);
            pipeline.SetFeatureExtractor(extractor);

            // Configure
            pipeline.Configure(rawBandCount: 200, selectedBands: config.SelectedBands);

            // Input Data (200 Bands)
            ushort[] input = new ushort[200];
            input[10] = 200; // Target
            input[9] = 100;  // Gap

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
