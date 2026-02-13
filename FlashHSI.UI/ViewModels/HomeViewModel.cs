using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Control.Camera; // AI가 추가함: 카메라 서비스
using FlashHSI.Core.Control.Hardware;
using FlashHSI.Core.Control.Serial; // AI가 추가함: 피더 전원 제어
using FlashHSI.Core.Messages; // AI가 추가함: HardwareStatusMessage 수신
using FlashHSI.Core.Models; // AI가 추가함: ModelCard
using FlashHSI.Core.Settings;
using Serilog; // AI가 추가함: 램프 온도 로깅
using System.Collections.ObjectModel; // AI가 추가함: ModelCards 컬렉션
using System.IO; // AI가 추가함: 디렉터리 스캔
using System.Windows.Threading; // AI가 추가함: 시작 타이머용

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
        private readonly ICameraService _cameraService; // AI가 추가함: 카메라 서비스
        
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
        
        // AI가 추가함: 카메라 연결 상태 (홈 화면에서 카메라 제어)
        [ObservableProperty] private bool _isCameraConnected;
        [ObservableProperty] private string _cameraName = "연결 필요";
        
        // AI가 추가함: 에러 상태 (ErrorStatusMessage에서 수신)
        /// <ai>AI가 작성함</ai>
        [ObservableProperty] private int _leftBeltError = 2; // 기본값 2=경고(데이터 미수신)
        [ObservableProperty] private int _rightBeltError = 2;
        [ObservableProperty] private int _emergencyStop = 2;
        
        // AI가 추가함: 시작 타이머 (카메라 재시도 카운트다운)
        /// <ai>AI가 작성함</ai>
        private DispatcherTimer? _timer;
        private TimeSpan _remainingTime;
        
        /// <summary>타이머 남은 시간 문자열 (mm:ss 형식, UI 바인딩용)</summary>
        /// <ai>AI가 작성함</ai>
        [ObservableProperty] private string _remainingTimeString = "";
        
        /// <summary>타이머 실행 중 여부 (타이머 오버레이 표시 제어용)</summary>
        /// <ai>AI가 작성함</ai>
        [ObservableProperty] private bool _isTimerRunning;
        
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
        
        // AI가 추가함: 모델 카드 목록 (디렉터리 스캔 결과)
        /// <summary>모델 디렉터리 내 JSON 파일을 카드로 표시하는 컬렉션</summary>
        /// <ai>AI가 작성함</ai>
        public ObservableCollection<ModelCard> ModelCards { get; } = new();
        
        /// <summary>모델 디렉터리 경로 (설정에 저장됨)</summary>
        /// <ai>AI가 작성함</ai>
        [ObservableProperty] private string _modelDirectory = "";

        /// <ai>AI가 수정함: DI 주입 확장 — SettingVM, SerialCommandService, CameraService 추가</ai>
        public HomeViewModel(HsiEngine engine, IEtherCATService hardware, IMessenger messenger, SettingViewModel settingVM, SerialCommandService serialService, ICameraService cameraService)
        {
            _hsiEngine = engine;
            _hardwareService = hardware;
            _serialService = serialService;
            _cameraService = cameraService; // AI가 추가함
            _messenger = messenger;
            Settings = settingVM;
            
            _hsiEngine.SimulationStateChanged += s => IsSimulating = s;
            
            // AI가 추가함: HardwareStatusMessage 수신 → 램프 온도 인디케이터 업데이트
            _messenger.Register<HardwareStatusMessage>(this, (r, m) =>
            {
                _lastHardwareStatus = m.Value;
                UpdateLampIndicator();
            });
            
            // AI가 추가함: ErrorStatusMessage 수신 → 에러 인디케이터 업데이트
            _messenger.Register<ErrorStatusMessage>(this, (r, m) =>
            {
                var err = m.Value;
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    LeftBeltError = err.LeftBeltError;
                    RightBeltError = err.RightBeltError;
                    EmergencyStop = err.EmergencyStop;
                });
            });
            
            // AI가 추가함: 앱 시작 시 저장된 램프 온도 상태 복원
            RestoreLampIndicatorFromSavedState();
            
            // AI가 추가함: 카메라 재시도 카운트다운 타이머 시작
            StartTimer();
            
            // AI가 추가함: 저장된 모델 디렉터리 경로 복원 및 스캔
            var savedModelDir = SettingsService.Instance.Settings.ModelDirectory;
            if (!string.IsNullOrEmpty(savedModelDir) && Directory.Exists(savedModelDir))
            {
                _modelDirectory = savedModelDir;
                ScanModelDirectory();
            }
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
        
        /// <ai>AI가 작성함: 카메라 연결/해제 토글 (홈 화면에서 카메라 제어)</ai>
        [RelayCommand]
        private async Task ToggleCamera()
        {
            try
            {
                if (IsCameraConnected)
                {
                    StatusMessage = "카메라 연결 해제 중...";
                    await _cameraService.DisconnectAsync();
                    IsCameraConnected = false;
                    CameraName = "연결 필요";
                    StatusMessage = "카메라 연결 해제됨";
                    Log.Information("카메라 연결 해제 (홈)");
                }
                else
                {
                    StatusMessage = "카메라 연결 중...";
                    bool connected = await _cameraService.ConnectAsync();
                    
                    if (connected)
                    {
                        IsCameraConnected = true;
                        CameraName = "FX50 Connected"; // TODO: 실제 카메라 이름 조회
                        StatusMessage = "카메라 연결 성공";
                        Log.Information("카메라 연결 성공 (홈)");
                    }
                    else
                    {
                        IsCameraConnected = false;
                        StatusMessage = "카메라 연결 실패";
                        Log.Warning("카메라 연결 실패 (홈)");
                    }
                }
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"연결 오류: {ex.Message}";
                Log.Error(ex, "카메라 연결 오류 (홈)");
            }
        }
        
        /// <ai>AI가 작성함: 에러 클리어 커맨드 (비상정지 해제 등)</ai>
        [RelayCommand]
        private async Task ErrorClear()
        {
            try
            {
                await _serialService.ErrorClearCommandAsync();
                StatusMessage = "에러 클리어 완료";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"에러 클리어 실패: {ex.Message}";
            }
        }
        
        #region 모델 카드 (디렉터리 기반 선택)
        
        /// <summary>
        /// 모델 디렉터리를 선택하는 폴더 브라우저 다이얼로그를 열어요.
        /// 선택하면 디렉터리 내 JSON 파일을 스캔하여 ModelCards에 추가해요.
        /// </summary>
        /// <ai>AI가 작성함</ai>
        [RelayCommand]
        private void BrowseModelDirectory()
        {
            // WPF에서 FolderBrowserDialog 대신 OpenFolderDialog 사용 (.NET 8)
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "모델 디렉터리 선택"
            };
            
            if (!string.IsNullOrEmpty(ModelDirectory) && Directory.Exists(ModelDirectory))
            {
                dlg.InitialDirectory = ModelDirectory;
            }
            
            if (dlg.ShowDialog() == true)
            {
                ModelDirectory = dlg.FolderName;
                SettingsService.Instance.Settings.ModelDirectory = ModelDirectory;
                SettingsService.Instance.Save();
                ScanModelDirectory();
                Log.Information("모델 디렉터리 설정: {Path}", ModelDirectory);
            }
        }
        
        /// <summary>
        /// 모델 디렉터리 내 JSON 파일을 스캔하여 ModelCards 컬렉션을 갱신해요.
        /// </summary>
        /// <ai>AI가 작성함</ai>
        [RelayCommand]
        private void RefreshModelCards()
        {
            ScanModelDirectory();
        }
        
        /// <summary>
        /// 카드 클릭 시 해당 모델을 로드해요.
        /// 기존 SettingVM.ModelLoaded 이벤트 플로우를 재사용해요.
        /// </summary>
        /// <ai>AI가 작성함</ai>
        [RelayCommand]
        private void SelectModelCard(ModelCard card)
        {
            if (card == null) return;
            
            // 기존 선택 해제
            foreach (var c in ModelCards)
                c.IsSelected = false;
            
            card.IsSelected = true;
            
            // AI가 수정함: CS0070 해결 — 이벤트 직접 Invoke 대신 public 메서드 호출
            Settings.RaiseModelLoaded(card.FilePath);
            Log.Information("모델 카드 선택: {Name} ({Path})", card.Name, card.FilePath);
        }
        
        /// <summary>
        /// ModelDirectory 내의 *.json 파일을 스캔하여 ModelCards를 채워요.
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private void ScanModelDirectory()
        {
            ModelCards.Clear();
            
            if (string.IsNullOrEmpty(ModelDirectory) || !Directory.Exists(ModelDirectory))
            {
                Log.Warning("모델 디렉터리가 없거나 비어있어요: {Path}", ModelDirectory);
                return;
            }
            
            var jsonFiles = Directory.GetFiles(ModelDirectory, "*.json");
            var currentModelPath = SettingsService.Instance.Settings.LastModelPath;
            
            foreach (var file in jsonFiles.OrderBy(f => Path.GetFileNameWithoutExtension(f)))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var isSelected = string.Equals(file, currentModelPath, System.StringComparison.OrdinalIgnoreCase);
                ModelCards.Add(new ModelCard(name, file, isSelected));
            }
            
            Log.Information("모델 디렉터리 스캔 완료: {Count}개 파일 발견 ({Path})", ModelCards.Count, ModelDirectory);
        }
        
        #endregion
        
        #region 시작 타이머 (카메라 재시도, 레거시 StartTimerUserControl 동등)
        
        /// <summary>
        /// 카메라 재시도 카운트다운 타이머를 시작합니다.
        /// 설정된 CameraRetryTimerMinutes 후 카메라 초기화를 재시도합니다.
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private void StartTimer()
        {
            var timerMinutes = SettingsService.Instance.Settings.CameraRetryTimerMinutes;
            if (timerMinutes <= 0)
            {
                Log.Information("카메라 재시도 타이머: 0분 설정 — 타이머 생략");
                return;
            }
            
            _remainingTime = TimeSpan.FromMinutes(timerMinutes);
            RemainingTimeString = _remainingTime.ToString(@"mm\:ss");
            IsTimerRunning = true;
            
            Log.Information("카메라 재시도 타이머 시작: {Minutes}분", timerMinutes);
            
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
        
        /// <summary>
        /// 1초마다 호출되어 남은 시간을 감소시킵니다.
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_remainingTime.TotalSeconds > 0)
            {
                _remainingTime = _remainingTime.Subtract(TimeSpan.FromSeconds(1));
                RemainingTimeString = _remainingTime.ToString(@"mm\:ss");
            }
            else
            {
                _timer?.Stop();
                RemainingTimeString = "타이머 완료!";
                OnTimerCompleted();
            }
        }
        
        /// <summary>
        /// 타이머 완료 시 카메라 초기화 재시도를 수행합니다.
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private void OnTimerCompleted()
        {
            Log.Information("카메라 재시도 타이머 완료 — 카메라 초기화 재시도 시작");
            IsTimerRunning = false;
            StatusMessage = "카메라 재시도 타이머 완료";
            
            // TODO: CameraService 구현 후 여기서 카메라 초기화 재시도 호출
            // _ = RetryCameraInitAsync();
        }
        
        /// <summary>
        /// 타이머를 수동으로 중지합니다.
        /// </summary>
        /// <ai>AI가 작성함</ai>
        [RelayCommand]
        private void StopTimer()
        {
            _timer?.Stop();
            IsTimerRunning = false;
            RemainingTimeString = "타이머 중지됨";
            Log.Information("카메라 재시도 타이머 수동 중지");
            StatusMessage = "카메라 재시도 타이머 중지됨";
        }
        
        #endregion
        
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
