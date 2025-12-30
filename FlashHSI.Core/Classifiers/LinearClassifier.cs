using FlashHSI.Core.Interfaces;
using System.Runtime.CompilerServices;

namespace FlashHSI.Core.Classifiers
{
    /// <summary>
    /// Generic Linear Classifier supporting LDA, Linear SVM, and PLS-DA.
    /// Logic: Score[c] = (Features . Weights[c]) + Bias[c]
    /// </summary>
    public unsafe class LinearClassifier : IClassifier
    {
        private double[] _flattenedWeights = Array.Empty<double>();
        private double[] _biases = Array.Empty<double>();
        private int _classCount;
        private int _featureCount;
        private double _confidenceThreshold = 0.75;
        private const double NegativeInfinity = -999.0;
        private string _modelType = "Linear";
        private string _originalType = "";

        public void Load(ModelConfig config)
        {
            _modelType = config.ModelType;
            _originalType = config.OriginalType ?? "";
            _classCount = config.Weights.Count;
            if (_classCount == 0) return;

            _featureCount = config.Weights[0].Count;

            // Flatten Weights for cache locality
            // Weights[c][f] -> flattened[c * featureCount + f]
            _flattenedWeights = new double[_classCount * _featureCount];
            for (int c = 0; c < _classCount; c++)
            {
                for (int f = 0; f < _featureCount; f++)
                {
                    _flattenedWeights[c * _featureCount + f] = config.Weights[c][f];
                }
            }

            _biases = config.Bias.ToArray();
        }

        public void SetThreshold(double threshold) => _confidenceThreshold = threshold;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Predict(double* features, int length)
        {
            if (length != _featureCount) return -1;

            // Stackalloc for class scores (small allocation)
            double* classScores = stackalloc double[_classCount];

            // 1. Calculate Linear Scores (Dot Product + Bias)
            for (int c = 0; c < _classCount; c++)
            {
                double dotProduct = 0.0;
                int weightOffset = c * _featureCount;

                for (int f = 0; f < _featureCount; f++)
                {
                    dotProduct += _flattenedWeights[weightOffset + f] * features[f];
                }

                dotProduct += _biases[c];
                classScores[c] = dotProduct;
            }

            // 2. Decision Logic
            // SVM/PLS (Regression/Distance) -> ArgMax (Ignore Threshold)
            // ModelType is often "LinearModel" for all, so we check OriginalType.
            if (_originalType.Contains("PLS") || _originalType.Contains("SVM") || _originalType.Contains("SVC"))
            {
                return ArgMax(classScores);
            }
            else
            {
                // LDA / Default -> Softmax -> Threshold
                return SoftmaxAndThreshold(classScores);
            }
        }

        private int ArgMax(double* scores)
        {
            double maxVal = NegativeInfinity;
            int maxIdx = -1;
            for (int c = 0; c < _classCount; c++)
            {
                if (scores[c] > maxVal)
                {
                    maxVal = scores[c];
                    maxIdx = c;
                }
            }
            // For SVM/PLS, if max score is too low? 
            // Maybe apply threshold if we interpret scores as confidence?
            // Let's assume user wants raw argmax for now unless logic refines.
            return maxIdx;
        }

        private int SoftmaxAndThreshold(double* scores)
        {
            // Softmax
            double maxLogit = NegativeInfinity;
            for (int c = 0; c < _classCount; c++) if (scores[c] > maxLogit) maxLogit = scores[c];

            double sumExp = 0.0;
            for (int c = 0; c < _classCount; c++)
            {
                double expVal = Math.Exp(scores[c] - maxLogit);
                sumExp += expVal;
                scores[c] = expVal;
            }

            // Find Max Prob
            double maxProb = 0.0;
            int predictedClass = -1;
            for (int c = 0; c < _classCount; c++)
            {
                double prob = scores[c] / sumExp;
                if (prob > maxProb)
                {
                    maxProb = prob;
                    predictedClass = c;
                }
            }

            return (maxProb >= _confidenceThreshold) ? predictedClass : -1;
        }
    }
}
