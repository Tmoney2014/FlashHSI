using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Logging;
using FlashHSI.Core.Messages;

namespace FlashHSI.UI.Services
{
    /// <summary>
    /// ILogMessageSender의 UI 구현체.
    /// IMessenger를 통해 SystemMessage를 전송하여 MainVM 상태바 + LogVM 시스템 로그에 반영합니다.
    /// </summary>
    public class MessengerLogMessageSender : ILogMessageSender
    {
        private readonly IMessenger _messenger;

        public MessengerLogMessageSender(IMessenger messenger)
        {
            _messenger = messenger;
        }

        public void SendLog(string message)
        {
            _messenger.Send(new SystemMessage(message));
        }
    }
}
