using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Control.Hardware;
using FlashHSI.Core.Settings;

namespace FlashHSI.UI.ViewModels
{
    /// <summary>
    /// 홈 화면 ViewModel - 시뮬레이션 및 하드웨어 연결 제어
    /// </summary>
    /// <ai>AI가 작성함: PageViewModels.cs에서 분리</ai>
    public partial class HomeViewModel : ObservableObject
    {
        private readonly HsiEngine _hsiEngine;
        private readonly IEtherCATService _hardwareService;
        private readonly IMessenger _messenger;
        
        [ObservableProperty] private bool _isSimulating;
        [ObservableProperty] private bool _isHardwareConnected;
        [ObservableProperty] private string _statusMessage = "Ready";

        /// <ai>AI가 수정함: DI 주입 및 Messenger 적용</ai>
        public HomeViewModel(HsiEngine engine, IEtherCATService hardware, IMessenger messenger)
        {
            _hsiEngine = engine;
            _hardwareService = hardware;
            _messenger = messenger;
            
            _hsiEngine.SimulationStateChanged += s => IsSimulating = s;
        }

        [RelayCommand]
        private void ToggleSimulation()
        {
            if (IsSimulating) _hsiEngine.Stop();
            else 
            {
               var hdr = SettingsService.Instance.Settings.LastHeaderPath;
               if(!string.IsNullOrEmpty(hdr)) _hsiEngine.StartSimulation(hdr);
            }
        }

        [RelayCommand]
        private void ConnectHardware()
        {
            if (IsHardwareConnected)
            {
                _hardwareService.DisconnectAsync();
                IsHardwareConnected = false;
                StatusMessage = "Hardware Disconnected";
            }
            else
            {
                _hardwareService.Connect("이더넷");
                if(_hardwareService.IsConnected)
                {
                    IsHardwareConnected = true;
                    StatusMessage = "Hardware Connected";
                }
            }
        }
    }
}
