using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FlashHSI.Core.Messages;

/// <summary>
///     시리얼 통신 에러 상태 메시지
/// </summary>
public class ErrorStatusMessage : ValueChangedMessage<ErrorStatus>
{
    public ErrorStatusMessage(ErrorStatus value) : base(value)
    {
    }
}

/// <summary>
///     에러 상태 데이터 (4가지 상태: 0=데이터없음, 1=정상, 2=경고, 3=에러)
/// </summary>
public partial class ErrorStatus : ObservableObject
{
    /// <summary>
    ///     긴급 정지 상태
    /// </summary>
    [ObservableProperty] private int _emergencyStop = 2; // 기본값 2 (경고)

    /// <summary>
    ///     왼쪽 벨트 에러 상태
    /// </summary>
    [ObservableProperty] private int _leftBeltError = 2; // 기본값 2 (경고)

    /// <summary>
    ///     오른쪽 벨트 에러 상태
    /// </summary>
    [ObservableProperty] private int _rightBeltError = 2; // 기본값 2 (경고)
}
