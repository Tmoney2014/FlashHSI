using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Serilog;

namespace FlashHSI.Core.Memory;

/// <summary>
/// <ai>AI가 작성함: HSIClient에서 포팅</ai>
/// 고빈도 이미징 작업에서 할당을 줄이기 위한 BGR24 이미지 데이터 전용 버퍼 풀입니다.
/// 풀링 전략:
/// - 64KB 이하 버퍼만 풀링 (LOH 경계인 85KB 미만으로 유지)
/// - 64KB 초과 버퍼는 GC가 자동 처리
/// - 이유: LOH 할당을 피하고 Gen0/Gen1에서 효율적인 메모리 관리
/// </summary>
public class BgrBufferPool : IDisposable
{
    private static readonly Lazy<BgrBufferPool> _instance = new(() => new BgrBufferPool());
    private readonly ConcurrentDictionary<int, int> _currentPoolCounts = new();
    private readonly int _maxPoolSize = 50; // 크기별 풀당 최대 버퍼 개수

    // 풀 크기 배열 - LOH(Large Object Heap) 경계인 85KB 미만으로 설정
    // 85KB 이상은 Gen2에서만 정리되므로 풀링 효율이 떨어짐
    // 64KB(65536 bytes)를 최대로 설정하여 LOH 할당을 피하고 Gen0/Gen1에서 효율적으로 관리
    private readonly int[] _poolSizes = { 1024, 2048, 4096, 8192, 16384, 32768, 65536 };

    // 다양한 BGR 버퍼 크기별 크기 기반 풀
    private readonly ConcurrentQueue<PooledBgrBuffer>[] _sizePools;
    private bool _disposed;
    private long _poolHits;
    private long _poolMisses;

    // 통계
    private long _totalRents;
    private long _totalReturns;

    private BgrBufferPool()
    {
        _sizePools = new ConcurrentQueue<PooledBgrBuffer>[_poolSizes.Length];
        for (var i = 0; i < _poolSizes.Length; i++)
        {
            _sizePools[i] = new ConcurrentQueue<PooledBgrBuffer>();
            _currentPoolCounts[_poolSizes[i]] = 0;
        }
    }

    public static BgrBufferPool Instance => _instance.Value;

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 버퍼 풀 통계를 가져옵니다
    /// </summary>
    public BgrBufferPoolStats Stats => new()
    {
        TotalRents = _totalRents,
        TotalReturns = _totalReturns,
        PoolHits = _poolHits,
        PoolMisses = _poolMisses,
        HitRate = _totalRents > 0 ? (double)_poolHits / _totalRents : 0.0,
        PoolCounts = new Dictionary<int, int>(_currentPoolCounts)
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 최종 통계 로깅
        var stats = Stats;
        Log.Information("BgrBufferPool disposal - Rents: {TotalRents}, Returns: {TotalReturns}, Hit Rate: {HitRate:P2}",
            stats.TotalRents, stats.TotalReturns, stats.HitRate);

        // 모든 풀 지우기
        for (var i = 0; i < _sizePools.Length; i++)
            while (_sizePools[i].TryDequeue(out _))
            {
                // 지우기 위해 대기열에서 제거, 버퍼들은 GC로 처리됨
            }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 지정된 크기의 BGR 버퍼를 대여합니다
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledBgrBuffer RentBgrBuffer(int width, int height)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BgrBufferPool));

        var stride = width * 3; // BGR24 = 픽셀당 3바이트
        var bufferSize = height * stride;

        Interlocked.Increment(ref _totalRents);

        // 적절한 크기 풀에서 가져오기 시도
        var poolIndex = GetPoolIndex(bufferSize);
        if (poolIndex >= 0 && _sizePools[poolIndex].TryDequeue(out var pooledBuffer))
        {
            Interlocked.Increment(ref _poolHits);

            // 풀 카운트 감소 - GetOrAdd 결과를 직접 ref로 전달할 수 없으므로 AddOrUpdate 사용
            var poolSize = _poolSizes[poolIndex];
            _currentPoolCounts.AddOrUpdate(poolSize, 0, (key, value) => Math.Max(0, value - 1));

            // 기존 버퍼 재사용, 메타데이터만 업데이트
            return new PooledBgrBuffer(pooledBuffer.Buffer, width, height, stride, this, true);
        }

        Interlocked.Increment(ref _poolMisses);

        // 풀 미스 시 적절한 풀 크기로 버퍼 생성하여 재사용 가능하도록 함
        if (poolIndex >= 0) // bufferSize <= 65536 (64KB 이하)
        {
            // 풀 크기에 맞는 버퍼 생성 - 나중에 풀로 반환 가능
            // 예: bufferSize=3000 요청 시 -> 4096 크기로 생성하여 재사용 가능하게 함
            var poolSize = _poolSizes[poolIndex];
            var newBuffer = new byte[poolSize];
            return new PooledBgrBuffer(newBuffer, width, height, stride, this, true); // isPooled=true
        }
        else // poolIndex == -1, bufferSize > 65536 (64KB 초과)
        {
            // 64KB를 초과하는 큰 버퍼는 풀링하지 않음
            // 이유:
            // 1. 85KB 이상은 LOH(Large Object Heap)에 할당되어 Gen2에서만 정리됨
            // 2. 드물게 사용되는 큰 이미지를 위해 메모리를 예약해두는 것은 비효율적
            // 3. GC가 자동으로 처리하도록 위임하는 것이 더 효율적
            var newBuffer = new byte[bufferSize];
            return new PooledBgrBuffer(newBuffer, width, height, stride, this, false); // isPooled=false (GC가 처리)
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetExactPoolIndex(int bufferSize)
    {
        // 풀로 반환하기 위한 정확한 일치 찾기
        for (var i = 0; i < _poolSizes.Length; i++)
            if (bufferSize == _poolSizes[i])
                return i;
        return -1;
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 요청된 버퍼 크기에 적합한 풀 인덱스를 찾습니다.
    /// </summary>
    /// <param name="bufferSize">필요한 버퍼 크기</param>
    /// <returns>풀 인덱스 (0~6), 65536초과 시 -1 반환</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetPoolIndex(int bufferSize)
    {
        // 버퍼 크기를 수용할 수 있는 첫 번째 풀 찾기
        // 예: bufferSize=3000 -> poolSizes[2]=4096 선택 (index=2)
        for (var i = 0; i < _poolSizes.Length; i++)
            if (bufferSize <= _poolSizes[i])
                return i;

        // 65536(64KB) 초과 시 -1 반환
        // 이런 큰 버퍼는 LOH에 할당될 가능성이 높으므로 풀링하지 않음
        return -1;
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// BGR 버퍼를 적절한 풀로 반환합니다
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ReturnBgrBuffer(PooledBgrBuffer buffer)
    {
        if (_disposed || buffer.Buffer == null)
            return;

        Interlocked.Increment(ref _totalReturns);

        var bufferSize = buffer.Buffer.Length;
        var poolIndex = GetExactPoolIndex(bufferSize);

        // 풀 크기가 일치하고 풀이 가득 차지 않은 경우에만 풀로 반환
        if (poolIndex >= 0)
        {
            var poolSize = _poolSizes[poolIndex];
            var currentCount = _currentPoolCounts.GetOrAdd(poolSize, 0);

            if (currentCount < _maxPoolSize)
            {
                // 데이터 유출 방지를 위해 버퍼 지우기 (성능 최적화: 작은 버퍼만 지우기)
                if (buffer.Buffer.Length <= 8192) // 8KB 이하만 지우기
                    Array.Clear(buffer.Buffer, 0, buffer.Buffer.Length);
                // 큰 버퍼는 성능상 지우지 않음 (BGR 데이터는 매번 덮어쓰워지므로 안전)

                _sizePools[poolIndex].Enqueue(buffer);

                // 풀 카운트 증가 - AddOrUpdate를 사용하여 스레드 안전하게 증가
                _currentPoolCounts.AddOrUpdate(poolSize, 1, (key, value) => value + 1);
            }
        }
        // 버퍼가 풀 크기와 일치하지 않거나 풀이 가득 찬 경우 GC가 처리하도록 함
    }

    ~BgrBufferPool()
    {
        Dispose();
    }
}

/// <summary>
/// <ai>AI가 작성함: HSIClient에서 포팅</ai>
/// 해제 시 자동으로 풀로 반환되는 풀링된 BGR 버퍼
/// </summary>
public readonly struct PooledBgrBuffer : IDisposable
{
    public byte[] Buffer { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public bool IsPooled { get; }

    private readonly BgrBufferPool _pool;

    internal PooledBgrBuffer(byte[] buffer, int width, int height, int stride, BgrBufferPool pool, bool isPooled)
    {
        Buffer = buffer;
        Width = width;
        Height = height;
        Stride = stride;
        _pool = pool;
        IsPooled = isPooled;
    }

    /// <summary>
    /// <ai>AI가 작성함: HSIClient에서 포팅</ai>
    /// 이 풀링된 버퍼로부터 Bgr24Buffer를 생성합니다
    /// 참고: 반환된 Bgr24Buffer는 동일한 기본 배열을 공유합니다
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Bgr24Buffer ToBgr24Buffer()
    {
        return new Bgr24Buffer(Width, Height, Stride, Buffer);
    }

    public void Dispose()
    {
        if (IsPooled)
            _pool?.ReturnBgrBuffer(this);
    }
}

/// <summary>
/// <ai>AI가 작성함: FlashHSI용 BGR24 버퍼 구조체</ai>
/// BGR24 이미지 데이터를 위한 경량 버퍼입니다.
/// </summary>
public readonly struct Bgr24Buffer
{
    public byte[] Buffer { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }

    public Bgr24Buffer(int width, int height, int stride, byte[] buffer)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Buffer = buffer;
    }
}

/// <summary>
/// <ai>AI가 작성함: HSIClient에서 포팅</ai>
/// BGR 버퍼 풀 통계
/// </summary>
public readonly struct BgrBufferPoolStats
{
    public long TotalRents { get; init; }
    public long TotalReturns { get; init; }
    public long PoolHits { get; init; }
    public long PoolMisses { get; init; }
    public double HitRate { get; init; }
    public Dictionary<int, int> PoolCounts { get; init; }
}
