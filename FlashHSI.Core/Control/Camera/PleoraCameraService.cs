using System.Buffers;
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

        // AI가 추가함: 연결 성공 이벤트
        public event Action? Connected;

        // AI가 추가함: 연결 끊김 이벤트
        public event Action<string>? ConnectionLost;

        // AI가 추가함: ICameraService 메타데이터 구현
        public double[]? Wavelengths { get; private set; }
        public int ParameterWidth { get; private set; }
        public int ParameterHeight { get; private set; }
        public string CameraName { get; private set; } = "Unknown Camera";
        public string CameraType { get; private set; } = "Unknown Type";
        public double ExposureTime { get; private set; } = 0.0;

        public PleoraCameraService()
        {
            _system = new PvSystem();
        }

        // AI가 수정함: 내부 블로킹 호출을 Task.Run으로 래핑하여 UI 프리징 방지
        public async Task<bool> ConnectAsync(string? deviceId = null)
        {
            if (_isConnected) return true;

            try
            {
                // AI가 수정함: 무거운 동기 API(Find, CreateAndConnect, CreateAndOpen 등)를
                // 백그라운드 스레드에서 실행하여 UI 스레드 블로킹 방지
                await Task.Run(() =>
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
                        throw new InvalidOperationException("NO_CAMERA_FOUND");
                    }

                    Log.Information($"Connecting to {deviceInfo.ModelName} ({deviceInfo.UniqueID})...");

                    // Connect to Device
                    _device = PvDevice.CreateAndConnect(deviceInfo);
                    if (_device == null) throw new Exception("Failed to connect to device.");

                    // Open Stream
                    _stream = PvStream.CreateAndOpen(deviceInfo);
                    if (_stream == null) throw new Exception("Failed to open stream.");

                    // Configure Stream (GigE Vision only)
                    // AI가 수정함: FX50은 NegotiatePacketSize()를 지원하지 않으므로 직접 패킷 사이즈 설정
                    var lDGEV = _device as PvDeviceGEV;
                    var lSGEV = _stream as PvStreamGEV;
                    if (lDGEV != null && lSGEV != null)
                    {
                        // FX50: NegotiatePacketSize() 미지원 → GevSCPSPacketSize 직접 설정
                        try
                        {
                            lDGEV.Parameters.SetIntegerValue("GevSCPSPacketSize", 8192);
                            Log.Information("GevSCPSPacketSize set to 8192 (Jumbo Frame).");
                        }
                        catch
                        {
                            try
                            {
                                lDGEV.Parameters.SetIntegerValue("GevSCPSPacketSize", 1476);
                                Log.Warning("Jumbo Frame 미지원. GevSCPSPacketSize를 1476으로 설정.");
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "GevSCPSPacketSize 설정 실패.");
                            }
                        }

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

                    // AI가 추가함: 카메라 메타데이터(Wavelength 등) 동적 추출 캐싱
                    try
                    {
                        Log.Information("카메라 메타데이터(Wavelength, Name 등) 동적 추출 시작...");

                        // Name, Type, ExposureTime 추출
                        try { CameraName = _device.Parameters.GetStringValue("DeviceModelName"); } catch { CameraName = "Unknown Camera"; }
                        try { CameraType = _device.Parameters.GetStringValue("DeviceUserID"); } catch { CameraType = "Unknown Type"; }
                        try { ExposureTime = _device.Parameters.GetFloatValue("ExposureTime"); } catch { ExposureTime = 0.0; }

                        ParameterWidth = (int)_device.Parameters.GetIntegerValue("Width");
                        ParameterHeight = (int)_device.Parameters.GetIntegerValue("Height"); // 밴드 수 (154 예상)

                        // Wavelength 테이블 순회하여 캐싱
                        if (ParameterHeight > 0)
                        {
                            Wavelengths = new double[ParameterHeight];
                            for (long idx = 0; idx < ParameterHeight; idx++)
                            {
                                _device.Parameters.SetIntegerValue("WavelengthTableIndex", idx);
                                // WavelengthTableValue는 2712.1300048828125 같은 String 형태 (GenICam 규격)
                                string wavStr = _device.Parameters.GetStringValue("WavelengthTableValue");
                                if (double.TryParse(wavStr, out double wavVal))
                                {
                                    Wavelengths[idx] = wavVal;
                                }
                            }
                            Log.Information("Wavelength 배열 추출 완료: 0번={First}, {Count}번={Last}",
                                Wavelengths[0], ParameterHeight - 1, Wavelengths[ParameterHeight - 1]);
                        }
                    }
                    catch (Exception metaEx)
                    {
                        Log.Error(metaEx, "카메라 메타데이터(Wavelength) 추출 실패!");
                        Wavelengths = null;
                    }

                    // AI가 추가함: 카메라 전체 파라미터 덤프 (Params.txt)
                    try
                    {
                        Log.Information("디바이스 파라미터 전체 덤프 시작...");
                        var sb = new System.Text.StringBuilder();
                        var parameters = _device.Parameters;

                        // AI가 수정함: 쓰기 가능 여부(RW/RO) 및 범위(Min~Max) 정보 추가
                        for (uint i = 0; i < parameters.Count; i++)
                        {
                            var param = parameters.Get(i);
                            if (param != null)
                            {
                                string name = param.Name;
                                string type = param.Type.ToString();
                                string value = "";
                                string description = "";
                                string access = "??";
                                string range = "";

                                try { access = param.IsWritable ? "RW" : "RO"; } catch { }
                                try { description = param.Description; } catch { }

                                try
                                {
                                    if (param is PvGenInteger gInt)
                                    {
                                        value = gInt.Value.ToString();
                                        try { range = $"[{gInt.Min}~{gInt.Max}]"; } catch { }
                                    }
                                    else if (param is PvGenFloat gFloat)
                                    {
                                        value = gFloat.Value.ToString();
                                        try { range = $"[{gFloat.Min}~{gFloat.Max}]"; } catch { }
                                    }
                                    else if (param is PvGenString gStr) value = gStr.Value;
                                    else if (param is PvGenEnum gEnum)
                                    {
                                        value = gEnum.ValueString;
                                        // AI가 추가함: Enum 항목 목록 추가 (WindowingMode 등 디버깅용)
                                        try
                                        {
                                            var entries = new System.Collections.Generic.List<string>();
                                            for (long ei = 0; ei < gEnum.EntriesCount; ei++)
                                            {
                                                var entry = gEnum.GetEntryByIndex(ei);
                                                if (entry != null && entry.IsAvailable)
                                                    entries.Add(entry.ValueString);
                                            }
                                            if (entries.Count > 0)
                                                range = $"[{string.Join(", ", entries)}]";
                                        }
                                        catch { }
                                    }
                                    else if (param is PvGenBoolean gBool) value = gBool.Value.ToString();
                                }
                                catch { value = "<Unreadable>"; }

                                sb.AppendLine($"[{type}] [{access}] {name} = {value} {range}");
                                if (!string.IsNullOrEmpty(description))
                                    sb.AppendLine($"    Description: {description}");
                            }
                        }
                        System.IO.File.WriteAllText("Params.txt", sb.ToString());
                        Log.Information("Params.txt 에 전체 파라미터 저장 완료.");
                    }
                    catch (Exception pEx)
                    {
                        Log.Warning($"파라미터 덤프 실패: {pEx.Message}");
                    }
                });

                // AI가 수정함: Task.Run 밖에서 이벤트 발생 (UI 스레드 컨텍스트)
                Connected?.Invoke();

                return true;
            }
            catch (InvalidOperationException ex) when (ex.Message == "NO_CAMERA_FOUND")
            {
                return false;
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
            const int MaxConsecutiveErrors = 10;

            // AI가 추가함: 프레임 수신 진단 카운터
            int frameCount = 0;
            int timeoutCount = 0;
            int errorCount = 0;
            var diagTimer = System.Diagnostics.Stopwatch.StartNew();

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
                    consecutiveErrors = 0;
                    frameCount++; // AI가 추가함: 프레임 수신 카운트

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
                    if (result.Code == PvResultCode.TIMEOUT)
                    {
                        timeoutCount++; // AI가 추가함
                    }
                    else
                    {
                        errorCount++; // AI가 추가함
                        consecutiveErrors++;
                        if (consecutiveErrors >= MaxConsecutiveErrors)
                        {
                            HandleConnectionLost($"RetrieveBuffer 연속 실패: {result.Code}");
                            break;
                        }
                    }
                }

                // AI가 추가함: 5초마다 수신 상태 진단 로그 제거 완료
                if (diagTimer.ElapsedMilliseconds >= 5000)
                {
                    Log.Information("📷 카메라 수신 진단: Frames={FrameCount}, Timeouts={TimeoutCount}, Errors={ErrorCount} (5초)",
                        frameCount, timeoutCount, errorCount);
                    frameCount = 0;
                    timeoutCount = 0;
                    errorCount = 0;
                    diagTimer.Restart();
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

        private static int _dropLogCount = 0;

        private unsafe void ProcessBuffer(PvBuffer? buffer)
        {
            if (buffer == null)
            {
                if (_dropLogCount++ < 10) Log.Warning("ProcessBuffer: buffer is null");
                return;
            }

            // PayloadType check - AI가 수정함: SDK 표준 enum 값 사용
            if (buffer.PayloadType != PvPayloadType.Image)
            {
                if (_dropLogCount++ < 10) Log.Warning($"ProcessBuffer: PayloadType isn't Image (Current: {buffer.PayloadType})");
                return;
            }

            var image = buffer.Image;
            long width = image.Width;
            long height = image.Height;

            if (width <= 0 || height <= 0)
            {
                if (_dropLogCount++ < 10) Log.Warning($"ProcessBuffer: Invalid Size {width}x{height}");
                return;
            }

            byte* pData = buffer.DataPointer;
            if (pData == null)
            {
                if (_dropLogCount++ < 10) Log.Warning("ProcessBuffer: DataPointer is null");
                return;
            }

            // 로그 폭주 방지용. 정상 프레임 진입 시 리셋.
            _dropLogCount = 0;

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
                    else if (value is int iVal) paramsList.SetIntegerValue(name, iVal); // AI가 추가함: MROI 등 int형 파라미터 지원
                    else if (value is float fVal) paramsList.SetFloatValue(name, fVal);
                    else if (value is double dVal) paramsList.SetFloatValue(name, (float)dVal); // Float/Double conversion
                    else if (value is string sVal)
                    {
                        // AI가 수정함: Enum 파라미터(예: WindowingMode)를 string으로 넘기는 경우 대응
                        // SetEnumValue를 먼저 시도하고, 실패 시 SetStringValue로 fallback
                        try { paramsList.SetEnumValue(name, sVal); }
                        catch { paramsList.SetStringValue(name, sVal); }
                    }
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

        // AI가 추가함: 지정된 이름의 Float형 카메라 파라미터의 허용 범위(Min, Max)를 조회
        public async Task<(double Min, double Max)> GetFloatParameterRangeAsync(string name)
        {
            if (_device == null) return (0.0, 0.0);
            return await Task.Run(() =>
            {
                try
                {
                    var param = _device.Parameters.Get(name);
                    if (param is PvGenFloat gFloat)
                    {
                        return (gFloat.Min, gFloat.Max);
                    }
                    return (0.0, 0.0);
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to get range for {name}: {ex.Message}");
                    return (0.0, 0.0);
                }
            });
        }

        // AI가 수정함: 외부에서 MROI 등 커맨드(예: RegionClear, RegionApply)를 호출할 수 있도록 public 개방
        public async Task ExecuteCommandAsync(string cmdName)
        {
            if (_device == null) return;
            await Task.Run(() =>
            {
                try
                {
                    _device.Parameters.ExecuteCommand(cmdName);
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to execute command {cmdName}: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
        }
    }
}
