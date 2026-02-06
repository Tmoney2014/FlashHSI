using System.Threading.Tasks;

namespace FlashHSI.Core.Control.Hardware
{
    public interface IEtherCATService
    {
        bool IsConnected { get; }
        
        void Connect(string interfaceName, int cycleFreq = 500);
        Task DisconnectAsync();
        
        /// <summary>
        /// Fires specific channel for duration using Precision Timer
        /// </summary>
        Task FireChannelAsync(int channel, int durationMs);
        
        event System.Action<string>? LogMessage;
        
        Task ResetAsync();
    }
}
