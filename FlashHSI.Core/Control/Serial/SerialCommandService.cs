using System.Buffers;
using System.IO;
using System.IO.Ports;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Enums;
using FlashHSI.Core.Messages;
using FlashHSI.Core.Models.Serial;
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
    private readonly ErrorStatusMessage _errorStatusMessage = new(new ErrorStatus());
    private readonly HardwareStatusMessage _hardwareStatusMessage = new(new HardwareStatus());
    
    // 동기화 및 에러 추적
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _serialSemaphore = new(1, 1);
    private readonly TimeSpan _errorThreshold = TimeSpan.FromSeconds(11);
    
    private readonly SerialPort _serialPort = new();
    private Timer? _errorCheckTimer;
    
    private bool _isSerialPortError;
    private DateTime? _lastSuccessfulConnectionTime;
    private string? _selectedSerialPort;
    
    private readonly IMessenger _messenger;

    // 생성자 주입
    public SerialCommandService(IMessenger messenger)
    {
        _messenger = messenger;
        _serialPort.DataReceived += SerialPortDataReceived;
        
        // 초기 상태 전송
        BroadcastInitialStatus();
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
                await Task.Delay(500);
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
        if (!await _serialSemaphore.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Log.Warning("Serial Command Timeout (Semaphore)");
            return;
        }

        try
        {
            if (!_serialPort.IsOpen) await ConnectPortAsync();

            if (_serialPort.IsOpen)
            {
                _serialPort.Write(command);
                // Log.Debug($"Serial Sent: {command.Trim()}"); 
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Serial Send Failed");
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
        _messenger.Send(_hardwareStatusMessage);
    }

    private void OnErrorOccurred(string msg)
    {
        _messenger.Send(new SystemMessage(msg));
    }

    private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        // 간단한 구현: 데이터 수신 처리 (기존 로직 참조)
        // 실제로는 바이트 단위 파싱 로직이 필요함 (여기서는 구조만 잡음)
        try
        {
            int bytesToRead = _serialPort.BytesToRead;
            byte[] buffer = _bytePool.Rent(bytesToRead);
            try
            {
                _serialPort.Read(buffer, 0, bytesToRead);
                
                // 간단한 파싱: ID 확인
                // if (buffer[1] == GlobalVariables.idDIO) ProcessDio(buffer);
                // else if (buffer[1] == GlobalVariables.idFDR) ProcessFdr(buffer);
            }
            finally
            {
                _bytePool.Return(buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Serial Receive Error");
        }
    }

    #endregion
}
