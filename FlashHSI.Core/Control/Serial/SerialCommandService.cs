using System.Buffers;
using System.IO.Ports;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Enums;
using FlashHSI.Core.Messages;
using FlashHSI.Core.Models.Serial;
using FlashHSI.Core.Settings;
using Serilog;
using Timer = System.Timers.Timer;

namespace FlashHSI.Core.Control.Serial;

/// <summary>
///     시리얼 통신을 위한 서비스 (벨트, 피더, 램프 제어)
/// </summary>
public class SerialCommandService
{
    // 커맨드전송후 딜레이 타임
    private const int DelayTime = 50;
    
    // 재사용 가능한 버퍼 및 빌더
    private static readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;
    private readonly StringBuilder _boardIdBuilder = new(2);
    private readonly StringBuilder _hexBuilder = new(256);
    private readonly StringBuilder _byteValuesBuilder = new(512);
    private readonly ErrorStatusMessage _errorStatusMessage = new(new ErrorStatus());
    private readonly HardwareStatusMessage _hardwareStatusMessage = new(new HardwareStatus());
    
    // 동기화 및 에러 추적
    private readonly object _stateLock = new();
    private readonly object _logSbLock = new();
    private readonly SemaphoreSlim _serialSemaphore = new(1, 1);
    private readonly TimeSpan _errorThreshold = TimeSpan.FromSeconds(11);
    
    private readonly SerialPort _serialPort = new();
    private Timer? _errorCheckTimer;
    
    private bool _isSerialPortError;
    private DateTime? _lastSuccessfulConnectionTime;
    private string? _selectedSerialPort;
    
    // AI가 추가함: 캐시된 피더 값
    private List<int> _cachedFeederValues = new();
    
    private readonly IMessenger _messenger;

    // 생성자 주입
    public SerialCommandService(IMessenger messenger)
    {
        _messenger = messenger;
        _serialPort.DataReceived += SerialPortDataReceived;
        
        // 초기 상태 전송
        BroadcastInitialStatus();
        
        // 메시지 구독
        WeakReferenceMessenger.Default.Register<SerialCommandService, SettingsChangedMessage<List<int>>>(this, static (recipient, message) =>
        {
            if (message.PropertyName == nameof(SystemSettings.FeederValues))
            {
                recipient.UpdateAllFeederValues(message.Value);
            }
        });
    }

    /// <summary>
    /// 모든 피더 값 업데이트 (메시지로 호출됨)
    /// </summary>
    private void UpdateAllFeederValues(List<int> values)
    {
        if (values == null || values.Count == 0) return;
        
        _cachedFeederValues = new List<int>(values);
        
        // 시리얼로 각 피더에 값 전송 (0-based 인덱스 — HSIClient와 동일)
        for (int i = 0; i < values.Count; i++)
        {
            var feederNum = i; // 0-based: SetFeederValueCommandAsync 내부에서 FDR_WT_INPUT0 + feederNumber 계산
            var feederVal = values[i];
            Task.Run(async () => await SetFeederValueCommandAsync(feederNum, feederVal));
        }
    }

    private void BroadcastInitialStatus()
    {
        _messenger.Send(_errorStatusMessage);
        _messenger.Send(_hardwareStatusMessage);
        
        Log.Information(
            "초기 상태 메시지 전송 - Error(L={Left}, R={Right}, E={Emerg}), HW(Lamp={Lamp}, Belt={Belt}, Feeder={Feeder})",
            _errorStatusMessage.Value.LeftBeltError,
            _errorStatusMessage.Value.RightBeltError,
            _errorStatusMessage.Value.EmergencyStop,
            _hardwareStatusMessage.Value.LampStatus,
            _hardwareStatusMessage.Value.BeltStatus,
            _hardwareStatusMessage.Value.FeederStatus);
    }

    #region Command Methods

    public async Task BeltOffCommandAsync() => 
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DIO_WT_AC, 0));

    public async Task BeltOnCommandAsync() => 
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DIO_WT_AC, 1));

    public async Task BootEndCommandAsync() =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DIO_WT_BOOT, 1));

    public async Task BootStartCommandAsync() =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DIO_WT_BOOT, 0));
        
    public async Task LampOffCommandAsync() =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DIO_WT_LAMP, 0));

    public async Task LampOnCommandAsync() =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DIO_WT_LAMP, 1));

    public async Task PowerOffCommandAsync() =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DIO_WT_OUT, 1 << 6));

    public async Task ErrorClearCommandAsync() =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DIO_RD_WT_IN, 1));

    public async Task FeederNotUseCommandAsync(int feederNumber) =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idFDR, SerialCommand.FDR_WT_ONOFF0 + feederNumber, 0));

    public async Task FeederUseCommandAsync(int feederNumber) =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idFDR, SerialCommand.FDR_WT_ONOFF0 + feederNumber, 1));

    public async Task FeederPowerOffCommandAsync() =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idFDR, SerialCommand.FDR_WT_POWER_ALL, 0));

    public async Task FeederPowerOnCommandAsync() =>
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idFDR, SerialCommand.FDR_WT_POWER_ALL, 1));

    public async Task SetFeederValueCommandAsync(int feederNumber, int feederValue)
    {
        if (feederValue < 1 || feederValue > 99)
        {
            OnErrorOccurred("Feeder value must be between 1 and 99.");
            return;
        }
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idFDR, SerialCommand.FDR_WT_INPUT0 + feederNumber, feederValue));
    }
    
    #endregion

    #region Management Methods

    public string[] GetAvailablePorts() => SerialPort.GetPortNames();

    public async Task ChangePortAsync(string newPort)
    {
        _selectedSerialPort = newPort;
        PortCloseWhenOpen();
        await ConnectPortAsync(portName: newPort);
    }

    public async Task StartUp(string selectedSerialPort, int feederCount, IList<int>? feederValues)
    {
        _selectedSerialPort = selectedSerialPort;
        Log.Information($"SerialCommand StartUp: Port={selectedSerialPort}, FeederCount={feederCount}");

        try 
        {
            await BootStartCommandAsync();
            await BoardUseCommandAsync(); // 중요: 보드 사용 시작 알림
            
            await FeederPowerOffCommandAsync();
            
            for (var i = 0; i < feederCount; i++)
                await FeederUseCommandAsync(i);

            await FeederPowerOffCommandAsync(); // 2차 확인

            if (feederValues != null)
            {
                for (var i = 0; i < feederCount; i++)
                    if (i < feederValues.Count)
                        await SetFeederValueCommandAsync(i, feederValues[i]);
            }

            await BeltOffCommandAsync();
            await LampOffCommandAsync();
            await BootEndCommandAsync();

            StartPeriodicErrorCheck(3000);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Serial StartUp Failed");
            OnErrorOccurred($"Serial Startup Failed: {ex.Message}");
            throw; // 호출자(ConnectSerialPortAsync)가 실패를 감지할 수 있도록 재전파
        }
    }

    public async Task ShutDown()
    {
        try
        {
            Log.Information("Serial ShutDown Initiated");
            if (!IsOpenPort())
            {
                try { await ConnectPortAsync(); }
                catch { /* Ignore connect failure on shutdown */ }
            }

            if (IsOpenPort())
            {
                await FeederPowerOffCommandAsync();
                await Task.Delay(1000);
                await LampOffCommandAsync();
                await BeltOffCommandAsync();
                await PowerOffCommandAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Serial ShutDown Error");
        }
    }

    /// <summary>
    /// 종료 준비 단계 - 피더/램프 OFF, 벨트는 유지 (람프 냉각 대기용)
    /// </summary>
    public async Task PrepareShutDown()
    {
        try
        {
            Log.Information("Serial PrepareShutDown Initiated - Lamp/Feeder OFF, Belt continues");

            if (!IsOpenPort())
            {
                try { await ConnectPortAsync(); }
                catch { /* Ignore connect failure on shutdown */ }
            }

            if (IsOpenPort())
            {
                // 피더 전체 OFF
                await FeederPowerOffCommandAsync();
                Log.Information("PrepareShutDown: Feeder Power OFF");

                // 램프 OFF (벨트는 계속 돌림)
                await LampOffCommandAsync();
                
                // 램프 OFF 상태를 설정에 저장 (냉각 계산용)
                FlashHSI.Core.Settings.SettingsService.Instance.SetLampOff();
                
                Log.Information("PrepareShutDown: Lamp OFF");
            }
            else
            {
                Log.Warning("PrepareShutDown: Port not open, skipping hardware commands");
            }

            Log.Information("Serial PrepareShutDown Completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Serial PrepareShutDown Error");
        }
    }

    #endregion

    #region Internal Logic

    private async Task BoardUseCommandAsync()
    {
        await SendCommandAsync(SerialCommand.COMM_W(GlobalVariables.idDIO, SerialCommand.DiO_WT_AVH, 1));
    }

    private void StartPeriodicErrorCheck(int intervalMs)
    {
        StopPeriodicErrorCheck();
        _errorCheckTimer = new Timer(intervalMs);
        _errorCheckTimer.Elapsed += async (s, e) => await ErrorCheckCommandAsync();
        _errorCheckTimer.Start();
    }

    public void StopPeriodicErrorCheck()
    {
        _errorCheckTimer?.Stop();
        _errorCheckTimer?.Dispose();
        _errorCheckTimer = null;
    }

    private async Task ErrorCheckCommandAsync()
    {
        if (!IsOpenPort())
        {
            await HandlePortClosedAsync();
            return;
        }

        if (await TrySerialCommunicationAsync())
        {
            UpdateSuccessState();
            return;
        }

        await HandleCommunicationFailureAsync();
    }

    private async Task<bool> TrySerialCommunicationAsync()
    {
        try
        {
            await SendCommandAsync(SerialCommand.COMM_R(GlobalVariables.idDIO, SerialCommand.DIO_RD_WT_IN));
            await Task.Delay(200);
            await SendCommandAsync(SerialCommand.COMM_R(GlobalVariables.idFDR, SerialCommand.FDR_RD_FEEDERONOFF));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"Serial Communication Failed: {ex.Message}");
            return false;
        }
    }

    private async Task SendCommandAsync(string command)
    {
        // 타임아웃 10초 (HSIClient와 동일)
        if (!await _serialSemaphore.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            Log.Warning("시리얼 커맨드 전송 대기 타임아웃 (10초)");
            OnErrorOccurred("Command queue timeout.");
            return;
        }

        try
        {
            if (!_serialPort.IsOpen) await ConnectPortAsync();

            var cnt = command.Length;
            var txBuf = new byte[cnt];
            for (var i = 0; i < cnt; i++) txBuf[i] = (byte)command[i];

            _serialPort.Write(txBuf, 0, cnt);
            Log.Information("시리얼 커맨드 전송 완료");
            await Task.Delay(DelayTime);
        }
        catch (Exception ex)
        {
            OnErrorOccurred($"Command send failed: {ex.Message}");
            await ConnectPortAsync();
            throw; // 호출자에게 예외 전파
        }
        finally
        {
            _serialSemaphore.Release();
        }
    }

    private async Task ConnectPortAsync(int baudRate = 38400, string? portName = null)
    {
        PortCloseWhenOpen();
        var targetPort = portName ?? _selectedSerialPort;
        if (string.IsNullOrWhiteSpace(targetPort)) return;

        _serialPort.PortName = targetPort;
        _serialPort.BaudRate = baudRate;
        _serialPort.ReadTimeout = 500;
        _serialPort.WriteTimeout = 500;

        await Task.Run(() =>
        {
            try
            {
                _serialPort.Open();
                Log.Information($"Serial Port Opened: {targetPort}");
                UpdateSuccessState();
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to open serial port {targetPort}: {ex.Message}");
                throw;
            }
        });
    }

    private void PortCloseWhenOpen()
    {
        if (_serialPort.IsOpen) _serialPort.Close();
    }

    private bool IsOpenPort() => _serialPort.IsOpen;

    private void UpdateSuccessState()
    {
        lock (_stateLock)
        {
            _lastSuccessfulConnectionTime = DateTime.Now;
            _isSerialPortError = false;
        }
    }

    private async Task HandlePortClosedAsync()
    {
        if (_isSerialPortError)
        {
            await ConnectPortAsync();
            return;
        }
        SetAllIndicatorsToWarning();
        CheckAndSetErrorFlag();
    }

    private async Task HandleCommunicationFailureAsync()
    {
        SetAllIndicatorsToWarning();
        CheckAndSetErrorFlag();
        if (!_isSerialPortError) await ConnectPortAsync();
    }

    private void CheckAndSetErrorFlag()
    {
        lock (_stateLock)
        {
            if (_lastSuccessfulConnectionTime == null)
            {
                _lastSuccessfulConnectionTime = DateTime.Now;
                return;
            }

            var elapsed = DateTime.Now - _lastSuccessfulConnectionTime.Value;
            if (elapsed >= _errorThreshold && !_isSerialPortError)
            {
                _isSerialPortError = true;
                Log.Warning($"Serial Error Flag Set (Disconnected for {elapsed.TotalSeconds:F1}s)");
                OnErrorOccurred("Serial connection lost for over 10 seconds.");
            }
        }
    }

    private void SetAllIndicatorsToWarning()
    {
        _hardwareStatusMessage.Value.BeltStatus = (int)DeviceStatus.Warning;
        _hardwareStatusMessage.Value.LampStatus = (int)DeviceStatus.Warning;
        _hardwareStatusMessage.Value.FeederStatus = (int)DeviceStatus.Warning;

        // 에러 상태도 경고(2)로 설정
        _errorStatusMessage.Value.EmergencyStop = (int)DeviceStatus.Warning;
        _errorStatusMessage.Value.LeftBeltError = (int)DeviceStatus.Warning;
        _errorStatusMessage.Value.RightBeltError = (int)DeviceStatus.Warning;

        _messenger.Send(_hardwareStatusMessage);
        _messenger.Send(_errorStatusMessage);

        Log.Information(
            "모든 인디케이터를 경고 상태로 설정 - HW(Lamp={Lamp}, Belt={Belt}, Feeder={Feeder}), Err(Emerg={E}, L={L}, R={R})",
            _hardwareStatusMessage.Value.LampStatus,
            _hardwareStatusMessage.Value.BeltStatus,
            _hardwareStatusMessage.Value.FeederStatus,
            _errorStatusMessage.Value.EmergencyStop,
            _errorStatusMessage.Value.LeftBeltError,
            _errorStatusMessage.Value.RightBeltError);
    }

    private void OnErrorOccurred(string msg)
    {
        _messenger.Send(new SystemMessage(msg));
    }

    // 보드 ID 확인 (bytes[1], bytes[2]를 char로 변환 → "52"=DIO, "50"=FDR)
    private string GetBoardId(byte[] rentedBuffer)
    {
        _boardIdBuilder.Clear();
        _boardIdBuilder.Append((char)rentedBuffer[1]);
        _boardIdBuilder.Append((char)rentedBuffer[2]);
        return _boardIdBuilder.ToString();
    }

    /// <summary>
    ///     하드웨어 상태(램프, 벨트)를 처리
    /// </summary>
    private void ProcessDioHardwareStatus(byte[] bytes)
    {
        var isLampOn = (bytes[8] & 0x01) != 0;
        var isBeltOn = (bytes[8] & 0x02) != 0;

        Log.Information("램프 상태: {Status} (바이트[8]=0x{Value:X2})", isLampOn ? "ON" : "OFF", bytes[8]);
        Log.Information("벨트 상태: {Status} (바이트[8]=0x{Value:X2})", isBeltOn ? "ON" : "OFF", bytes[8]);

        _hardwareStatusMessage.Value.LampStatus = isLampOn ? (int)DeviceStatus.Running : (int)DeviceStatus.Stopped;
        _hardwareStatusMessage.Value.BeltStatus = isBeltOn ? (int)DeviceStatus.Running : (int)DeviceStatus.Stopped;
    }

    /// <summary>
    ///     피더 하드웨어 상태를 처리
    /// </summary>
    private void ProcessFdrHardwareStatus(byte[] bytes)
    {
        var isFeederOn = (bytes[8] & 0x01) != 0;

        Log.Information("피더 상태: {Status} (바이트[8]=0x{Value:X2})", isFeederOn ? "ON" : "OFF", bytes[8]);

        _hardwareStatusMessage.Value.FeederStatus = isFeederOn ? (int)DeviceStatus.Running : (int)DeviceStatus.Stopped;
    }

    /// <summary>
    ///     에러 상태를 처리 (긴급정지, 좌/우 벨트 에러)
    /// </summary>
    private void ProcessStatus(byte[] bytes)
    {
        // 긴급정지 상태 확인 (bit3: 0x08)
        var emergencyStopStatus = (bytes[6] & 0x08) != 0 ? (int)DeviceStatus.Error : (int)DeviceStatus.Running;
        Log.Information("긴급정지 상태: {S} (바이트[6]={V})", emergencyStopStatus == (int)DeviceStatus.Error ? "활성화(에러)" : "비활성화(정상)", bytes[6]);

        // 왼쪽 벨트 에러 상태 확인 (bit0: 0x01)
        var leftBeltErrorStatus = (bytes[6] & 0x01) != 0 ? (int)DeviceStatus.Error : (int)DeviceStatus.Running;
        Log.Information("왼쪽 벨트 에러: {S} (바이트[6]={V})", leftBeltErrorStatus == (int)DeviceStatus.Error ? "에러" : "정상", bytes[6]);

        // 오른쪽 벨트 에러 상태 확인 (bit1: 0x02)
        var rightBeltErrorStatus = (bytes[6] & 0x02) != 0 ? (int)DeviceStatus.Error : (int)DeviceStatus.Running;
        Log.Information("오른쪽 벨트 에러: {S} (바이트[6]={V})", rightBeltErrorStatus == (int)DeviceStatus.Error ? "에러" : "정상", bytes[6]);

        _errorStatusMessage.Value.EmergencyStop = emergencyStopStatus;
        _errorStatusMessage.Value.LeftBeltError = leftBeltErrorStatus;
        _errorStatusMessage.Value.RightBeltError = rightBeltErrorStatus;

        // 벨트 에러 발생 시 HardwareStatus도 Error로 변경
        if (leftBeltErrorStatus == (int)DeviceStatus.Error || rightBeltErrorStatus == (int)DeviceStatus.Error)
            _hardwareStatusMessage.Value.BeltStatus = (int)DeviceStatus.Error;
    }

    /// <summary>
    ///     시리얼포트로부터 데이터 수신 처리
    /// </summary>
    private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        byte[]? rentedBuffer = null;
        try
        {
            var bytesToRead = _serialPort.BytesToRead;
            rentedBuffer = _bytePool.Rent(bytesToRead);
            var actualRead = _serialPort.Read(rentedBuffer, 0, bytesToRead);

            // 로깅
            lock (_logSbLock)
            {
                _hexBuilder.Clear();
                _byteValuesBuilder.Clear();
                for (var i = 0; i < actualRead; i++)
                {
                    if (i > 0) _hexBuilder.Append('-');
                    _hexBuilder.Append(rentedBuffer[i].ToString("X2"));
                    if (i > 0) _byteValuesBuilder.Append(", ");
                    _byteValuesBuilder.Append("0x").Append(rentedBuffer[i].ToString("X2"));
                }
                Log.Information(
                    "바이트 데이터 - Hex: {Hex}, Base64: {Base64}, Bytes: [{ByteValues}]",
                    _hexBuilder.ToString(),
                    Convert.ToBase64String(rentedBuffer, 0, actualRead),
                    _byteValuesBuilder.ToString());
            }

            // ACK 응답(5-6바이트 짧은 패킷)은 무시
            if (actualRead < 9)
            {
                Log.Information("ACK 응답 수신 (패킷 길이: {Length}바이트, 무시)", actualRead);
                return;
            }

            // 보드 ID로 DIO / FDR 분기 처리
            var boardId = GetBoardId(rentedBuffer);

            switch (boardId)
            {
                case "52": // DIO 보드 (bytes[1]='5'=0x35, bytes[2]='2'=0x32)
                    Log.Information("DIO 보드 데이터 처리 (패킷 길이: {Length}바이트)", actualRead);
                    // bytes[6..8]이 ASCII로 오므로 숫자로 변환
                    for (var i = 6; i <= 8 && i < actualRead; i++)
                    {
                        if (rentedBuffer[i] >= 0x30 && rentedBuffer[i] <= 0x39)
                            rentedBuffer[i] = (byte)(rentedBuffer[i] - 0x30);
                        else if (rentedBuffer[i] >= 0x41 && rentedBuffer[i] <= 0x46)
                            rentedBuffer[i] = (byte)(rentedBuffer[i] - 0x41 + 10);
                    }
                    ProcessDioHardwareStatus(rentedBuffer);
                    ProcessStatus(rentedBuffer);
                    break;

                case "50": // FDR 보드 (bytes[1]='5'=0x35, bytes[2]='0'=0x30)
                    Log.Information("FDR 보드 데이터 처리 (패킷 길이: {Length}바이트)", actualRead);
                    // bytes[8]이 ASCII로 오므로 숫자로 변환
                    if (actualRead > 8)
                    {
                        if (rentedBuffer[8] >= 0x30 && rentedBuffer[8] <= 0x39)
                            rentedBuffer[8] = (byte)(rentedBuffer[8] - 0x30);
                        else if (rentedBuffer[8] >= 0x41 && rentedBuffer[8] <= 0x46)
                            rentedBuffer[8] = (byte)(rentedBuffer[8] - 0x41 + 10);
                    }
                    ProcessFdrHardwareStatus(rentedBuffer);
                    break;

                default:
                    Log.Warning($"알 수 없는 보드 ID: {boardId}");
                    break;
            }

            // 최종 메시지 전송
            _messenger.Send(_errorStatusMessage);
            _messenger.Send(_hardwareStatusMessage);
        }
        catch (System.IO.IOException ioEx)
        {
            OnErrorOccurred($"I/O error: {ioEx.Message}");
        }
        catch (UnauthorizedAccessException uaEx)
        {
            OnErrorOccurred($"Access denied: {uaEx.Message}");
        }
        catch (InvalidOperationException invOpEx)
        {
            OnErrorOccurred($"Invalid operation: {invOpEx.Message}");
        }
        catch (Exception ex)
        {
            OnErrorOccurred($"Unknown error: {ex.Message}");
        }
        finally
        {
            if (rentedBuffer != null)
                _bytePool.Return(rentedBuffer);
        }
    }

    #endregion
}
