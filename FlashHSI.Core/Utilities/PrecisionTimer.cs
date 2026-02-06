using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FlashHSI.Core.Utilities
{
    public static class PrecisionTimer
    {
        /// <summary>
        /// Hybrid Precision Wait (Task.Delay + SpinWait)
        /// - Low CPU usage for long waits (> 20ms)
        /// - High precision (microsecond level) for short waits
        /// </summary>
        public static async Task WaitAsync(int milliseconds, CancellationToken token = default)
        {
            if (milliseconds <= 0) return;

            long startTicks = Stopwatch.GetTimestamp();
            long targetTicks = startTicks + (long)((double)milliseconds * Stopwatch.Frequency / 1000.0);

            // 1. Long Wait: Use Task.Delay to yield CPU
            // Windows Task.Delay resolution is approx 15.6ms. We leave 15ms safety margin.
            if (milliseconds > 20)
            {
                int delayMs = milliseconds - 15;
                await Task.Delay(delayMs, token);
            }

            // 2. Short Wait / Remaining Time: SpinWait (Busy Wait)
            while (Stopwatch.GetTimestamp() < targetTicks)
            {
                if (token.IsCancellationRequested) return;
                Thread.SpinWait(10); // Very short spin (approx 1us - 50ns depending on CPU)
            }
        }

        public static void Wait(int milliseconds, CancellationToken token = default)
        {
             if (milliseconds <= 0) return;
             
             long startTicks = Stopwatch.GetTimestamp();
             long targetTicks = startTicks + (long)((double)milliseconds * Stopwatch.Frequency / 1000.0);
             
             while (Stopwatch.GetTimestamp() < targetTicks)
             {
                 if (token.IsCancellationRequested) return;
                 Thread.SpinWait(10);
             }
        }
    }
}
