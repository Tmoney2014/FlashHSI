using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EtherCAT.NET;
using EtherCAT.NET.Infrastructure;
using FlashHSI.Core.Utilities;

namespace FlashHSI.Core.Control.Hardware
{
    public class EtherCATService : IEtherCATService
    {
        // Legacy 패턴: 상태 머신 기반 채널 추적 (CAS 사용)
        private readonly ConcurrentDictionary<int, ChannelState> _channelStates = new();
        private readonly object _ioLock = new();
        
        private EcMaster? _master;
        private Thread? _realTimeThread;
        private CancellationTokenSource _ctsConnection = new();
        private EcSettings? _ecSettings;
        private List<CustomDigitalOut>? _digitalOuts;
        private List<SlaveInfo>? _slaves;
        private List<(int pdo, int pdoChannel)> _channelMap = new();
        private int _totalChannels;
        
        private volatile bool _isConnected;
        private volatile bool _isMasterOn; // AI가 추가함: 마스터 ON/OFF 플래그
        private CancellationTokenSource? _ctsAllChannelTest; // AI가 추가함: 전체 채널 테스트 취소용
        private int _cycleFrequency;

        public bool IsConnected => _isConnected;
        /// <ai>AI가 작성함</ai>
        public bool IsMasterOn => _isMasterOn;
        /// <ai>AI가 작성함</ai>
        public int TotalChannels => _totalChannels;
        public event Action<string>? LogMessage;

        /// <summary>
        /// 로깅辅助 메서드
        /// </summary>
        private void Log(string message)
        {
            LogMessage?.Invoke(message);
        }

        public void Connect(string interfaceName, int cycleFreq = 500)
        {
            if (_isConnected) return;
            
            _cycleFrequency = cycleFreq;
            _ctsConnection = new CancellationTokenSource();
            
            Task.Run(() => InitializeMaster(interfaceName));
        }

        private async Task InitializeMaster(string interfaceName)
        {
            try
            {
                var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var esiDirectoryPath = Path.Combine(localAppDataPath, "ESI");
                Directory.CreateDirectory(esiDirectoryPath);

                Log("Scanning Devices...");
                _ecSettings = new EcSettings((uint)_cycleFrequency, esiDirectoryPath, interfaceName);
                
                var rootSlave = EcUtilities.ScanDevices(interfaceName);
                await Task.Delay(200);
                
                foreach (var slave in rootSlave.Descendants().ToList())
                {
                    EcUtilities.CreateDynamicData(esiDirectoryPath, slave);
                    await Task.Delay(100);
                }

                _slaves = rootSlave.Descendants().ToList();
                _master = new EcMaster(_ecSettings);
                _master.Configure(rootSlave);

                // Init Custom Digital Outs
                _digitalOuts = new List<CustomDigitalOut>();
                foreach (var slave in _slaves)
                {
                    _digitalOuts.Add(new CustomDigitalOut(slave));
                }

                InitializeChannelMap();
                
                _isConnected = true;
                _isMasterOn = true; // AI가 추가함: 연결 시 마스터 ON 상태로 설정
                Log($"EtherCAT Connected. Total Channels: {_totalChannels}");

                // Start RealTime Thread
                _realTimeThread = new Thread(RealTimeLoop)
                {
                    Priority = ThreadPriority.Highest,
                    IsBackground = true,
                    Name = "EtherCAT_RT"
                };
                _realTimeThread.Start();
            }
            catch (Exception ex)
            {
                Log($"Connection Failed: {ex.Message}");
                _isConnected = false;
            }
        }

        private void InitializeChannelMap()
        {
            _channelMap.Clear();
            _totalChannels = 0;
            
            for (int i = 0; i < _slaves!.Count; i++)
            {
                int vars = _slaves[i].DynamicData.Pdos.Sum(p => p.Variables.Count);
                for (int c = 1; c <= vars; c++)
                {
                    _channelMap.Add((i, c));
                }
                _totalChannels += vars;
            }
        }

        private void RealTimeLoop()
        {
            var cycleMs = 1000.0 / _cycleFrequency;
            var sw = Stopwatch.StartNew();
            var nextCycle = sw.Elapsed.TotalMilliseconds + cycleMs;

            while (!_ctsConnection.IsCancellationRequested)
            {
                // AI가 수정함: _isMasterOn 체크 추가 — 마스터 OFF 시 IO 갱신 중지
                if (_isConnected && _isMasterOn && _master != null)
                {
                    lock (_ioLock)
                    {
                        _master.UpdateIO(DateTime.UtcNow);
                        ProcessChannelState();  // 채널 상태 처리 통합
                    }
                }

                // Precision Wait for Cycle
                var currentMs = sw.Elapsed.TotalMilliseconds;
                var waitMs = nextCycle - currentMs;
                if (waitMs > 0)
                {
                    if (waitMs > 16) Thread.Sleep((int)waitMs - 1);
                    while (sw.Elapsed.TotalMilliseconds < nextCycle)
                    {
                        Thread.SpinWait(10);
                    }
                }
                nextCycle += cycleMs;
            }
        }

        /// <summary>
        /// RealTimeLoop에서 2ms마다 호출하는 채널 상태 처리 (동기 메서드)
        /// Task.Delay를 사용하지 않고 RealTimeLoop에 통합하여 GC pressure zero 달성
        /// </summary>
        private void ProcessChannelState()
        {
            if (_channelStates.IsEmpty || _digitalOuts == null) return;

            var nowTicks = Stopwatch.GetTimestamp();

            foreach (var kv in _channelStates)
            {
                var channel = kv.Key;
                var state = kv.Value;
                var phase = (ChannelPhase)Volatile.Read(ref state.Phase);

                // channel index range check
                if (channel < 1 || channel > _channelMap.Count) continue;
                var (pdoIdx, pdoCh) = _channelMap[channel - 1];

                switch (phase)
                {
                    case ChannelPhase.TurningOn:
                        // 채널 ON으로 전환 (1회만 설정, 다음 사이클에서 On 상태 확인)
                        _digitalOuts![pdoIdx].SetChannel(pdoCh, true);
                        Volatile.Write(ref state.Phase, (int)ChannelPhase.On);
                        break;

                    case ChannelPhase.On:
                        // 만료 시간 확인 → 끄기 전환
                        if (nowTicks >= state.ExpiryTicks)
                        {
                            Volatile.Write(ref state.Phase, (int)ChannelPhase.TurningOff);
                        }
                        break;

                    case ChannelPhase.TurningOff:
                        // 채널 OFF로 전환
                        _digitalOuts![pdoIdx].SetChannel(pdoCh, false);
                        Volatile.Write(ref state.Phase, (int)ChannelPhase.Idle);
                        break;

                    case ChannelPhase.Idle:
                    default:
                        // 아무것도 안 함
                        break;
                }
            }
        }

        /// <summary>
        /// 채널을 발사합니다 (Legacy OnAndOffChannelAsyncV2 패턴 - CAS 상태 머신 사용)
        /// </summary>
        public async Task FireChannelAsync(int channel, int durationMs)
        {
            // 가드: 연결 안 됐거나 마스터 꺼져있으면 무시
            if (!_isConnected || !_isMasterOn || channel < 1 || channel > _totalChannels) return;

            var (pdoIdx, pdoCh) = _channelMap[channel - 1];
            var now = Stopwatch.GetTimestamp();
            var newExpiry = now + (long)(Stopwatch.Frequency * (durationMs / 1000.0));

            // 채널 상태 가져오기 또는 생성
            var state = _channelStates.GetOrAdd(channel, _ => new ChannelState());

            // Lock-free 상태 처리 - CAS 기반 상태 전환 (최대 10회 재시도)
            var handled = false;
            var maxRetries = 10;

            for (var retryIndex = 0; retryIndex < maxRetries && !handled; retryIndex++)
            {
                var currentPhase = (ChannelPhase)Volatile.Read(ref state.Phase);

                switch (currentPhase)
                {
                    case ChannelPhase.Idle:
                        // Idle에서 TurningOn으로 CAS 시도
                        if (Interlocked.CompareExchange(ref state.Phase, (int)ChannelPhase.TurningOn,
                                (int)ChannelPhase.Idle) == (int)ChannelPhase.Idle)
                        {
                            // RealTimeLoop가 처리하므로 상태만 설정
                            Interlocked.Exchange(ref state.ExpiryTicks, newExpiry);
                            handled = true;
                        }
                        break;

                    case ChannelPhase.TurningOn:
                    case ChannelPhase.On:
                        // 동일 채널에 새 요청이 오면 Expiry만 업데이트 (RealTimeLoop가 처리)
                        Interlocked.Exchange(ref state.ExpiryTicks, newExpiry);
                        // Phase는 그대로 유지 (On 또는 TurningOn)
                        handled = true;
                        break;

                    case ChannelPhase.TurningOff:
                        // 꺼지는 중에서 TurningOn으로 CAS 시도
                        if (Interlocked.CompareExchange(ref state.Phase, (int)ChannelPhase.TurningOn,
                                (int)ChannelPhase.TurningOff) == (int)ChannelPhase.TurningOff)
                        {
                            Interlocked.Exchange(ref state.ExpiryTicks, newExpiry);
                            handled = true;
                        }
                        break;
                }

                // 재시도 간 딜레이
                if (!handled && retryIndex < maxRetries - 1)
                {
                    await Task.Delay(1);
                }
            }

            if (!handled)
            {
                Log($"채널 {channel} 상태 업데이트 실패 - 최대 재시도 횟수 초과");
            }
        }
        /// <summary>
        /// 여러 채널을 발사합니다 (개별 FireChannelAsync 호출로 위임)
        /// </summary>
        public async Task FireChannelsAsync(IEnumerable<int> channels, int durationMs)
        {
            if (!_isConnected) return;
            var targetChannels = channels.Where(c => c >= 1 && c <= _totalChannels).ToList();
            if (targetChannels.Count == 0) return;

            // 각 채널을 개별적으로 fire (상태 머신 사용)
            var tasks = targetChannels.Select(ch => FireChannelAsync(ch, durationMs));
            await Task.WhenAll(tasks);
        }

        // AI가 수정함: 연결 해제 전 CancelAllAsync 호출하여 안전한 종료 보장
        public async Task DisconnectAsync()
        {
            await CancelAllAsync();
            
            _ctsConnection.Cancel();
            _isConnected = false;
            
            if (_realTimeThread != null && _realTimeThread.IsAlive)
            {
                await Task.Run(() => _realTimeThread.Join(1000));
            }

            _master?.Dispose();
            _master = null;
            Log("EtherCAT Disconnected");
        }

        public async Task ResetAsync()
        {
            await DisconnectAsync();
            await Task.Delay(1000);
            // Reconnect logic would depend on caller
        }

        /// <ai>AI가 작성함: 마스터 ON/OFF 상태 변경</ai>
        public void SetMasterOn(bool value)
        {
            _isMasterOn = value;
            Log($"Master {(value ? "ON" : "OFF")}");
        }

        /// <ai>AI가 작성함: 전체 채널 순차 테스트 (레거시 TestAllChannelAsync 동등)</ai>
        public async Task TestAllChannelAsync(int startChannel, int blowTime, int delayBetween, CancellationToken ct)
        {
            if (!_isConnected || !_isMasterOn) return;

            _ctsAllChannelTest = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _ctsAllChannelTest.Token;

            await Task.Run(async () =>
            {
                for (var ch = startChannel; ch <= _totalChannels; ch++)
                {
                    if (token.IsCancellationRequested)
                    {
                        Log("TestAllChannel 취소됨");
                        return;
                    }

                    await Task.Delay(delayBetween, token);
                    await FireChannelAsync(ch, blowTime);
                    Log($"Test Channel: {ch} 완료");
                }
            }, token).ConfigureAwait(false);
        }

        /// <ai>AI가 작성함: 전체 채널 테스트 취소</ai>
        public void CancelTestAllChannel()
        {
            _ctsAllChannelTest?.Cancel();
        }

        /// <ai>AI가 작성함: 모든 채널 강제 OFF</ai>
        public async Task OffAllChannelAsync()
        {
            if (_digitalOuts == null || _channelMap.Count == 0) return;

            await Task.Run(() =>
            {
                lock (_ioLock)
                {
                    for (var i = 0; i < _channelMap.Count; i++)
                    {
                        var (pdoIdx, pdoCh) = _channelMap[i];
                        _digitalOuts[pdoIdx].SetChannel(pdoCh, false);
                    }
                    _master?.UpdateIO(DateTime.UtcNow);
                }
            });
            Log("모든 채널 OFF 완료");
        }

        /// <summary>
        /// 모든 채널 작업을 취소하고 마스터를 끕니다 (RealTimeLoop 통합 버전)
        /// </summary>
        public async Task CancelAllAsync()
        {
            SetMasterOn(false);

            // 테스트 CTS 취소
            _ctsAllChannelTest?.Cancel();

            // RealTimeLoop 통합: 모든 채널 OFF로 전환 + Phase를 Idle로 설정
            foreach (var kv in _channelStates)
            {
                var channel = kv.Key;
                var state = kv.Value;

                // 채널 OFF로 직접 전환
                if (channel >= 1 && channel <= _channelMap.Count)
                {
                    var (pdoIdx, pdoCh) = _channelMap[channel - 1];
                    if (_digitalOuts != null && pdoIdx < _digitalOuts.Count)
                    {
                        _digitalOuts[pdoIdx].SetChannel(pdoCh, false);
                    }
                }

                // Phase를 Idle로 설정
                Volatile.Write(ref state.Phase, (int)ChannelPhase.Idle);
            }
            _channelStates.Clear();

            await Task.Delay(200);
            await OffAllChannelAsync();
            Log("CancelAll 완료 — 모든 작업 중지됨");
        }

        // === Legacy 패턴: 채널 상태 머신 ===

        /// <summary>
        /// 채널의 상태를 나타내는 열거형 (Legacy EtherCATMasterService 패턴)
        /// </summary>
        private static void SpinWaitForMs(int milliseconds, CancellationToken token = default)
        {
            if (milliseconds <= 0 || token.IsCancellationRequested) return;
            
            var endTicks = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * milliseconds / 1000.0);
            while (!token.IsCancellationRequested && Stopwatch.GetTimestamp() < endTicks)
            {
                Thread.SpinWait(10);
            }
        }
        private enum ChannelPhase
        {
            Idle,       // 채널 비활성 상태
            TurningOn,  // 채널 켜기 중
            On,         // 채널 ON 상태 유지 중
            TurningOff  // 채널 끄기 중
        }

        /// <summary>
        /// 채널 상태를 저장하는 클래스 - RealTimeLoop에서 처리하므로 CTS 불필요
        /// </summary>
        private class ChannelState
        {
            public long ExpiryTicks; // Stopwatch 기준 만료 시간 (Interlocked 접근)
            public int Phase = (int)ChannelPhase.Idle; // atomic int로 관리
        }
    }
}

