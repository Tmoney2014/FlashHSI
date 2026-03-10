using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FlashHSI.Core.Messages;

/// <summary>
/// EtherCAT 연결 상태 변경 메시지
/// </summary>
public class EtherCATConnectionMessage : ValueChangedMessage<EtherCATConnectionStatus>
{
    public EtherCATConnectionMessage(EtherCATConnectionStatus value) : base(value)
    {
    }
}

/// <summary>
/// EtherCAT 연결 상태 데이터
/// </summary>
public class EtherCATConnectionStatus
{
    /// <summary>연결 성공 여부</summary>
    public bool IsConnected { get; init; }

    /// <summary>총 채널 수 (연결 성공 시)</summary>
    public int TotalChannels { get; init; }

    /// <summary>실패 원인 메시지 (연결 실패 시)</summary>
    public string? ErrorMessage { get; init; }
}
