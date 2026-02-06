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
        
        // Child ViewModels (Injected)
        public HomeViewModel HomeVM { get; }
        public StatisticViewModel StatisticVM { get; }
        public SettingViewModel SettingVM { get; }
        public LogViewModel LogVM { get; }

        // Global UI State
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private ImageSource? _waterfallImage;
        
        /// <ai>AI가 수정함: 모든 의존성을 생성자를 통해 주입받음 (DI 체인)</ai>
        public MainViewModel(
            HsiEngine hsiEngine,
            WaterfallService waterfallService,
            IEtherCATService hardwareService,
            CommonDataShareService dataShare,
            IMessenger messenger,
            HomeViewModel homeVM,
            StatisticViewModel statisticVM,
            SettingViewModel settingVM,
            LogViewModel logVM)
        {
            _hsiEngine = hsiEngine;
            _waterfallService = waterfallService;
            _hardwareService = hardwareService;
            _dataShare = dataShare;
            _messenger = messenger;

            HomeVM = homeVM;
            StatisticVM = statisticVM;
            SettingVM = settingVM;
            LogVM = logVM;
            
            // 3. Event Subscriptions
            _hsiEngine.LogMessage += msg => StatusMessage = msg;
            _hardwareService.LogMessage += msg => StatusMessage = msg;
            
            _hsiEngine.FrameProcessed += OnFrameProcessed;
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

        private void OnFrameProcessed(int[] data, int width)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_waterfallService.DisplayImage == null)
                {
                    _waterfallService.Initialize(width, 400); 
                    WaterfallImage = _waterfallService.DisplayImage;
                }
                _waterfallService.AddLine(data, width);
            }, DispatcherPriority.Render);
        }

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
                StatusMessage = "Loading Model UI...";
                
                string json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<ModelConfig>(json);
                if (config == null) return;

                StatisticVM.InitializeStats(config);
                _waterfallService.UpdateColorMap(config);
                
                string threshStr = config.Preprocessing.Threshold ?? "0";
                double.TryParse(threshStr, out double thresh);
                
                SettingVM.BackgroundThreshold = thresh;
                
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
