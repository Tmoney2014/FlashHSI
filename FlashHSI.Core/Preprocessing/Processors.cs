using FlashHSI.Core.Interfaces;
using System;

namespace FlashHSI.Core.Preprocessing
{
    public unsafe class L2NormalizeProcessor : IHsiFrameProcessor
    {
        public void Process(double* features, int length)
        {
            double sumSq = 0;
            for (int i = 0; i < length; i++) sumSq += features[i] * features[i];

            if (sumSq > 1e-9)
            {
                double norm = Math.Sqrt(sumSq);
                for (int i = 0; i < length; i++) features[i] /= norm;
            }
        }
    }

    public unsafe class MinMaxProcessor : IHsiFrameProcessor
    {
        public void Process(double* features, int length)
        {
            double min = double.MaxValue;
            double max = double.MinValue;
            for (int i = 0; i < length; i++)
            {
                if (features[i] < min) min = features[i];
                if (features[i] > max) max = features[i];
            }

            double range = max - min;
            if (range > 1e-9)
            {
                for (int i = 0; i < length; i++) features[i] = (features[i] - min) / range;
            }
        }
    }

    public unsafe class SnvProcessor : IHsiFrameProcessor
    {
        public void Process(double* features, int length)
        {
            double sum = 0;
            for (int i = 0; i < length; i++) sum += features[i];
            double mean = sum / length;

            double sumSqDiff = 0;
            for (int i = 0; i < length; i++)
            {
                double d = features[i] - mean;
                sumSqDiff += d * d;
            }
            // Academic Standard: Sample Standard Deviation (N-1)
            double std = Math.Sqrt(sumSqDiff / (length - 1));

            if (std > 1e-9)
            {
                for (int i = 0; i < length; i++) features[i] = (features[i] - mean) / std;
            }
        }
    }
}
