using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlashHSI.Core.Engine;
using FlashHSI.Core;
using FlashHSI.Core.Settings;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FlashHSI.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly HsiEngine _hsiEngine;
        
        // UI State
        [ObservableProperty] private double _confidenceThreshold = 0.75;
        [ObservableProperty] private double _backgroundThreshold = 3000.0;
        
        // Stats
        [ObservableProperty] private long _unknownCount; 
        [ObservableProperty] private long _backgroundCount; 
        [ObservableProperty] private long _detectedObjects;
        [ObservableProperty] private double _fps;
        [ObservableProperty] private long _totalFrames;

        // Status
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _isSimulating;
        [ObservableProperty] private double _targetFps = 100.0;
        [ObservableProperty] private bool _isWhiteRefLoaded;
        [ObservableProperty] private bool _isDarkRefLoaded;

        // Model & Data
        private string _headerPath = "";
        private ModelConfig? _currentConfig;
        
        public ObservableCollection<ClassInfo> ClassStats { get; } = new();

        public MainViewModel()
        {
            _hsiEngine = new HsiEngine();
            _hsiEngine.StatsUpdated += OnStatsUpdated;
            _hsiEngine.LogMessage += msg => Application.Current.Dispatcher.Invoke(() => StatusMessage = msg);
            _hsiEngine.SimulationStateChanged += state => Application.Current.Dispatcher.Invoke(() => IsSimulating = state);

            // Load Settings
            var s = SettingsService.Instance.Settings;
            _headerPath = s.LastHeaderPath;
            _targetFps = s.TargetFps;
            _confidenceThreshold = s.ConfidenceThreshold;
            _backgroundThreshold = s.BackgroundThreshold;

            _hsiEngine.SetTargetFps(_targetFps);
            // Engine settings will be applied when model loads or user changes them
        }

        private void OnStatsUpdated(EngineStats stats)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UnknownCount += stats.Unknown;
                BackgroundCount += stats.Background;
                DetectedObjects += stats.Objects;
                TotalFrames += stats.ProcessedLines;
                Fps = stats.Fps;

                for (int i = 0; i < stats.ClassCounts.Length; i++)
                {
                    if (i < ClassStats.Count)
                    {
                        ClassStats[i].Count += stats.ClassCounts[i];
                    }
                }
                
                string debugInfo = $"Obj: {DetectedObjects}"; 
                StatusMessage = $"Running... {debugInfo} | Fps: {Fps}";
            }, DispatcherPriority.Background);
        }

        [RelayCommand]
        public void LoadModel()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Model JSON (*.json)|*.json|All files (*.*)|*.*";
            if (dlg.ShowDialog() == true)
            {
                LoadModelFromFile(dlg.FileName);
            }
        }

        private void LoadModelFromFile(string path)
        {
            try
            {
                // Delegate to Engine
                _hsiEngine.LoadModel(path);

                // UI Setup (Colors/Names)
                StatusMessage = "Loading Model UI...";
                string json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<ModelConfig>(json);
                if (config == null) return;
                _currentConfig = config;

                ClassStats.Clear();
                int count = config.Weights.Count;
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
                while (ClassStats.Count < count)
                {
                    ClassStats.Add(new ClassInfo { Index = ClassStats.Count, Name = $"Class {ClassStats.Count}", ColorHex = "#888888" });
                }
                
                // Set initial settings to Engine 
                // (Engine LoadModel might overwrite with Config defaults, so we might need to re-apply User Overrides if desired)
                // For now, let's assume Config defaults are respected but User UI sliders override them.
                
                // Config might have Mask Rules
                ApplyMaskSettings();

                StatusMessage = $"Model Loaded: {config.ModelType}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show(ex.Message);
            }
        }

        private void ApplyMaskSettings()
        {
             // We need to parse mask rules again here or trust Engine?
             // Engine doesn't expose Mask Parsing helper currently.
             // We can duplicate parsing logic or move Parser to Core (It is in Core).
             if (_currentConfig == null) return;
             
             string rules = _currentConfig.Preprocessing.MaskRules ?? "Mean";
             string threshStr = _currentConfig.Preprocessing.Threshold ?? "0";
             
             double thresh = 0;
             double.TryParse(threshStr, out thresh);
             BackgroundThreshold = thresh; // Update UI

             try 
             {
                 var rule = FlashHSI.Core.Masking.MaskRuleParser.Parse(rules);
                 if (rule != null)
                 {
                    // Advanced rule
                    // Extract Band Index if present for Simple display?
                    // Let's just push to Engine
                    // We need to extract arguments for Engine.SetMaskSettings
                    // Engine SetMaskSettings takes (Mode, Rule, BandIndex, LessThan, Thresh)
                    
                    // Simple extraction logic for BandPixel mode:
                    int bIdx = 0;
                    var match = System.Text.RegularExpressions.Regex.Match(rules, @"[bB](\d+)");
                    if (match.Success) int.TryParse(match.Groups[1].Value, out bIdx);
                    
                    bool less = rules.Contains("<");
                    
                    _hsiEngine.SetMaskSettings(FlashHSI.Core.Engine.MaskMode.MaskRule, rule, bIdx, less, thresh);
                 }
                 else
                 {
                    _hsiEngine.SetMaskSettings(FlashHSI.Core.Engine.MaskMode.Mean, null, 0, true, BackgroundThreshold);
                 }
             }
             catch
             {
                 _hsiEngine.SetMaskSettings(FlashHSI.Core.Engine.MaskMode.Mean, null, 0, true, BackgroundThreshold);
             }
        }

        [RelayCommand]
        public void ToggleSimulation()
        {
            if (IsSimulating)
            {
                _hsiEngine.Stop();
            }
            else
            {
                if (string.IsNullOrEmpty(_headerPath)) { StatusMessage = "Select Data First!"; return; }
                _hsiEngine.StartSimulation(_headerPath);
            }
        }

        [RelayCommand]
        public void SelectDataFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "ENVI Header (*.hdr)|*.hdr";
            if (dlg.ShowDialog() == true)
            {
                _headerPath = dlg.FileName;
                StatusMessage = $"Data: {Path.GetFileName(_headerPath)}";
                SettingsService.Instance.Settings.LastHeaderPath = _headerPath;
                SettingsService.Instance.Save();
            }
        }

        [RelayCommand]
        public void LoadWhiteRef()
        {
            LoadRef(false);
        }

        [RelayCommand]
        public void LoadDarkRef()
        {
            LoadRef(true);
        }

        private void LoadRef(bool isDark)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "ENVI Header (*.hdr)|*.hdr";
            if (dlg.ShowDialog() == true)
            {
                var data = _hsiEngine.LoadReference(dlg.FileName, isDark);
                if (data != null)
                {
                    string name = Path.GetFileName(dlg.FileName);
                    if (isDark) { IsDarkRefLoaded = true; StatusMessage = $"Dark Ref: {name}"; SettingsService.Instance.Settings.LastDarkRefPath = dlg.FileName; }
                    else { IsWhiteRefLoaded = true; StatusMessage = $"White Ref: {name}"; SettingsService.Instance.Settings.LastWhiteRefPath = dlg.FileName; }
                    
                    SettingsService.Instance.Save();
                }
            }
        }

        [RelayCommand]
        public void ResetStats()
        {
            UnknownCount = 0;
            BackgroundCount = 0;
            DetectedObjects = 0;
            TotalFrames = 0;
            Fps = 0;
            foreach(var c in ClassStats) c.Count = 0;
        }

        // Property Change Handlers
        partial void OnTargetFpsChanged(double value)
        {
            _hsiEngine.SetTargetFps(value);
            SettingsService.Instance.Settings.TargetFps = value;
            SettingsService.Instance.Save();
        }

        partial void OnBackgroundThresholdChanged(double value)
        {
             // Update Engine Threshold
             // Note: Engine needs re-configuration of usage? 
             // SetMaskSettings takes threshold. We should re-call SetMaskSettings or add SetThreshold.
             // For now re-apply mask settings logic or simplified SetThreshold
             _hsiEngine.SetMaskSettings(FlashHSI.Core.Engine.MaskMode.Mean, null, 0, true, value); 
             // Caution: This overwrites MaskRule if active. Ideally Engine should have dedicated SetThreshold.
             // But for Mean mode (default slider) this is fine.
             
             SettingsService.Instance.Settings.BackgroundThreshold = value;
             SettingsService.Instance.Save();
        }

        partial void OnConfidenceThresholdChanged(double value)
        {
            // Engine doesn't expose Pipeline for Threshold setting directly?
            // HsiPipeline has SetThreshold.
            // We need to Expose this on Engine.
             _hsiEngine.SetConfidenceThreshold(value); 
            
            SettingsService.Instance.Settings.ConfidenceThreshold = value;
            SettingsService.Instance.Save();
        }
    }


}
