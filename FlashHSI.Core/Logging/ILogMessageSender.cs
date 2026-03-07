namespace FlashHSI.Core.Logging
{
    /// <summary>
    /// 사용자에게 보여지는 상태 메시지를 전송하는 인터페이스.
    /// Core 프로젝트에서 UI 의존성 없이 메시지를 보낼 수 있도록 추상화합니다.
    /// </summary>
    public interface ILogMessageSender
    {
        void SendLog(string message);
    }
}
