using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FlashHSI.Core.Messages;

/// <summary>
/// 설정 변경 메시지 — 런타임 시 설정값 변경을 다른 서비스에 전파합니다
/// </summary>
public class SettingsChangedMessage<T> : ValueChangedMessage<T>
{
    public SettingsChangedMessage(string propertyName, T value) : base(value)
    {
        PropertyName = propertyName;
    }

    /// <summary>
    /// 변경된 설정 프로퍼티 이름
    /// </summary>
    public string PropertyName { get; }
}
