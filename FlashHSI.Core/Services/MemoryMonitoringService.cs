using System.Diagnostics;
using System.Timers;
using FlashHSI.Core.Memory;
using Serilog;
using Timer = System.Timers.Timer;

namespace FlashHSI.Core.Services;

/// <summary>
/// <ai>AI가 작성함: HSIClient에서 포팅 + 강화</ai>
/// 메모리 사용량, 버퍼 풀, GC 성능을 모니터링하는 서비스입니다.
/// </summary>
public class MemoryMonitoringService : IDisposable
{
    private readonly object _lock = new();
    private readonly long _memoryThresholdMB;

    // 모니터링 설정
    private readonly int _monitoringIntervalMs;
    private readonly Timer _monitoringTimer;
    private bool _disposed;
    private bool _isEnabled;

    // AI가 추가함: 실시간 GC 정밀 추적용 폴링 스레드
    private Thread? _gcPollThread;
    private double _periodMaxGcPauseMs;
    private double _periodTotalGcPauseMs;
    private long _periodTotalGcCount;

    // 메모리 메트릭
    private long _lastGen0Collections;
    private long _lastGen1Collections;
    private long _lastGen2Collections;
    private long _lastTotalMemory;
    private long _peakMemoryUsage;
    private TimeSpan _lastGCPauseTime; // AI가 추가함: 2ms 실시간 모니터링용

    public MemoryMonitoringService(int intervalMs = 30000, long thresholdMb = 1000)
    {
        _monitoringIntervalMs = intervalMs;
        _memoryThresholdMB = thresholdMb * 1024 * 1024; // 바이트로 변환

        _monitoringTimer = new Timer(_monitoringIntervalMs);
        _monitoringTimer.Elapsed += OnMonitoringTick;

        InitializeBaseline();
        StartGcPollingThread(); // AI가 추가함: 1ms 폴링 모니터링 시작

        Log.Information(
            "MemoryMonitoringService initialized with {IntervalMs}ms interval and {ThresholdMB}MB threshold",
            intervalMs, thresholdMb);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        StopMonitoring();
        _monitoringTimer?.Dispose();

        // Log final statistics
        var finalStats = GetCurrentStats();
        Log.Information("MemoryMonitoringService disposed. Final stats - Memory: {MemoryMB}MB, Peak: {PeakMB}MB",
            finalStats.TotalMemoryBytes / (1024 * 1024),
            finalStats.PeakMemoryBytes / (1024 * 1024));

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 가비지 컴렉션과 버퍼 풀 정리를 강제로 수행합니다
    /// </summary>
    public void ForceCleanup()
    {
        Log.Warning("Force cleanup initiated");

        try
        {
            // 반환되지 않은 버퍼가 있으면 버퍼 풀 정리
            var bufferStats = BufferPool.Instance.Stats;
            if (bufferStats.ActiveBuffers > 100)
            {
                Log.Warning("High active buffer count detected: {ActiveBuffers}", bufferStats.ActiveBuffers);
                BufferPool.Instance.ForceCleanup();
            }

            // 전체 GC 강제 실행
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var newMemory = GC.GetTotalMemory(true);
            Log.Information("Force cleanup completed. Memory after cleanup: {MemoryMB}MB", newMemory / (1024 * 1024));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during force cleanup");
        }
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 현재 메모리 통계를 가져옵니다
    /// </summary>
    public MemoryStats GetCurrentStats()
    {
        var process = Process.GetCurrentProcess();
        var totalMemory = GC.GetTotalMemory(false);
        var gcInfo = GC.GetGCMemoryInfo(); // AI가 추가함: GC 상세 정보 가져오기

        return new MemoryStats
        {
            TotalMemoryBytes = totalMemory,
            WorkingSetBytes = process.WorkingSet64,
            PrivateMemoryBytes = process.PrivateMemorySize64,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            PeakMemoryBytes = _peakMemoryUsage,
            BufferPoolStats = BufferPool.Instance.Stats,
            Timestamp = DateTime.UtcNow,
            GCPauseTimeMs = gcInfo.PauseTimePercentage, // .NET 8에서는 %로 제공됨, 런타임 지연 추산용
            GCTotalPauseDuration = gcInfo.PauseDurations.ToArray() // AI가 추가함: 최근 GC의 실제 STW(Stop-The-World) 시간들
        };
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 메모리 모니터링을 시작합니다
    /// </summary>
    public void StartMonitoring()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryMonitoringService));

        lock (_lock)
        {
            if (!_isEnabled)
            {
                _isEnabled = true;
                _monitoringTimer.Start();
                Log.Information("Memory monitoring started");
            }
        }
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 메모리 모니터링을 중지합니다
    /// </summary>
    public void StopMonitoring()
    {
        lock (_lock)
        {
            if (_isEnabled)
            {
                _isEnabled = false;
                _monitoringTimer.Stop();
                Log.Information("Memory monitoring stopped");
            }
        }
    }

    private void InitializeBaseline()
    {
        _lastGen0Collections = GC.CollectionCount(0);
        _lastGen1Collections = GC.CollectionCount(1);
        _lastGen2Collections = GC.CollectionCount(2);
        _lastTotalMemory = GC.GetTotalMemory(false);
        _peakMemoryUsage = _lastTotalMemory;

        var gcInfo = GC.GetGCMemoryInfo();
        if (gcInfo.PauseDurations.Length > 0)
        {
            _lastGCPauseTime = gcInfo.PauseDurations[^1]; // 가장 마지막 GC 일시정지 시간
        }
    }

    /// <summary>
    /// AI가 추가함: 1ms마다 GC.Index를 확인하여 GC 발생 시점과 STW 시간을 정밀 포착합니다.
    /// </summary>
    private void StartGcPollingThread()
    {
        _gcPollThread = new Thread(() =>
        {
            long lastGcIndex = GC.GetGCMemoryInfo().Index;
            while (!_disposed)
            {
                var info = GC.GetGCMemoryInfo();
                if (info.Index > lastGcIndex)
                {
                    double currentPauseMs = 0;
                    var durations = info.PauseDurations;
                    for (int i = 0; i < durations.Length; i++)
                    {
                        currentPauseMs += durations[i].TotalMilliseconds;
                    }

                    lock (_lock)
                    {
                        if (currentPauseMs > _periodMaxGcPauseMs) _periodMaxGcPauseMs = currentPauseMs;
                        _periodTotalGcPauseMs += currentPauseMs;
                        _periodTotalGcCount += (info.Index - lastGcIndex);
                    }

                    // 치명적 스파이크 감지 시 0.001초 내로 즉각 로깅
                    if (currentPauseMs > 2.0)
                    {
                        Log.Error("🚨 [2ms Violation] GC #{Index} (Gen {Generation}) STW: {PauseMs:F4} ms! (Concurrent: {IsConcurrent})",
                            info.Index, info.Generation, currentPauseMs, info.Concurrent);
                    }

                    lastGcIndex = info.Index;
                }
                Thread.Sleep(1);
            }
        })
        {
            IsBackground = true,
            Name = "GCPollingThread"
        };
        _gcPollThread.Start();
    }

    private void OnMonitoringTick(object sender, ElapsedEventArgs e)
    {
        if (_disposed)
            return;

        try
        {
            var stats = GetCurrentStats();

            // Update peak memory usage
            if (stats.TotalMemoryBytes > _peakMemoryUsage)
                _peakMemoryUsage = stats.TotalMemoryBytes;

            // Calculate deltas since last check
            var gen0Delta = stats.Gen0Collections - _lastGen0Collections;
            var gen1Delta = stats.Gen1Collections - _lastGen1Collections;
            var gen2Delta = stats.Gen2Collections - _lastGen2Collections;
            var memoryDelta = stats.TotalMemoryBytes - _lastTotalMemory;

            // Log periodic summary with detailed explanations
            double maxPause, totalPause;
            long realGcCount;
            lock (_lock)
            {
                maxPause = _periodMaxGcPauseMs;
                totalPause = _periodTotalGcPauseMs;
                realGcCount = _periodTotalGcCount;

                // 버퍼 초기화
                _periodMaxGcPauseMs = 0;
                _periodTotalGcPauseMs = 0;
                _periodTotalGcCount = 0;
            }

            Log.Information("Memory Monitor:\r\n" +
                            "  • Total: {TotalMB}MB (.NET GC 관리 힙 메모리)\r\n" +
                            "  • Working Set: {WorkingMB}MB (프로세스 전체 물리 RAM 사용량)\r\n" +
                            "  • GC Gen0: {Gen0Delta}회 (작은 객체 정리 횟수)\r\n" +
                            "  • GC Gen1: {Gen1Delta}회 (중간 생존 객체 정리 횟수)\r\n" +
                            "  • GC Gen2: {Gen2Delta}회 (장수 객체 정리 횟수 - 0이면 메모리 누수 없음)\r\n" +
                            "  • ⚠️ 30초 내 실제 포착된 GC: {RealGcCount}회\r\n" +
                            "  • ⚠️ 30초 내 최대 STW: {MaxPause:F4} ms (총합: {TotalPause:F4} ms)\r\n" +
                            "  • Active Buffers: {ActiveBuffers}개 (현재 반환안된 버퍼 수)\r\n" +
                            "  • Memory Delta: {MemoryDelta}MB (GC힙 메모리 증감량)",
                stats.TotalMemoryBytes / (1024 * 1024),
                stats.WorkingSetBytes / (1024 * 1024),
                gen0Delta, gen1Delta, gen2Delta,
                realGcCount, maxPause, totalPause,
                stats.BufferPoolStats.ActiveBuffers,
                memoryDelta / (1024 * 1024));

            // Check for memory pressure
            if (stats.TotalMemoryBytes > _memoryThresholdMB)
            {
                Log.Warning("Memory threshold exceeded: {MemoryMB}MB > {ThresholdMB}MB",
                    stats.TotalMemoryBytes / (1024 * 1024), _memoryThresholdMB / (1024 * 1024));

                // Consider automatic cleanup for extreme cases
                if (stats.TotalMemoryBytes > _memoryThresholdMB * 1.5)
                {
                    Log.Error("Extreme memory pressure detected, forcing cleanup");
                    ForceCleanup();
                }
            }

            // Check for excessive GC pressure
            if (gen2Delta > 5) Log.Warning("High Gen2 GC activity detected: {Gen2Count} collections", gen2Delta);

            // Check buffer pool health
            if (stats.BufferPoolStats.ReturnRate < 0.95 && stats.BufferPoolStats.TotalRents > 100)
                Log.Warning("Poor buffer return rate: {ReturnRate:P2}", stats.BufferPoolStats.ReturnRate);

            // Update baseline for next iteration
            _lastGen0Collections = stats.Gen0Collections;
            _lastGen1Collections = stats.Gen1Collections;
            _lastGen2Collections = stats.Gen2Collections;
            _lastTotalMemory = stats.TotalMemoryBytes;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during memory monitoring tick");
        }
    }
}

/// <summary>
/// <ai>AI가 작성함: HSIClient에서 포팅</ai>
/// Comprehensive memory statistics
/// </summary>
public readonly struct MemoryStats
{
    public long TotalMemoryBytes { get; init; }
    public long WorkingSetBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public long PeakMemoryBytes { get; init; }
    public long Gen0Collections { get; init; }
    public long Gen1Collections { get; init; }
    public long Gen2Collections { get; init; }
    public BufferPoolStats BufferPoolStats { get; init; }
    public DateTime Timestamp { get; init; }

    // AI가 추가함: GC 지연 분석용
    public double GCPauseTimeMs { get; init; }
    public TimeSpan[] GCTotalPauseDuration { get; init; }
}
