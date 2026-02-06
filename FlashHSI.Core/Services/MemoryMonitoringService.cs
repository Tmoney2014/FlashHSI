using System;
using System.Diagnostics;
using System.Timers;
using Serilog;

namespace FlashHSI.Core.Services
{
    /// <summary>
    /// <ai>AI가 작성함</ai>
    /// 메모리 사용량 및 GC 성능을 모니터링하는 서비스입니다.
    /// </summary>
    public class MemoryMonitoringService : IDisposable
    {
        private readonly System.Timers.Timer _timer;
        private readonly long _thresholdBytes;
        private bool _disposed;

        public MemoryMonitoringService(int intervalMs = 30000, long thresholdMb = 1000)
        {
            _thresholdBytes = thresholdMb * 1024 * 1024;
            _timer = new System.Timers.Timer(intervalMs);
            _timer.Elapsed += OnTick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void OnTick(object? sender, ElapsedEventArgs e)
        {
            if (_disposed) return;

            long totalMemory = GC.GetTotalMemory(false);
            var process = Process.GetCurrentProcess();
            long workingSet = process.WorkingSet64;

            Log.Information("Memory Monitor - Total: {TotalMB}MB, WorkingSet: {WorkingMB}MB", 
                totalMemory / (1024 * 1024), 
                workingSet / (1024 * 1024));

            if (totalMemory > _thresholdBytes)
            {
                Log.Warning("High memory usage detected. Forcing cleanup.");
                ForceCleanup();
            }
        }

        public void ForceCleanup()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Log.Information("Manual GC Cleanup performed.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
