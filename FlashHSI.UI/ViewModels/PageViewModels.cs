using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Control.Hardware;
using System.Windows;
using FlashHSI.Core.Settings;
using FlashHSI.Core;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using FlashHSI.Core.Control;
using FlashHSI.Core.Services;

namespace FlashHSI.UI.ViewModels
{
    // ----------------------------------------------------------------------------------
    // 1. HomeViewModel
    // ----------------------------------------------------------------------------------
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

    // ----------------------------------------------------------------------------------
    // 2. StatisticViewModel
    // ----------------------------------------------------------------------------------
    public partial class StatisticViewModel : ObservableObject
    {
         private readonly HsiEngine _hsiEngine;
         private readonly CommonDataShareService _dataShare;
         
         public ObservableCollection<ClassInfo> ClassStats => _dataShare.CurrentClassStats;
         
         [ObservableProperty] private double _fps;
         [ObservableProperty] private long _totalObjects;
         [ObservableProperty] private bool _isCollecting;

         /// <ai>AI가 수정함: DI 주입</ai>
         public StatisticViewModel(HsiEngine engine, CommonDataShareService dataShare)
         {
             _hsiEngine = engine;
             _dataShare = dataShare;
             _hsiEngine.StatsUpdated += OnStatsUpdated;
         }
         
         private void OnStatsUpdated(EngineStats stats)
         {
             if(!IsCollecting) return;
             
             Application.Current.Dispatcher.InvokeAsync(() =>
             {
                 Fps = stats.Fps;
                 
                 long currentTotal = 0;
                 foreach(var c in ClassStats) currentTotal += c.Count;
                 TotalObjects = currentTotal;

                 for (int i = 0; i < stats.ClassCounts.Length; i++)
                 {
                    if (i < ClassStats.Count) ClassStats[i].Count += stats.ClassCounts[i];
                 }
                 
                 if (currentTotal > 0)
                 {
                    foreach(var c in ClassStats)
                        c.Percentage = $"{(double)c.Count / currentTotal * 100:0.00}%";
                 }
             });
         }
         
         [RelayCommand]
         public void ToggleCollection() => IsCollecting = !IsCollecting;
         
         [RelayCommand]
         public void ResetStats()
         {
             _dataShare.ClearStats();
             TotalObjects = 0;
         }
         
         public void InitializeStats(ModelConfig config)
         {
             ClassStats.Clear();
             var sortedKeys = config.Labels.Keys.OrderBy(k => int.Parse(k)).ToList();
             foreach (var key in sortedKeys)
             {
                if (int.TryParse(key, out int index))
                {
                    var name = config.Labels.ContainsKey(key) ? config.Labels[key] : $"Class {index}";
                    var color = config.Colors.ContainsKey(key) ? config.Colors[key] : "#888888";
                    ClassStats.Add(new ClassInfo { Index = index, Name = name, ColorHex = color });
                }
             }
         }
    }

    // ----------------------------------------------------------------------------------
    // 3. SettingViewModel
    // ----------------------------------------------------------------------------------
    public partial class SettingViewModel : ObservableObject
    {
        private readonly HsiEngine _hsiEngine;
        private readonly IMessenger _messenger;
        
        [ObservableProperty] private string _headerPath = "";
        [ObservableProperty] private double _targetFps = 100.0;
        [ObservableProperty] private double _confidenceThreshold = 0.75;
        [ObservableProperty] private double _backgroundThreshold = 3000.0;
        [ObservableProperty] private bool _isWhiteRefLoaded;
        [ObservableProperty] private bool _isDarkRefLoaded;

        /// <ai>AI가 수정함: DI 및 Messenger</ai>
        public SettingViewModel(HsiEngine engine, IMessenger messenger)
        {
            _hsiEngine = engine;
            _messenger = messenger;
            var s = SettingsService.Instance.Settings;
            _headerPath = s.LastHeaderPath;
            _targetFps = s.TargetFps;
            _confidenceThreshold = s.ConfidenceThreshold;
            _backgroundThreshold = s.BackgroundThreshold;
            
            _isWhiteRefLoaded = !string.IsNullOrEmpty(s.LastWhiteRefPath);
            _isDarkRefLoaded = !string.IsNullOrEmpty(s.LastDarkRefPath);
        }

        [RelayCommand]
        public void LoadModel()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Model JSON (*.json)|*.json" };
            if (dlg.ShowDialog() == true)
            {
               ModelLoaded?.Invoke(dlg.FileName);
            }
        }
        
        public event System.Action<string>? ModelLoaded;
        
        [RelayCommand]
        public void SelectDataFile()
        {
             var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr" };
             if (dlg.ShowDialog() == true)
             {
                 HeaderPath = dlg.FileName;
                 SettingsService.Instance.Settings.LastHeaderPath = HeaderPath;
                 SettingsService.Instance.Save();
             }
        }
        
        [RelayCommand]
        public void LoadWhiteRef()
        {
             var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr" };
             if (dlg.ShowDialog() == true)
             {
                 _hsiEngine.LoadReference(dlg.FileName, false);
                 SettingsService.Instance.Settings.LastWhiteRefPath = dlg.FileName;
                 SettingsService.Instance.Save();
                 IsWhiteRefLoaded = true;
             }
        }

        [RelayCommand]
        public void LoadDarkRef()
        {
             var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr" };
             if (dlg.ShowDialog() == true)
             {
                 _hsiEngine.LoadReference(dlg.FileName, true);
                 SettingsService.Instance.Settings.LastDarkRefPath = dlg.FileName;
                 SettingsService.Instance.Save();
                 IsDarkRefLoaded = true;
             }
        }
        
        partial void OnTargetFpsChanged(double value)
        {
            _hsiEngine.SetTargetFps(value);
            SettingsService.Instance.Settings.TargetFps = value;
            SettingsService.Instance.Save();
        }
        
        partial void OnConfidenceThresholdChanged(double value)
        {
            _hsiEngine.SetConfidenceThreshold(value);
            SettingsService.Instance.Settings.ConfidenceThreshold = value;
            SettingsService.Instance.Save();
        }
        
         partial void OnBackgroundThresholdChanged(double value)
        {
             _hsiEngine.SetMaskSettings(FlashHSI.Core.Engine.MaskMode.Mean, null, 0, true, value); 
             SettingsService.Instance.Settings.BackgroundThreshold = value;
             SettingsService.Instance.Save();
        }
    }

    // ----------------------------------------------------------------------------------
    // 4. LogViewModel
    // ----------------------------------------------------------------------------------
    public partial class LogViewModel : ObservableObject
    {
         public ObservableCollection<FlashHSI.Core.Control.EjectionLogItem> Logs { get; } = new();
         
         /// <ai>AI가 수정함: DI</ai>
         public LogViewModel(HsiEngine engine)
         {
             engine.EjectionOccurred += OnEjection;
         }
         
         private void OnEjection(EjectionLogItem item)
         {
             Application.Current.Dispatcher.InvokeAsync(() => {
                 Logs.Insert(0, item);
                 if(Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
             });
         }
         
         [RelayCommand]
         public void ClearLogs() => Logs.Clear();
    }
}
