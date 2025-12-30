using FlashHSI.Core;
using Newtonsoft.Json;
using System.Diagnostics;
using Xunit.Abstractions;

namespace FlashHSI.Tests
{
    public class BenchmarkTests
    {
        private readonly ITestOutputHelper _output;

        public BenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private string CreateMockModel()
        {
            // Create a realistic-sized mock model (e.g., 200 input bands, 3 classes, 5 selected bands)
            var config = new ModelConfig
            {
                ModelType = "LinearModel",
                Weights = new List<List<double>>(),
                Bias = new List<double>(),
                SelectedBands = new List<int> { 10, 30, 50, 70, 90 },
                Preprocessing = new PreprocessingConfig { Gap = 5 }
            };

            // 3 Classes
            for (int c = 0; c < 3; c++)
            {
                var w = new List<double>();
                // 200 Input Bands
                for (int b = 0; b < 200; b++) w.Add(0.123);
                config.Weights.Add(w);
                config.Bias.Add(0.5);
            }

            return JsonConvert.SerializeObject(config);
        }

        [Fact]
        public unsafe void PerformanceTest_Under_1_4ms()
        {
            // Arrange
            string json = CreateMockModel();
            var config = JsonConvert.DeserializeObject<ModelConfig>(json);

            var classifier = new FlashHSI.Core.Classifiers.LinearClassifier();
            classifier.Load(config);

            int totalBands = 200;
            // Features (5 bands) are input to LinearClassifier, NOT raw bands
            // Wait, LinearClassifier expects 'features' sized to Weights columns (200 in mock model?)
            // MockModel in lines 34 says "for (int b = 0; b < 200; b++)". So 200 input features.

            double[] featureData = new double[totalBands];
            Array.Fill(featureData, 0.5);

            int iterations = 100_000;

            // Warmup
            fixed (double* ptr = featureData)
            {
                for (int i = 0; i < 100; i++) classifier.Predict(ptr, totalBands);
            }

            // Act
            Stopwatch sw = Stopwatch.StartNew();
            fixed (double* ptr = featureData)
            {
                for (int i = 0; i < iterations; i++)
                {
                    classifier.Predict(ptr, totalBands);
                }
            }
            sw.Stop();

            // Assert & Report
            double totalTimeMs = sw.Elapsed.TotalMilliseconds;
            double avgTimeMs = totalTimeMs / iterations;
            double avgTimeUs = avgTimeMs * 1000.0;

            _output.WriteLine($"Total Time: {totalTimeMs:F2} ms for {iterations} frames");
            _output.WriteLine($"Average Time per Frame: {avgTimeUs:F4} μs ({avgTimeMs:F6} ms)");

            // Rigid Constraint: Must be well under 1.4ms (e.g., target 100us for pure logic)
            // 0.005ms = 5us. 
            // If it takes more than 0.1ms (100us), we might need optimizations depending on overheads.
            // But strict requirement is 1.4ms.
            Assert.True(avgTimeMs < 1.4, $"Performance failed! Avg: {avgTimeMs} ms >= 1.4 ms");

            // Stronger target for pure logic: 0.05ms (50us)
            // Assert.True(avgTimeMs < 0.05, $"Optimization Target missed! Avg: {avgTimeMs} ms");
        }
    }
}
