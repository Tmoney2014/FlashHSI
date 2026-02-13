using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FlashHSI.Core.Messages;

/// <summary>
/// Snackbar 알림 메시지 (UI 하단 알림 표시용)
/// 모든 ViewModel/Service에서 IMessenger를 통해 전송 가능
/// </summary>
/// <ai>AI가 작성함</ai>
public class SnackbarMessage : ValueChangedMessage<string>
{
    public SnackbarMessage(string value) : base(value)
    {
    }
}
