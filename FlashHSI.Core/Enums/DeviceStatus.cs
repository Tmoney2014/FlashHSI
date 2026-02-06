namespace FlashHSI.Core.Enums;

/// <summary>
///     장비 상태 (0: 정지, 1: 동작, 2: 경고, 3: 에러)
/// </summary>
public enum DeviceStatus
{
    Stopped = 0,
    Running = 1,
    Warning = 2,
    Error = 3
}
