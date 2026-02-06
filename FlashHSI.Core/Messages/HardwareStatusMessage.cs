using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using FlashHSI.Core.Enums; // DeviceStatus 사용을 위해 namespace 추가

namespace FlashHSI.Core.Messages;

/// <summary>
///     하드웨어 상태 메시지
/// </summary>
public class HardwareStatusMessage : ValueChangedMessage<HardwareStatus>
{
    public HardwareStatusMessage(HardwareStatus value) : base(value)
    {
    }
}

/// <summary>
///     하드웨어 상태 데이터 (int 기반: 0=Stopped, 1=Running, 2=Warning, 3=Error)
/// </summary>
public partial class HardwareStatus : ObservableObject
{
    /// <summary>
    ///     벨트 상태 (0=정지, 1=동작, 2=경고, 3=에러)
    /// </summary>
    [ObservableProperty] private int _beltStatus = (int)DeviceStatus.Warning; 

    /// <summary>
    ///     피더 상태 (0=정지, 1=동작, 2=경고, 3=에러)
    /// </summary>
    [ObservableProperty] private int _feederStatus = (int)DeviceStatus.Warning;

    /// <summary>
    ///     램프 상태 (0=정지, 1=동작, 2=경고, 3=에러)
    /// </summary>
    [ObservableProperty] private int _lampStatus = (int)DeviceStatus.Warning;
}
