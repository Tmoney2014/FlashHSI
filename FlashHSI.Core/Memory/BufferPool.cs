using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Serilog;

namespace FlashHSI.Core.Memory;

/// <summary>
/// <ai>AI가 작성함: HSIClient에서 포팅</ai>
/// 스트리밍 작업에서 할당을 최소화하고 GC 압박을 줄이기 위해
/// 다양한 버퍼 크기를 관리하는 고성능 버퍼 풀 구현체
/// </summary>
public class BufferPool : IDisposable
{
    private static readonly Lazy<BufferPool> _instance = new(() => new BufferPool());
    private readonly ArrayPool<byte> _hugePool = ArrayPool<byte>.Create(1048576, 10); // 최대 1MB
    private readonly ArrayPool<byte> _largePool = ArrayPool<byte>.Create(65536, 20); // 최대 64KB
    private readonly ArrayPool<byte> _mediumPool = ArrayPool<byte>.Create(8192, 30); // 최대 8KB  

    // 정리를 위한 대여된 버퍼 추적
    private readonly ConcurrentBag<(byte[] buffer, ArrayPool<byte> pool)> _rentedBuffers = new();

    // 다양한 크기 카테고리별 배열 풀
    private readonly ArrayPool<byte> _smallPool = ArrayPool<byte>.Create(1024, 50); // 최대 1KB
    private long _activeBuffers;
    private bool _disposed;

    // 풀 통계
    private long _totalRents;
    private long _totalReturns;

    private BufferPool()
    {
    }

    public static BufferPool Instance => _instance.Value;

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 풀 사용 통계를 가져옵니다
    /// </summary>
    public BufferPoolStats Stats => new()
    {
        TotalRents = _totalRents,
        TotalReturns = _totalReturns,
        ActiveBuffers = _activeBuffers
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Log final statistics
        Log.Information("BufferPool disposal: Rents={TotalRents}, Returns={TotalReturns}, Active={ActiveBuffers}",
            _totalRents, _totalReturns, _activeBuffers);

        if (_activeBuffers > 0)
        {
            Log.Warning("BufferPool disposed with {ActiveBuffers} unreturned buffers", _activeBuffers);
            ForceCleanup();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 모든 대여된 버퍼의 강제 정리 (비상시에만 사용)
    /// </summary>
    public void ForceCleanup()
    {
        Log.Warning("Forcing buffer pool cleanup - this indicates a potential memory leak");

        while (_rentedBuffers.TryTake(out var item))
            try
            {
                Array.Clear(item.buffer, 0, item.buffer.Length);
                item.pool.Return(item.buffer);
                Interlocked.Decrement(ref _activeBuffers);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during force cleanup of buffer");
            }
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 지정된 최소 길이 이상의 버퍼를 대여합니다
    /// </summary>
    /// <param name="minimumLength">필요한 최소 버퍼 크기</param>
    /// <returns>요청된 크기보다 클 수 있는 풀링된 버퍼</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledBuffer RentBuffer(int minimumLength)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BufferPool));

        var pool = GetPoolForSize(minimumLength);
        var buffer = pool.Rent(minimumLength);

        Interlocked.Increment(ref _totalRents);
        Interlocked.Increment(ref _activeBuffers);

        _rentedBuffers.Add((buffer, pool));

        return new PooledBuffer(buffer, minimumLength, this, pool);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ArrayPool<byte> GetPoolForSize(int size)
    {
        return size switch
        {
            <= 1024 => _smallPool,
            <= 8192 => _mediumPool,
            <= 65536 => _largePool,
            _ => _hugePool
        };
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 버퍼를 적절한 풀로 반환합니다
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ReturnBuffer(byte[] buffer, ArrayPool<byte> pool)
    {
        if (_disposed || buffer == null)
            return;

        try
        {
            // 데이터 유출 방지를 위해 반환하기 전 버퍼를 지웁니다
            Array.Clear(buffer, 0, buffer.Length);
            pool.Return(buffer);

            Interlocked.Increment(ref _totalReturns);
            Interlocked.Decrement(ref _activeBuffers);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to return buffer to pool");
        }
    }

    ~BufferPool()
    {
        Dispose();
    }
}

/// <summary>
/// <ai>AI가 작성함: HSIClient에서 포팅</ai>
/// A pooled buffer that automatically returns to the pool when disposed
/// </summary>
public readonly struct PooledBuffer : IDisposable
{
    private readonly BufferPool _pool;
    private readonly ArrayPool<byte> _arrayPool;

    public byte[] Buffer { get; }

    public int Length { get; }

    internal PooledBuffer(byte[] buffer, int length, BufferPool pool, ArrayPool<byte> arrayPool)
    {
        Buffer = buffer;
        Length = length;
        _pool = pool;
        _arrayPool = arrayPool;
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// Gets a span view of the usable portion of the buffer
    /// </summary>
    public Span<byte> Span => Buffer.AsSpan(0, Length);

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// Gets a memory view of the usable portion of the buffer
    /// </summary>
    public Memory<byte> Memory => Buffer.AsMemory(0, Length);

    public void Dispose()
    {
        _pool?.ReturnBuffer(Buffer, _arrayPool);
    }
}

/// <summary>
/// <ai>AI가 작성함: HSIClient에서 포팅</ai>
/// Buffer pool usage statistics
/// </summary>
public readonly struct BufferPoolStats
{
    public long TotalRents { get; init; }
    public long TotalReturns { get; init; }
    public long ActiveBuffers { get; init; }

    public double ReturnRate => TotalRents > 0 ? (double)TotalReturns / TotalRents : 0.0;
}
