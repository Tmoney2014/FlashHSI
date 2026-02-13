using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlashHSI.Core.Control.Hardware
{
    public interface IEtherCATService
    {
        bool IsConnected { get; }
        
        /// <summary>에어건 마스터 ON/OFF 상태</summary>
        /// <ai>AI가 작성함</ai>
        bool IsMasterOn { get; }
        
        /// <summary>전체 채널 수</summary>
        /// <ai>AI가 작성함</ai>
        int TotalChannels { get; }
        
        void Connect(string interfaceName, int cycleFreq = 500);
        Task DisconnectAsync();
        
        /// <summary>
        /// Fires specific channel for duration using Precision Timer
        /// </summary>
        Task FireChannelAsync(int channel, int durationMs);
        
        /// <summary>
        /// Fires multiple channels simultaneously for duration
        /// </summary>
        Task FireChannelsAsync(IEnumerable<int> channels, int durationMs);
        
        /// <ai>AI가 작성함: 마스터 ON/OFF 상태 변경</ai>
        void SetMasterOn(bool value);
        
        /// <ai>AI가 작성함: 전체 채널 순차 테스트</ai>
        Task TestAllChannelAsync(int startChannel, int blowTime, int delayBetween, CancellationToken ct);
        
        /// <ai>AI가 작성함: 전체 채널 테스트 취소</ai>
        void CancelTestAllChannel();
        
        /// <ai>AI가 작성함: 모든 채널 강제 OFF</ai>
        Task OffAllChannelAsync();
        
        /// <ai>AI가 작성함: 마스터 OFF + 모든 토큰 취소 + 전체 채널 OFF</ai>
        Task CancelAllAsync();
        
        event Action<string>? LogMessage;
        
        Task ResetAsync();
    }
}
