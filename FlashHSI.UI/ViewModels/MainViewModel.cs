using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core;
using FlashHSI.Core.Control;
using FlashHSI.Core.Control.Hardware;
using FlashHSI.Core.Control.Serial;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Messages;
using FlashHSI.Core.Services;
using FlashHSI.Core.Settings;
using FlashHSI.UI.Services;
using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;
using Serilog;
using SnackbarMessage = FlashHSI.Core.Messages.SnackbarMessage;
// AI가 추가함: SnackbarMessage 수신
// Added
// AI가 추가함: SnackbarMessageQueue

namespace FlashHSI.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // Core Services (Injected)
        private readonly HsiEngine _hsiEngine;
        private readonly WaterfallService _waterfallService;
        private readonly IEtherCATService _hardwareService;
        private readonly IMessenger _messenger;
        private readonly CommonDataShareService _dataShare;
        private readonly SerialCommandService _serialService;
        
        // Child ViewModels (Injected)
        public HomeViewModel HomeVM { get; }
        public LiveViewModel LiveVM { get; }  // AI가 추가함
        public StatisticViewModel StatisticVM { get; }
        public SettingViewModel SettingVM { get; }
        public LogViewModel LogVM { get; }

        // Global UI State
        [ObservableProperty] private string _statusMessage = "Ready";
        // AI가 수정함: WaterfallImage는 LiveViewModel로 이동됨
        
        // AI가 추가함: Busy 상태 오버레이 관리
        /// <ai>AI가 작성함</ai>
        private readonly IList<BusyMessage> _busyList = new List<BusyMessage>();
        [ObservableProperty] private bool _isBusy;
        
        // AI가 추가함: Snackbar 알림 큐 (하단 알림 메시지 표시용)
        /// <ai>AI가 작성함</ai>
        public SnackbarMessageQueue SnackbarQueue { get; } = new SnackbarMessageQueue(TimeSpan.FromSeconds(3));
        
        /// <ai>AI가 수정함: 모든 의존성을 생성자를 통해 주입받음 (DI 체인)</ai>
        public MainViewModel(
            HsiEngine hsiEngine,
            WaterfallService waterfallService,
            IEtherCATService hardwareService,
            SerialCommandService serialService,
            CommonDataShareService dataShare,
            IMessenger messenger,
            HomeViewModel homeVM,
            LiveViewModel liveVM,  // AI가 추가함
            StatisticViewModel statisticVM,
            SettingViewModel settingVM,
            LogViewModel logVM)
        {
            _hsiEngine = hsiEngine;
            _waterfallService = waterfallService;
            _hardwareService = hardwareService;
            _serialService = serialService;
            _dataShare = dataShare;
            _messenger = messenger;

            HomeVM = homeVM;
            LiveVM = liveVM;  // AI가 추가함
            StatisticVM = statisticVM;
            SettingVM = settingVM;
            LogVM = logVM;
            
            // 3. Event Subscriptions
            _hsiEngine.LogMessage += msg => StatusMessage = msg;
            _hardwareService.LogMessage += msg => StatusMessage = msg;
            
            // AI가 수정함: FrameProcessed 구독은 LiveViewModel로 이동됨
            _hsiEngine.EjectionOccurred += OnEjectionOccurredHardwareTrigger;
            
            SettingVM.ModelLoaded += OnModelLoaded;
            
            // AI가 추가함: SnackbarMessage 수신 → 하단 Snackbar 알림 표시
            _messenger.Register<SnackbarMessage>(this, (r, m) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    SnackbarQueue.DiscardDuplicates = true;
                    SnackbarQueue.Enqueue(m.Value);
                });
            });
            
            // AI가 추가함: BusyMessage 수신 → Busy 오버레이 표시/해제
            _messenger.Register<BusyMessage>(this, (r, m) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() => OnBusyMessage(m));
            });
            
            // AI가 추가함: SystemMessage 수신 → 하단 상태바 업데이트 (HomeViewModel 등에서 전송)
            _messenger.Register<SystemMessage>(this, (r, m) =>
            {
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    StatusMessage = m.Value;
                });
            });
            
            // 4. Load Initial Settings
             var s = SettingsService.Instance.Settings;
            if(!string.IsNullOrEmpty(s.LastHeaderPath))
            {
               SettingVM.HeaderPath = s.LastHeaderPath;
            }
            
            _hsiEngine.SetTargetFps(s.TargetFps);
            
            // AI가 수정함: SettingVM 이벤트 구독 완료 후 저장된 파일 자동 로드 시작
            // (SettingVM 생성자에서 fire-and-forget 시 ModelLoaded 구독 전에 이벤트가 발생하는 레이스 컨디션 수정)
            _ = SettingVM.LoadSavedFilesAsync();
        }

        // AI가 수정함: OnFrameProcessed 메서드는 LiveViewModel로 이동됨

        private void OnEjectionOccurredHardwareTrigger(EjectionLogItem log)
        {
             if(_hardwareService.IsConnected)
             {
                 // AI: User-Centric Ejection Logic (Delegated to EjectionService)
                 // MainViewModel now acts as a simple bridge to Hardware.
                 
                 _ = Task.Run(async () => 
                 {
                     try
                     {
                         // 1. Wait (Delay is already calculated in MS by EjectionService)
                         int delayMs = log.Delay;
                         if (delayMs > 0) await Task.Delay(delayMs);
                         
                         // 2. Fire (Duration is already calculated in MS)
                         int durationMs = log.DurationMs;
                         
                         // Safety Caps
                         if (durationMs < 5) durationMs = 5;       // Min 5ms
                         if (durationMs > 1000) durationMs = 1000; // Max 1s safety
                         
                         // 3. Fire Channels
                         if (log.ValveIds != null && log.ValveIds.Count > 0)
                         {
                             await _hardwareService.FireChannelsAsync(log.ValveIds, durationMs);
                         }
                         else
                         {
                             await _hardwareService.FireChannelAsync(log.ValveId, durationMs);
                         }
                     }
                     catch (Exception ex)
                     {
                         // Log error silently
                     }
                 });
             }
        }
        
        private void OnModelLoaded(string path)
        {
            try
            {
                _hsiEngine.LoadModel(path);
                
                // AI가 수정함: 모델 경로를 SystemSettings에 저장 (재시작 시 복원용)
                SettingsService.Instance.Settings.LastModelPath = path;
                SettingsService.Instance.Save();
                
                // AI가 추가함: MaskRule 활성화 여부 업데이트
                SettingVM.IsMaskRuleActive = _hsiEngine.IsMaskRuleActive;
                
                // AI가 추가함: SVM 모델 시 Confidence 슬라이더 비활성화
                var loadedType = _hsiEngine.LoadedModelType;
                SettingVM.IsConfidenceEnabled = !(loadedType.Contains("SVM") || loadedType.Contains("SVC"));
                
                StatusMessage = "Loading Model UI...";
                
                string json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<ModelConfig>(json);
                if (config == null) return;

                StatisticVM.InitializeStats(config);
                _waterfallService.UpdateColorMap(config);
                
                // AI가 수정함: 모델 로드 시 SortClass 목록 생성
                SettingVM.PopulateSortClasses(config);
                
                // AI가 수정함: model_config.json의 C# 전용 필드에서 배경 마스킹 설정 복원
                // (기존 엔진 직접 읽기 대신, 저장된 전체 설정을 복원)
                SettingVM.LoadMaskRuleSettings();
                
                // AI가 추가함: HomeView 모델 상태 갱신
                HomeVM.NotifyModelLoaded(config.ModelType ?? "Unknown");
                
                StatusMessage = $"Model Loaded: {config.ModelType}";
            }
            catch(Exception ex)
            {
                StatusMessage = $"Error loading model: {ex.Message}";
            }
        }
        
        [RelayCommand]
        public async Task WindowClosing()
        {
            // AI가 수정함: 종료 안전 처리 — 레거시 MainWindowViewModel.OnTimerCompleted() 동등
            Log.Information("앱 종료 처리 시작");
            
            // 1. 엔진 정지
            _hsiEngine.Stop();
            
            // 2. 시리얼(DIO) 보드 종료 (피더 OFF → 램프 OFF → 벨트 OFF → 전원 OFF)
            try
            {
                await _serialService.ShutDown();
                Log.Information("시리얼 보드 종료 완료");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "시리얼 보드 종료 중 오류 발생");
            }
            
            // 3. EtherCAT 마스터 종료
            try
            {
                await _hardwareService.DisconnectAsync();
                Log.Information("EtherCAT 마스터 종료 완료");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "EtherCAT 마스터 종료 중 오류 발생");
            }
            
            // 4. 설정 저장
            try
            {
                SettingsService.Instance.Save();
                Log.Information("설정 저장 완료");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "설정 저장 중 오류 발생");
            }
        }
        
        /// <summary>
        /// BusyMessage를 처리하여 _busyList 관리 및 IsBusy 갱신
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private void OnBusyMessage(BusyMessage message)
        {
            if (message.Value)
            {
                // 이미 추가된 BusyId면 무시
                var exist = _busyList.FirstOrDefault(b => b.BusyId == message.BusyId);
                if (exist != null) return;
                _busyList.Add(message);
            }
            else
            {
                var exist = _busyList.FirstOrDefault(b => b.BusyId == message.BusyId);
                if (exist == null) return;
                _busyList.Remove(exist);
            }
            
            IsBusy = _busyList.Any();
        }
        
        /// <ai>AI가 작성함</ai>
        partial void OnIsBusyChanged(bool oldValue, bool newValue)
        {
            Log.Information("IsBusy 값 변경: {NewValue}, 이전: {OldValue}", newValue, oldValue);
        }
    }
}
