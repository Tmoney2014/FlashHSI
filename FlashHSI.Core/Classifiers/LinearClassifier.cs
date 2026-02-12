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
            // AI가 수정함: 모델 타입별 3분기 처리
            if (_originalType.Contains("PLS"))
            {
                return PlsThreshold(classScores);
            }
            else if (_originalType.Contains("SVM") || _originalType.Contains("SVC"))
            {
                return ArgMaxOnly(classScores);
            }
            else
            {
                // LDA / Default
                return SoftmaxAndThreshold(classScores);
            }
        }

        /// <ai>AI가 작성함: PLS-DA용 - 점수 직접 Threshold 비교 (소속도 ≈ 0~1)</ai>
        private int PlsThreshold(double* scores)
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

            // PLS-DA 점수는 0~1 근처이지만 초과/미달 가능 → clamp
            double confidence = Math.Clamp(maxVal, 0.0, 1.0);

            if ((_logCounter++ % 5000) == 0)
            {
                GlobalLog?.Invoke($"[PLS] Score: {confidence:P1} / Threshold: {_confidenceThreshold:P1} => {(confidence >= _confidenceThreshold ? $"Class: {maxIdx}" : "Unknown")}");
            }

            return (confidence >= _confidenceThreshold) ? maxIdx : -1;
        }

        /// <ai>AI가 작성함: SVM용 - ArgMax만 (Threshold 미적용, 거리값이라 비교 무의미)</ai>
        private int ArgMaxOnly(double* scores)
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

            if ((_logCounter++ % 5000) == 0)
            {
                GlobalLog?.Invoke($"[SVM] BestScore: {maxVal:F4} => Class: {maxIdx} (No Threshold)");
            }

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

            // AI: Debug Logging (Sampled) - Confidence Check
            if ((_logCounter++ % 5000) == 0) 
            {
               string msg = $"[LinearClassifier] MaxProb: {maxProb:P2} / Threshold: {_confidenceThreshold:P2} => Decided: {(maxProb >= _confidenceThreshold ? predictedClass : -1)}";
               GlobalLog?.Invoke(msg);
            }

            return (maxProb >= _confidenceThreshold) ? predictedClass : -1;
        }

        private static int _logCounter = 0;
        public static event Action<string>? GlobalLog;
    }
}
