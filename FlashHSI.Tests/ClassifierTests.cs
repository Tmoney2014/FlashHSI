using FlashHSI.Core;
using Newtonsoft.Json;

namespace FlashHSI.Tests
{
    public class ClassifierTests
    {
        [Fact]
        public unsafe void Predict_Identifies_Class_Correctly()
        {
            // Arrange: Create a minimal model known to favor Class 0
            var config = new ModelConfig
            {
                ModelType = "LinearModel",
                SelectedBands = new List<int> { 0, 1 }, // Band 0 and 1
                Preprocessing = new PreprocessingConfig { Gap = 0 }, // Simplify: Log(Gap/Target) -> Gap=Target -> Log(1)=0 -> Score=0
                Weights = new List<List<double>>(),
                Bias = new List<double>()
            };

            // 2 Classes, 2 Bands
            // Class 0: Weight [10, 10], Bias 0 -> Score = 10*F0 + 10*F1
            // Class 1: Weight [-10, -10], Bias 0 -> Score = -10*F0 - 10*F1
            config.Weights.Add(new List<double> { 10.0, 10.0 });
            config.Bias.Add(0.0);

            config.Weights.Add(new List<double> { -10.0, -10.0 });
            config.Bias.Add(0.0);

            string json = JsonConvert.SerializeObject(config);
            var classifier = new FlashHSI.Core.Classifiers.LinearClassifier();
            classifier.Load(config);

            // Input (Simulated Features)
            double[] input = new double[1] { -0.693 };

            // Act
            int result = -1;
            fixed (double* p = input)
            {
                result = classifier.Predict(p, 1);
            }

            // Assert
            // Feature = Log(100/200) = -0.693
            // Class 0 Score = 10 * -0.693 = -6.93
            // Class 1 Score = -10 * -0.693 = 6.93
            // Class 1 is significantly higher.
            Assert.Equal(1, result);
        }
    }
}
