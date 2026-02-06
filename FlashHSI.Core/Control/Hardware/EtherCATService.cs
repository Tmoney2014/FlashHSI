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
        private int _cycleFrequency;

        public bool IsConnected => _isConnected;
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
                if (_isConnected && _master != null)
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

                // ON
                dOut.SetChannel(pdoCh, true); 
                // Note: RealTimeLoop will pick this up on next cycle, usually < 2ms.
                // For instant IO, we could call UpdateIO here inside lock, but RT loop handles it consistentlly.
                // Legacy code called SafeUpdateIO() directly here for immediacy.
                // Let's replicate SafeUpdateIO behavior if critical.
                lock(_ioLock) _master?.UpdateIO(DateTime.UtcNow);

                // WAIT
                await PrecisionTimer.WaitAsync(durationMs, cts.Token);

                // OFF
                if (!cts.IsCancellationRequested)
                {
                     dOut.SetChannel(pdoCh, false);
                     lock(_ioLock) _master?.UpdateIO(DateTime.UtcNow);
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                _activeChannels.TryRemove(channel, out _);
                cts.Dispose();
            }
        }

        public async Task DisconnectAsync()
        {
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

        private void Log(string msg)
        {
            LogMessage?.Invoke($"[EtherCAT] {msg}");
        }
    }
}
