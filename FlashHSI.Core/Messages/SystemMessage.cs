using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FlashHSI.Core.Messages;

/// <summary>
///     시스템 메시지 (에러나 알림 텍스트 전달용)
/// </summary>
public class SystemMessage : ValueChangedMessage<string>
{
    public SystemMessage(string value) : base(value)
    {
    }
}
