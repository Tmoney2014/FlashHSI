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
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeChannels = new();
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

        public async Task FireChannelAsync(int channel, int durationMs)
        {
            if (!_isConnected || channel < 1 || channel > _totalChannels) return;

            // Cancel existing fire on this channel
            if (_activeChannels.TryRemove(channel, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            var cts = new CancellationTokenSource();
            _activeChannels[channel] = cts;

            try
            {
                var (pdoIdx, pdoCh) = _channelMap[channel - 1];
                var dOut = _digitalOuts![pdoIdx];

                lock (_ioLock)
                {
                    dOut.SetChannel(pdoCh, true);
                    _master?.UpdateIO(DateTime.UtcNow);
                }

                await PrecisionTimer.WaitAsync(durationMs, cts.Token);

                if (!cts.IsCancellationRequested)
                {
                    lock (_ioLock)
                    {
                        dOut.SetChannel(pdoCh, false);
                        _master?.UpdateIO(DateTime.UtcNow);
                    }
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                if (_activeChannels.TryGetValue(channel, out var currentCts) && currentCts == cts)
                {
                    _activeChannels.TryRemove(channel, out _);
                }
                cts.Dispose();
            }
        }

        public async Task FireChannelsAsync(IEnumerable<int> channels, int durationMs)
        {
            if (!_isConnected) return;
            var targetChannels = channels.Where(c => c >= 1 && c <= _totalChannels).ToList();
            if (targetChannels.Count == 0) return;

            // 1. Cancel existing tasks
            foreach (var ch in targetChannels)
            {
                if (_activeChannels.TryRemove(ch, out var oldCts))
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
            }

            var cts = new CancellationTokenSource();
            foreach (var ch in targetChannels) _activeChannels[ch] = cts;

            try
            {
                // 2. Set ON Batch
                lock (_ioLock)
                {
                    foreach (var ch in targetChannels)
                    {
                        var (pdoIdx, pdoCh) = _channelMap[ch - 1];
                        _digitalOuts![pdoIdx].SetChannel(pdoCh, true); 
                    }
                    _master?.UpdateIO(DateTime.UtcNow);
                }

                // 3. Wait
                await PrecisionTimer.WaitAsync(durationMs, cts.Token);

                // 4. Set OFF Batch
                if (!cts.IsCancellationRequested)
                {
                    lock (_ioLock)
                    {
                         foreach (var ch in targetChannels)
                        {
                            var (pdoIdx, pdoCh) = _channelMap[ch - 1];
                            _digitalOuts![pdoIdx].SetChannel(pdoCh, false);
                        }
                        _master?.UpdateIO(DateTime.UtcNow);
                    }
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                foreach (var ch in targetChannels)
                {
                    if (_activeChannels.TryGetValue(ch, out var currentCts) && currentCts == cts)
                    {
                        _activeChannels.TryRemove(ch, out _);
                    }
                }
                cts.Dispose();
            }
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

        /// <ai>AI가 작성함: 마스터 OFF + 모든 토큰 취소 + 전체 채널 OFF</ai>
        public async Task CancelAllAsync()
        {
            SetMasterOn(false);

            // 테스트 CTS 취소
            _ctsAllChannelTest?.Cancel();

            // 활성 채널 CTS 전부 취소
            foreach (var cts in _activeChannels.Values)
            {
                cts.Cancel();
            }
            _activeChannels.Clear();

            await Task.Delay(100);
            await OffAllChannelAsync();
            Log("CancelAll 완료 — 모든 작업 중지됨");
        }

        private void Log(string msg)
        {
            LogMessage?.Invoke($"[EtherCAT] {msg}");
        }
    }
}
