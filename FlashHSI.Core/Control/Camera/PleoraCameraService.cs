using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PvDotNet;
using Serilog;

namespace FlashHSI.Core.Control.Camera
{
    public class PleoraCameraService : ICameraService
    {
        private PvSystem? _system;
        private PvDevice? _device;
        private PvStream? _stream;
        private Thread? _acquisitionThread;
        private CancellationTokenSource? _cancellationTokenSource;
        
        private volatile bool _isConnected;
        private volatile bool _isAcquiring;

        public bool IsConnected => _isConnected;
        public bool IsAcquiring => _isAcquiring;

        public event Action<ushort[], int, int>? FrameReceived;
        
        // AI가 추가함: 연결 끊김 이벤트
        public event Action<string>? ConnectionLost;

        public PleoraCameraService()
        {
            _system = new PvSystem();
        }

        public async Task<bool> ConnectAsync(string? deviceId = null)
        {
            if (_isConnected) return true;

            try
            {
                Log.Information("Searching for GigE Vision cameras...");
                _system?.Find();

                PvDeviceInfo? deviceInfo = null;
                
                // Auto-find first available GigE Vision device
                if (deviceInfo == null)
                {
                    var interfaceCount = _system?.InterfaceCount ?? 0;
                    for (uint i = 0; i < interfaceCount; i++)
                    {
                        var iface = _system?.GetInterface(i);
                        if (iface == null) continue;

                        var devCount = iface.DeviceCount;
                        for (uint j = 0; j < devCount; j++)
                        {
                            var dev = iface.GetDeviceInfo(j);
                            if (dev != null)
                            {
                                deviceInfo = dev;
                                break;
                            }
                        }
                        if (deviceInfo != null) break;
                    }
                }

                if (deviceInfo == null)
                {
                    Log.Warning("No GigE Vision camera found.");
                    return false;
                }

                Log.Information($"Connecting to {deviceInfo.ModelName} ({deviceInfo.UniqueID})...");

                // Connect to Device
                _device = PvDevice.CreateAndConnect(deviceInfo);
                if (_device == null) throw new Exception("Failed to connect to device.");

                // Open Stream
                _stream = PvStream.CreateAndOpen(deviceInfo);
                if (_stream == null) throw new Exception("Failed to open stream.");

                // Configure Stream (GigE Vision only)
                var lDGEV = _device as PvDeviceGEV;
                var lSGEV = _stream as PvStreamGEV;
                if (lDGEV != null && lSGEV != null)
                {
                    lDGEV.NegotiatePacketSize();
                    lDGEV.SetStreamDestination(lSGEV.LocalIPAddress, lSGEV.LocalPort);
                }

                // Buffer Management (Manual)
                // Read payload size
                long lPayloadSize = _device.PayloadSize;
                
                // Allocate buffers
                uint lBufferCount = 16;
                _buffers = new PvBuffer[lBufferCount];
                for (uint i = 0; i < lBufferCount; i++)
                {
                    _buffers[i] = new PvBuffer();
                    _buffers[i].Alloc((uint)lPayloadSize);
                }

                // Queue all buffers
                for (uint i = 0; i < lBufferCount; i++)
                {
                    _stream.QueueBuffer(_buffers[i]);
                }
                
                _isConnected = true;
                Log.Information("Camera Connected Successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Camera Connection Failed.");
                await DisconnectAsync(); // Cleanup
                return false;
            }
        }
        
        // Additional field for buffers
        private PvBuffer[]? _buffers;

        public async Task DisconnectAsync()
        {
            Log.Information("Disconnecting Camera...");
            await StopAcquisitionAsync();

            if (_stream != null)
            {
                _stream.Close();
                _stream.Dispose();
                _stream = null;
            }

            if (_device != null)
            {
                _device.Disconnect();
                _device.Dispose();
                _device = null;
            }
            
            _buffers = null;
            _isConnected = false;
            Log.Information("Camera Disconnected.");
        }

        public async Task StartAcquisitionAsync()
        {
            if (!_isConnected || _isAcquiring || _device == null) return;

            try
            {
                Log.Information("Starting Acquisition...");

                // Enable Stream
                _device.StreamEnable();

                // Start Acquisition Command
                _device.Parameters.ExecuteCommand("AcquisitionStart");

                // Start Thread
                _cancellationTokenSource = new CancellationTokenSource();
                _acquisitionThread = new Thread(AcquisitionLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Highest, // Critical for 700FPS
                    Name = "CameraAcquisition"
                };
                _isAcquiring = true;
                _acquisitionThread.Start();
                
                Log.Information("Acquisition Started.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to Start Acquisition.");
                _isAcquiring = false;
            }
        }

        public async Task StopAcquisitionAsync()
        {
            if (!_isAcquiring) return;

            Log.Information("Stopping Acquisition...");
            _cancellationTokenSource?.Cancel();
            
            // Wait for thread
            if (_acquisitionThread != null && _acquisitionThread.IsAlive)
            {
                _acquisitionThread.Join(500);
            }

            if (_device != null)
            {
                // Stop Command
                _device.Parameters.ExecuteCommand("AcquisitionStop");
                _device.StreamDisable();
            }

            // Abort and Dequeue
            if (_stream != null)
            {
                _stream.AbortQueuedBuffers();
                PvBuffer? lBuffer = null;
                PvResult lResult = new PvResult(PvResultCode.OK);
                PvResult lOpResult = new PvResult(PvResultCode.OK); // Dummy

                while (_stream.QueuedBufferCount > 0)
                {
                     // Retrieve blindly to clear
                     _stream.RetrieveBuffer(ref lBuffer, ref lOpResult, 0);
                     lBuffer = null;
                }
                
                // Re-queue for next time? Or re-allocate on Connect?
                // For simplicity, we re-queue on Connect or we keep them alive.
                // Actually if we Stop, we should probably just flush. 
                // If we want to Start again without Connect, we need to re-queue.
                
                // Let's re-queue them here if we plan to restart? 
                // Connect allocates. Disconnect clears. Stop just stops.
                // So Stop should ideally leave buffers ready or we re-queue in Start.
                // Sample Code Aborts and Retrieves. 
                // Let's re-queue in Start or just leave them?
                // Sample code Step5StoppingStream dequeues everything. 
                // Step2Configuring allocates and queues. 
                // So if we Stop, we are empty. We need to re-queue in Start?
                // Or we can just re-queue them here immediately after retrieving.
                
                if (_buffers != null)
                {
                    foreach (var buf in _buffers)
                    {
                        if (buf != null) _stream.QueueBuffer(buf);
                    }
                }
            }

            _isAcquiring = false;
            Log.Information("Acquisition Stopped.");
        }

        private void AcquisitionLoop()
        {
            if (_stream == null) return;
            
            Log.Information("Acquisition Loop Running...");

            PvBuffer? buffer = null;
            PvResult operationResult = new PvResult(PvResultCode.OK);
            PvResult result;
            
            // AI가 추가함: 연결 끊김 감지를 위한 연속 오류 카운터
            int consecutiveErrors = 0;
            const int MaxConsecutiveErrors = 10; // 10회 연속 오류 시 연결 끊김으로 판단

            while (_isAcquiring && _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                // Retrieve Buffer (100ms timeout)
                // Signature: RetrieveBuffer(ref PvBuffer aBuffer, ref PvResult aOperationResult, uint aTimeout)
                try
                {
                    result = _stream.RetrieveBuffer(ref buffer, ref operationResult, 100);
                }
                catch (Exception ex)
                {
                    // AI가 추가함: 예외 발생 시 연결 끊김 처리
                    Log.Error(ex, "카메라 스트림 예외 발생");
                    consecutiveErrors++;
                    if (consecutiveErrors >= MaxConsecutiveErrors)
                    {
                        HandleConnectionLost("스트림 예외 발생: " + ex.Message);
                        break;
                    }
                    continue;
                }

                if (result.IsOK)
                {
                    consecutiveErrors = 0; // 성공 시 리셋
                    
                    if (operationResult.IsOK)
                    {
                        ProcessBuffer(buffer);
                    }
                    
                    // Re-queue
                    _stream.QueueBuffer(buffer);
                }
                else
                {
                    // Timeout은 트리거 없을 때 정상, 다른 오류는 카운트
                    if (result.Code != PvResultCode.TIMEOUT)
                    {
                        consecutiveErrors++;
                        if (consecutiveErrors >= MaxConsecutiveErrors)
                        {
                            HandleConnectionLost($"RetrieveBuffer 연속 실패: {result.Code}");
                            break;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// AI가 추가함: 연결 끊김 처리
        /// </summary>
        private void HandleConnectionLost(string reason)
        {
            Log.Warning("카메라 연결 끊김 감지: {Reason}", reason);
            _isAcquiring = false;
            _isConnected = false;
            ConnectionLost?.Invoke(reason);
        }

        // AI가 추가함: 메모리 풀링을 위한 캐시된 버퍼 (700FPS 최적화)
        private ushort[]? _cachedFrameBuffer;
        private int _cachedBufferSize;

        private unsafe void ProcessBuffer(PvBuffer? buffer)
        {
            if (buffer == null) return;
            // PayloadType check - AI가 수정함: SDK 표준 enum 값 사용
            if (buffer.PayloadType != PvPayloadType.Image) return;

            var image = buffer.Image;
            long width = image.Width;
            long height = image.Height; 
            
            if (width <= 0 || height <= 0) return;

            byte* pData = buffer.DataPointer;
            if (pData == null) return;

            int totalPixels = (int)(width * height);
            
            // AI가 수정함: ArrayPool 기반 버퍼 풀링 (700FPS 최적화)
            // 기존: ushort[] frameData = new ushort[totalPixels]; // 매 프레임 힙 할당 (GC 부하)
            // 개선: 캐시된 버퍼가 충분하면 재사용, 아니면 Pool에서 대여
            ushort[] frameData;
            
            if (_cachedFrameBuffer != null && _cachedBufferSize >= totalPixels)
            {
                // 캐시된 버퍼 재사용
                frameData = _cachedFrameBuffer;
            }
            else
            {
                // Pool에서 대여 (최소 크기 보장)
                frameData = ArrayPool<ushort>.Shared.Rent(totalPixels);
                
                // 이전 버퍼 반환
                if (_cachedFrameBuffer != null)
                {
                    ArrayPool<ushort>.Shared.Return(_cachedFrameBuffer);
                }
                _cachedFrameBuffer = frameData;
                _cachedBufferSize = frameData.Length;
            }

            fixed (ushort* pDest = frameData)
            {
                long sizeBytes = totalPixels * 2;
                Buffer.MemoryCopy(pData, pDest, sizeBytes, sizeBytes);
            }

            // 참고: frameData는 Pool에서 가져온 배열이므로 길이가 totalPixels보다 클 수 있음
            // 이벤트에서 실제 width*height만 사용해야 함
            FrameReceived?.Invoke(frameData, (int)width, (int)height);
        }

        // --- Parameter Control ---

        public async Task SetParameterAsync<T>(string name, T value)
        {
            if (_device == null) return;
            await Task.Run(() => 
            {
                try
                {
                    var paramsList = _device.Parameters;
                    if (value is long lVal) paramsList.SetIntegerValue(name, lVal);
                    else if (value is float fVal) paramsList.SetFloatValue(name, fVal);
                    else if (value is double dVal) paramsList.SetFloatValue(name, (float)dVal); // Float/Double conversion
                    else if (value is string sVal) paramsList.SetStringValue(name, sVal);
                    else if (value is bool bVal) paramsList.SetBooleanValue(name, bVal);
                    else if (value is Enum eVal) paramsList.SetEnumValue(name, eVal.ToString());
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to set parameter {name}: {ex.Message}");
                }
            });
        }

        public async Task<T> GetParameterAsync<T>(string name)
        {
            if (_device == null) return default;
            return await Task.Run(() =>
            {
                try
                {
                    var paramsList = _device.Parameters;
                    // Generic handling is tricky with Pleora non-generic API
                    // Return zero/null for now basically
                    return default(T); 
                }
                catch { return default(T); }
            });
        }
        
        private void ExecuteCommand(string cmdName)
        {
             if (_device == null) return;
             _device.Parameters.ExecuteCommand(cmdName);
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
        }
    }
}
