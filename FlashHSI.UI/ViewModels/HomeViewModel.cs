using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Control.Hardware;
using FlashHSI.Core.Control.Serial; // AI가 추가함: 피더 전원 제어
using FlashHSI.Core.Messages; // AI가 추가함: HardwareStatusMessage 수신
using FlashHSI.Core.Settings;
using Serilog; // AI가 추가함: 램프 온도 로깅

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
        
        // AI가 추가함: 램프 온도 모니터링 (레거시 LampIndicatorUserControl 동등)
        /// <summary>램프 온도 퍼센트 (0~100, 프로그레스 바 너비 결정)</summary>
        [ObservableProperty] private double _lampTemperaturePercent;
        
        /// <summary>램프 상태 텍스트 (정지/가열중/준비완료/경고/에러)</summary>
        [ObservableProperty] private string _lampIndicatorStatusText = "정지";
        
        /// <summary>
        /// 램프 색상 상태 (0=회색/정지, 1=노랑/가열중, 2=주황/경고, 3=빨강/에러, 4=초록/준비완료)
        /// HardwareStatus.LampStatus + 온도 퍼센트 기반 계산
        /// </summary>
        /// <ai>AI가 작성함</ai>
        public int LampColorStatus
        {
            get
            {
                var lampStatus = _lastHardwareStatus?.LampStatus ?? 0;
                
                // AI가 수정함: 정지 상태 = 회색
                if (lampStatus == 0) return 0;
                // AI가 수정함: 경고 상태 = 주황
                if (lampStatus == 2) return 2;
                // AI가 수정함: 에러 상태 = 빨강
                if (lampStatus == 3) return 3;
                // AI가 수정함: 동작 중일 때 퍼센트에 따라 색상 결정
                // 100% = 초록 (준비 완료), 100% 미만 = 노랑 (가열 중)
                return LampTemperaturePercent >= 100 ? 4 : 1;
            }
        }
        
        /// <summary>램프 온도 퍼센트 텍스트 (UI 바인딩용)</summary>
        /// <ai>AI가 작성함</ai>
        public string LampTemperaturePercentText => $"{LampTemperaturePercent:F0}%";
        
        // AI가 추가함: 마지막 수신된 하드웨어 상태 (LampColorStatus 계산용)
        private HardwareStatus? _lastHardwareStatus;
        
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
            
            // AI가 추가함: HardwareStatusMessage 수신 → 램프 온도 인디케이터 업데이트
            _messenger.Register<HardwareStatusMessage>(this, (r, m) =>
            {
                _lastHardwareStatus = m.Value;
                UpdateLampIndicator();
            });
            
            // AI가 추가함: 앱 시작 시 저장된 램프 온도 상태 복원
            RestoreLampIndicatorFromSavedState();
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
        
        #region 램프 온도 모니터링 (레거시 MainViewModel 동등 구현)
        
        /// <summary>
        /// 현재 램프 온도를 계산합니다.
        /// 저장된 % + 경과 시간 기반 가열/냉각 계산
        /// </summary>
        /// <returns>램프 온도 퍼센트 (0~100)</returns>
        /// <ai>AI가 작성함</ai>
        private double GetCurrentLampTemperaturePercent()
        {
            var settings = SettingsService.Instance.Settings;
            var currentStatus = _lastHardwareStatus?.LampStatus ?? 0;
            var currentPercent = settings.LampTemperaturePercent;
            var lastUpdateTime = settings.LampLastUpdateTime;
            var heatUpTime = settings.LampHeatUpTimeMinutes;
            var coolDownTime = settings.LampCoolDownTimeMinutes;

            // 마지막 업데이트 시간이 없으면, 램프를 0%로 초기화
            if (!lastUpdateTime.HasValue)
            {
                return 0.0;
            }

            var elapsedMinutes = (DateTime.Now - lastUpdateTime.Value).TotalMinutes;

            // 시간이 거꾸로 간 비정상 상황이면, 안전하게 0%로 초기화
            if (elapsedMinutes < 0)
            {
                Log.Warning("램프 시간 계산 오류: elapsedMinutes < 0 (elapsed={Elapsed}, lastUpdate={LastUpdate})",
                    elapsedMinutes, lastUpdateTime.Value);
                return 0.0;
            }

            // AI가 수정함: 경과 시간 동안의 온도 변화 계산
            double calculatedPercent = currentPercent;

            if (currentStatus == 1 && heatUpTime > 0) // 동작(1) - 가열 중
            {
                var heatDelta = (elapsedMinutes / heatUpTime) * 100.0;
                calculatedPercent = currentPercent + heatDelta;
            }
            else if (currentStatus == 0 || currentStatus == 2) // 정지(0) 또는 경고(2) - 냉각 중
            {
                if (coolDownTime > 0)
                {
                    var coolDelta = (elapsedMinutes / coolDownTime) * 100.0;
                    calculatedPercent = currentPercent - coolDelta;
                }
            }
            // 에러(3) 상태는 변화 없음

            return Math.Clamp(calculatedPercent, 0.0, 100.0);
        }

        /// <summary>
        /// 하드웨어 상태 메시지 수신 시 램프 인디케이터 업데이트
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private void UpdateLampIndicator()
        {
            var now = DateTime.Now;
            var settings = SettingsService.Instance.Settings;

            // 계산된 온도 퍼센트
            var calculatedPercent = GetCurrentLampTemperaturePercent();

            // 계산된 값 저장
            settings.LampTemperaturePercent = calculatedPercent;
            settings.LampLastUpdateTime = now;
            SettingsService.Instance.Save();

            // VM에 값 반영
            LampTemperaturePercent = calculatedPercent;

            // UI 업데이트
            UpdateLampIndicatorUI();

            Log.Debug("램프 상태 업데이트: 값={Percent}%", calculatedPercent);
        }

        /// <summary>
        /// 램프 인디케이터 UI 업데이트 (상태 텍스트 + PropertyChanged 알림)
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private void UpdateLampIndicatorUI()
        {
            var currentLampStatus = _lastHardwareStatus?.LampStatus ?? 0;

            // 상태 텍스트 업데이트
            LampIndicatorStatusText = currentLampStatus switch
            {
                0 => LampTemperaturePercent > 0 ? "냉각 중" : "정지",
                1 => LampTemperaturePercent >= 100 ? "준비 완료" : "가열 중",
                2 => "경고",
                3 => "에러",
                _ => "정지"
            };

            // UI 재계산 알림
            OnPropertyChanged(nameof(LampColorStatus));
            OnPropertyChanged(nameof(LampTemperaturePercent));
            OnPropertyChanged(nameof(LampTemperaturePercentText));
        }

        /// <summary>
        /// 앱 시작 시 저장된 램프 온도 상태 복원
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private void RestoreLampIndicatorFromSavedState()
        {
            // 저장된 상태를 바탕으로 인디케이터 계산
            UpdateLampIndicator();
        }
        
        #endregion
    }
}
