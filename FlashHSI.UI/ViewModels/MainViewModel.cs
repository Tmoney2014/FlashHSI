using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Engine;
using FlashHSI.Core;
using FlashHSI.Core.Settings;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Media;
using FlashHSI.UI.Services;
using FlashHSI.Core.Control;
using FlashHSI.Core.Control.Hardware;
using FlashHSI.Core.Control.Serial; // Added
using FlashHSI.Core.Services;
using System.Linq;
using System;

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
            
            // 4. Load Initial Settings
             var s = SettingsService.Instance.Settings;
            if(!string.IsNullOrEmpty(s.LastHeaderPath))
            {
               SettingVM.HeaderPath = s.LastHeaderPath;
            }
            
            _hsiEngine.SetTargetFps(s.TargetFps);
        }

        // AI가 수정함: OnFrameProcessed 메서드는 LiveViewModel로 이동됨

        private void OnEjectionOccurredHardwareTrigger(EjectionLogItem log)
        {
             if(_hardwareService.IsConnected)
             {
                 Application.Current.Dispatcher.InvokeAsync(async () =>
                 {
                    double fps = _hsiEngine.IsSimulating ? SettingsService.Instance.Settings.TargetFps : 100.0;
                    if (fps <= 0) fps = 100;
                    
                    int delayMs = (int)(log.Delay / fps * 1000.0);
                    int pulseMs = 10; 
                    
                    _ = Task.Run(async () => 
                    {
                        if (delayMs > 0) await Task.Delay(delayMs);
                        await _hardwareService.FireChannelAsync(log.ValveId, pulseMs);
                    });
                 });
             }
        }
        
        private void OnModelLoaded(string path)
        {
            try
            {
                _hsiEngine.LoadModel(path);
                
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
                
                // AI가 수정함: 엔진에서 현재 적용된 임계값(MaskRule 포함)을 가져와 UI 설정
                SettingVM.BackgroundThreshold = _hsiEngine.GetCurrentThreshold();
                
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
            _hsiEngine.Stop();
             await _hardwareService.DisconnectAsync();
        }
    }
}
