using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Control.Hardware;
using FlashHSI.Core.Control.Serial; // AI가 추가함: 피더 전원 제어
using FlashHSI.Core.Settings;

namespace FlashHSI.UI.ViewModels
{
    /// <summary>
    /// 홈 화면 ViewModel - 운영 대시보드 (시뮬레이션, 하드웨어, SortClass, 피더 퀵 액세스)
    /// </summary>
    /// <ai>AI가 수정함: 운영 대시보드 역할 확장 — SettingVM 참조 추가, 모델 상태/피더 전원 노출</ai>
    public partial class HomeViewModel : ObservableObject
    {
        private readonly HsiEngine _hsiEngine;
        private readonly IEtherCATService _hardwareService;
        private readonly SerialCommandService _serialService; // AI가 추가함
        private readonly IMessenger _messenger;
        
        [ObservableProperty] private bool _isSimulating;
        [ObservableProperty] private bool _isHardwareConnected;
        [ObservableProperty] private string _statusMessage = "Ready";
        
        // AI가 추가함: 모델 로드 상태 (HomeView 상단 인디케이터용)
        [ObservableProperty] private bool _isModelLoaded;
        [ObservableProperty] private string _loadedModelName = "";
        
        // AI가 추가함: 피더 전원 상태 (HomeView 퀵 버튼용)
        [ObservableProperty] private bool _isFeederOn;
        
        // AI가 추가함: 벨트/램프 ON/OFF 상태
        [ObservableProperty] private bool _isBeltOn;
        [ObservableProperty] private bool _isLampOn;
        
        /// <ai>AI가 추가함: SettingVM 참조 — HomeView에서 SortClasses 등 운영 데이터 바인딩용</ai>
        public SettingViewModel Settings { get; }

        /// <ai>AI가 수정함: DI 주입 확장 — SettingVM, SerialCommandService 추가</ai>
        public HomeViewModel(HsiEngine engine, IEtherCATService hardware, IMessenger messenger, SettingViewModel settingVM, SerialCommandService serialService)
        {
            _hsiEngine = engine;
            _hardwareService = hardware;
            _serialService = serialService;
            _messenger = messenger;
            Settings = settingVM;
            
            _hsiEngine.SimulationStateChanged += s => IsSimulating = s;
        }
        
        /// <ai>AI가 작성함: 모델 로드 완료 시 HomeView 상태 갱신용</ai>
        public void NotifyModelLoaded(string modelName)
        {
            IsModelLoaded = true;
            LoadedModelName = modelName;
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
        
        /// <ai>AI가 작성함: 피더 전원 ON/OFF 토글 (홈 화면 퀵 버튼)</ai>
        [RelayCommand]
        private async Task ToggleFeederPower()
        {
            try
            {
                if (IsFeederOn)
                {
                    await _serialService.FeederPowerOffCommandAsync();
                    IsFeederOn = false;
                    StatusMessage = "피더 전원 OFF";
                }
                else
                {
                    await _serialService.FeederPowerOnCommandAsync();
                    IsFeederOn = true;
                    StatusMessage = "피더 전원 ON";
                }
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"피더 제어 실패: {ex.Message}";
            }
        }
        
        /// <ai>AI가 작성함: 벨트 ON/OFF 토글 (홈 화면 퀵 버튼)</ai>
        [RelayCommand]
        private async Task ToggleBelt()
        {
            try
            {
                if (IsBeltOn)
                {
                    await _serialService.BeltOffCommandAsync();
                    IsBeltOn = false;
                    StatusMessage = "벨트 OFF";
                }
                else
                {
                    await _serialService.BeltOnCommandAsync();
                    IsBeltOn = true;
                    StatusMessage = "벨트 ON";
                }
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"벨트 제어 실패: {ex.Message}";
            }
        }
        
        /// <ai>AI가 작성함: 램프 ON/OFF 토글 (홈 화면 퀵 버튼)</ai>
        [RelayCommand]
        private async Task ToggleLamp()
        {
            try
            {
                if (IsLampOn)
                {
                    await _serialService.LampOffCommandAsync();
                    IsLampOn = false;
                    StatusMessage = "램프 OFF";
                }
                else
                {
                    await _serialService.LampOnCommandAsync();
                    IsLampOn = true;
                    StatusMessage = "램프 ON";
                }
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"램프 제어 실패: {ex.Message}";
            }
        }
    }
}
