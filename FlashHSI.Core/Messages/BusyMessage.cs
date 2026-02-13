using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FlashHSI.Core.Messages;

/// <summary>
/// Busy 상태 메시지 — 장시간 작업 시 UI 오버레이 표시/해제용
/// Value=true: 오버레이 표시, Value=false: 오버레이 해제
/// BusyId로 큐 관리 (여러 작업이 동시에 Busy 상태일 수 있음)
/// </summary>
/// <ai>AI가 작성함</ai>
public class BusyMessage : ValueChangedMessage<bool>
{
    public BusyMessage(bool value) : base(value)
    {
    }

    /// <summary>
    /// Busy 큐에서 식별/제거 시 사용하는 ID
    /// </summary>
    public string BusyId { get; set; } = string.Empty;

    /// <summary>
    /// 오버레이에 표시할 메시지 텍스트 (선택)
    /// </summary>
    public string BusyText { get; set; } = string.Empty;
}
