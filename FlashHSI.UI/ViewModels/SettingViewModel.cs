using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core;
using FlashHSI.Core.Control.Camera;
using FlashHSI.Core.Control.Hardware;
using FlashHSI.Core.Control.Serial;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Messages;
using FlashHSI.Core.Models;
using FlashHSI.Core.Settings;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using JsonSerializer = System.Text.Json.JsonSerializer;
// AI가 추가함: ModelConfig 사용
// AI가 추가함: IEtherCATService 사용
// AI가 추가함: SerialCommandService 사용
// AI가 추가함: SettingsChangedMessage
// AI가 추가함: Feeder 모델
// AI가 추가함: File 사용

// AI가 추가함: model_config.json 부분 수정용

namespace FlashHSI.UI.ViewModels
{
    /// <summary>
    /// 설정 화면 ViewModel - 모델/레퍼런스 로드, 파라미터 설정
    /// </summary>
    /// <ai>AI가 작성함: PageViewModels.cs에서 분리, 카메라 파라미터 추가</ai>
    public partial class SettingViewModel : ObservableObject
    {
        private readonly HsiEngine _hsiEngine;
        private readonly ICameraService _cameraService;
        private readonly IEtherCATService _etherCATService; // AI가 추가함: EtherCAT 서비스
        private readonly SerialCommandService _serialCommandService; // AI가 추가함: 시리얼(피더) 서비스
        private readonly IMessenger _messenger;
        private CancellationTokenSource? _ctsTest; // AI가 추가함: 테스트 취소용
        
        // 시뮬레이션/엔진 설정
        [ObservableProperty] private string _headerPath = "";
        [ObservableProperty] private double _targetFps = 100.0;
        [ObservableProperty] private double _confidenceThreshold = 0.75;
        [ObservableProperty] private double _backgroundThreshold = 3000.0;
        
        // AI가 추가함: Mask Mode 설정
        [ObservableProperty] private MaskMode _selectedMaskMode = MaskMode.Mean;
        [ObservableProperty] private int _maskBandIndex = 80;
        [ObservableProperty] private bool _maskLessThan = true;
        
        [ObservableProperty] private bool _isWhiteRefLoaded;
        [ObservableProperty] private bool _isDarkRefLoaded;
        
        // AI가 추가함: 레퍼런스 파일 경로 표시용 바인딩 프로퍼티
        [ObservableProperty] private string _whiteRefPath = "";
        [ObservableProperty] private string _darkRefPath = "";
        [ObservableProperty] private string _lastWhiteRefPath = "";
        [ObservableProperty] private string _lastDarkRefPath = "";
        [ObservableProperty] private string _lastModelPath = "";
        [ObservableProperty] private string _lastHeaderPath = "";
        
        // AI가 추가함: SVM 모델 시 Confidence 슬라이더 비활성화
        [ObservableProperty] private bool _isConfidenceEnabled = true;
        
        // AI가 추가함: 모델의 MaskRule 활성화 여부 (슬라이더 제어용)
        [ObservableProperty] private bool _isMaskRuleActive;
        
        // AI가 추가함: 카메라 파라미터
        [ObservableProperty] private double _cameraExposureTime = 1000.0;  // μs
        [ObservableProperty] private double _cameraFrameRate = 100.0;      // FPS

        // AI가 추가함: Blob Tracking 파라미터
        [ObservableProperty] private int _blobMinPixels = 5;
        [ObservableProperty] private int _blobLineGap = 5;
        [ObservableProperty] private int _blobPixelGap = 10;

        // AI가 추가함: EtherCAT 연결 및 센서 매핑 파라미터
        [ObservableProperty] private int _fieldOfView;
        [ObservableProperty] private bool _isChannelReverse;
        [ObservableProperty] private string _selectedNetworkInterface = "";
        [ObservableProperty] private uint _etherCATCycleFrequency = 500;
        [ObservableProperty] private string _esiDirectoryPath = "";
        [ObservableProperty] private int _airGunChannelCount = 32;
        [ObservableProperty] private int _cameraSensorSize = 1024;
        [ObservableProperty] private bool _isEtherCATConnected;
        [ObservableProperty] private bool _isMasterOn;
        [ObservableProperty] private bool _isTestRunning;
        [ObservableProperty] private int _testStartChannel = 1;
        [ObservableProperty] private int _testBlowTime = 50;
        [ObservableProperty] private int _testDelay = 200;
        [ObservableProperty] private int _testSingleChannel = 1;
        [ObservableProperty] private string _airGunStatusText = "Disconnected";
        
        /// <ai>AI가 작성함: 네트워크 인터페이스 목록</ai>
        public ObservableCollection<string> NetworkInterfaces { get; } = new();

        // AI가 추가함: 피더(Feeder) 관련 속성
        [ObservableProperty] private int _feederCount;
        [ObservableProperty] private int _allFeederValue;
        [ObservableProperty] private string _selectedSerialPort = "";
        [ObservableProperty] private string _feederStatusText = "미연결";

        /// <ai>AI가 작성함: 피더 리스트 (ObservableCollection)</ai>
        public ObservableCollection<Feeder> FeederList { get; } = new();

        /// <ai>AI가 작성함: 사용 가능한 시리얼 포트 목록</ai>
        public ObservableCollection<string> AvailableSerialPorts { get; } = new();

        /// <ai>AI가 수정함: SerialCommandService 주입 추가</ai>
        public SettingViewModel(HsiEngine engine, ICameraService cameraService, IMessenger messenger, IEtherCATService etherCATService, SerialCommandService serialCommandService)
        {
            _hsiEngine = engine;
            _cameraService = cameraService;
            _messenger = messenger;
            _etherCATService = etherCATService;
            _serialCommandService = serialCommandService;

            // 구독: 프로퍼티 변경 시 설정 저장 및 메시지 전파
            PropertyChanged += OnPropertyChangedHandler;
            
            var s = SettingsService.Instance.Settings;
            _headerPath = s.LastHeaderPath;
            _targetFps = s.TargetFps;
            _confidenceThreshold = s.ConfidenceThreshold;
            _backgroundThreshold = s.BackgroundThreshold;
            
            _isWhiteRefLoaded = !string.IsNullOrEmpty(s.LastWhiteRefPath);
            _isDarkRefLoaded = !string.IsNullOrEmpty(s.LastDarkRefPath);
            // AI가 추가함: 저장된 레퍼런스 경로 복원
            _whiteRefPath = s.LastWhiteRefPath;
            _darkRefPath = s.LastDarkRefPath;
            
            // 카메라 설정 로드
            _cameraExposureTime = s.CameraExposureTime > 0 ? s.CameraExposureTime : 1000.0;
            _cameraFrameRate = s.CameraFrameRate > 0 ? s.CameraFrameRate : 100.0;
            
            // Blob 설정 로드
            _blobMinPixels = s.BlobMinPixels;
            _blobLineGap = s.BlobLineGap;
            _blobPixelGap = s.BlobPixelGap;
            
            // AI가 추가함: EtherCAT / 센서 매핑 설정 로드
            _fieldOfView = s.FieldOfView;
            _isChannelReverse = s.IsChannelReverse;
            _selectedNetworkInterface = s.SelectedNetworkInterface;
            _etherCATCycleFrequency = s.EtherCATCycleFrequency;
            _esiDirectoryPath = s.EsiDirectoryPath;
            _airGunChannelCount = s.AirGunChannelCount;
            _cameraSensorSize = s.CameraSensorSize;
            
            // AI가 추가함: 네트워크 인터페이스 목록 초기화
            LoadNetworkInterfaces();
            
            // AI가 추가함: EtherCAT 서비스 로그 구독
            _etherCATService.LogMessage += msg =>
            {
                AirGunStatusText = msg;
                IsEtherCATConnected = _etherCATService.IsConnected;
                IsMasterOn = _etherCATService.IsMasterOn;
            };

            // AI가 수정함: LoadSavedFilesAsync()는 MainViewModel에서 이벤트 구독 완료 후 호출
            // (SettingVM 생성자에서 fire-and-forget 시 ModelLoaded 이벤트 구독 전에 발생하는 레이스 컨디션 수정)

            // AI: Ejection 설정 로드
            // AI: Ejection 설정 로드
            _ejectionDelayMs = s.EjectionDelayMs;
            _ejectionDurationMs = s.EjectionDurationMs;
            _ejectionBlowMargin = s.EjectionBlowMargin;
            
            if (s.YCorrectionRules != null)
            {
                foreach (var rule in s.YCorrectionRules)
                {
                    YCorrectionRules.Add(rule);
                }
            }

            // AI가 추가함: 피더 설정 로드
            _selectedSerialPort = s.SelectedSerialPort;
            _feederCount = s.FeederCount;
            LoadAvailableSerialPorts();
            FeedersInit();
        }

        /// <summary>
        /// AI가 추가함: 저장된 파일들을 앱 시작 시 자동으로 로드
        /// </summary>
        internal async Task LoadSavedFilesAsync()
        {
            var s = SettingsService.Instance.Settings;

            // White Reference 로드
            if (!string.IsNullOrEmpty(s.LastWhiteRefPath) && File.Exists(s.LastWhiteRefPath))
            {
                try
                {
                    _hsiEngine.LoadReference(s.LastWhiteRefPath, false);
                    IsWhiteRefLoaded = true;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load saved WhiteRef");
                }
            }

            // Dark Reference 로드
            if (!string.IsNullOrEmpty(s.LastDarkRefPath) && File.Exists(s.LastDarkRefPath))
            {
                try
                {
                    _hsiEngine.LoadReference(s.LastDarkRefPath, true);
                    IsDarkRefLoaded = true;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load saved DarkRef");
                }
            }

            // Model 로드
            if (!string.IsNullOrEmpty(s.LastModelPath) && File.Exists(s.LastModelPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(s.LastModelPath);
                    var config = JsonSerializer.Deserialize<ModelConfig>(json);
                    if (config != null)
                    {
                        // AI가 수정함: LoadModel 메서드 호출
                        _hsiEngine.LoadModel(s.LastModelPath);
                        PopulateSortClasses(config);
                        ModelLoaded?.Invoke(s.LastModelPath);
                        
                        // AI가 수정함: 모델 로드 직후 MaskRule 설정 복원 (async 타이밍 버그 수정)
                        LoadMaskRuleSettings();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load saved Model");
                }
            }

            // Header (시뮬레이션 데이터) 경로 복원
            if (!string.IsNullOrEmpty(s.LastHeaderPath) && File.Exists(s.LastHeaderPath))
            {
                HeaderPath = s.LastHeaderPath;
            }
        }

        /// <summary>
        /// AI가 추가함: White Reference 로드
        /// </summary>
        [RelayCommand]
        private void LoadWhiteRef()
        {
            var dlg = new OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _hsiEngine.LoadReference(dlg.FileName, false);
                    WhiteRefPath = dlg.FileName;
                    LastWhiteRefPath = dlg.FileName;
                    SettingsService.Instance.Settings.LastWhiteRefPath = dlg.FileName;
                    SettingsService.Instance.Save();
                    IsWhiteRefLoaded = true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load White Reference");
                }
            }
        }

        /// <summary>
        /// AI가 추가함: Dark Reference 로드
        /// </summary>
        [RelayCommand]
        private void LoadDarkRef()
        {
            var dlg = new OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _hsiEngine.LoadReference(dlg.FileName, true);
                    DarkRefPath = dlg.FileName;
                    LastDarkRefPath = dlg.FileName;
                    SettingsService.Instance.Settings.LastDarkRefPath = dlg.FileName;
                    SettingsService.Instance.Save();
                    IsDarkRefLoaded = true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load Dark Reference");
                }
            }
        }

        /// <summary>
        /// AI가 추가함: 모델 로드 이벤트
        /// </summary>
        public event Action<string>? ModelLoaded;

        /// <summary>
        /// AI가 추가함: 외부에서 ModelLoaded 이벤트를 발행할 수 있도록 하는 메서드
        /// HomeViewModel의 카드 선택 플로우에서 사용해요.
        /// </summary>
        public void RaiseModelLoaded(string path) => ModelLoaded?.Invoke(path);

        /// <summary>
        /// AI가 추가함: 모델 로드 (File Path 탭)
        /// </summary>
        [RelayCommand]
        private async Task LoadModel()
        {
            var dlg = new OpenFileDialog { Filter = "Model JSON (*.json)|*.json" };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(dlg.FileName);
                    var config = JsonSerializer.Deserialize<ModelConfig>(json);
                    if (config != null)
                    {
                        // AI가 수정함: LoadModel 메서드 호출
                        _hsiEngine.LoadModel(dlg.FileName);
                        PopulateSortClasses(config);
                        LastModelPath = dlg.FileName;
                        SettingsService.Instance.Settings.LastModelPath = dlg.FileName;
                        SettingsService.Instance.Save();
                        ModelLoaded?.Invoke(dlg.FileName);
                        
                        // AI가 추가함: 모델 로드 직후 MaskRule 설정 복원
                        LoadMaskRuleSettings();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load model");
                }
            }
        }

        /// <summary>
        /// AI가 추가함: 시뮬레이션 데이터 선택 (File Path 탭)
        /// </summary>
        [RelayCommand]
        private void SelectDataFile()
        {
            var dlg = new OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr" };
            if (dlg.ShowDialog() == true)
            {
                HeaderPath = dlg.FileName;
                LastHeaderPath = dlg.FileName;
                SettingsService.Instance.Settings.LastHeaderPath = dlg.FileName;
                SettingsService.Instance.Save();
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
            // 소수점 3번째 자리까지 유지해서 적용
            Debug.WriteLine($"[SettingVM] BackgroundThreshold 적용됨: {value}");
            
            // AI가 수정함: SetMaskSettings를 통해 전체 설정 업데이트
            _hsiEngine.SetMaskSettings(SelectedMaskMode, null, MaskBandIndex, MaskLessThan, value);
            SettingsService.Instance.Settings.BackgroundThreshold = value;
            SettingsService.Instance.Save();
            SaveMaskSettingsToModelConfig();
        }

        // AI가 수정함: MaskRule 설정 로드 (모델 로드 후 호출)
        // model_config.json의 C# 전용 필드가 있으면 우선 사용, 없으면 엔진에서 읽기
        internal void LoadMaskRuleSettings()
        {
            try
            {
                var config = _hsiEngine.CurrentConfig;
                var prep = config?.Preprocessing;
                
                // JSON에 C# 전용 필드가 저장되어 있으면 우선 사용
                if (prep?.MaskMode != null)
                {
                    if (Enum.TryParse<MaskMode>(prep.MaskMode, out var savedMode))
                        _selectedMaskMode = savedMode;
                    
                    if (prep.MaskBandIndex.HasValue)
                        _maskBandIndex = prep.MaskBandIndex.Value;
                    
                    if (prep.MaskLessThan.HasValue)
                        _maskLessThan = prep.MaskLessThan.Value;
                    
                    if (prep.IsMaskRuleActive.HasValue)
                        _isMaskRuleActive = prep.IsMaskRuleActive.Value;
                    
                    // Threshold는 C# 전용 필드(MaskThreshold)에서 우선 읽고, 없으면 기존 Threshold 폴백
                    if (prep.MaskThreshold.HasValue)
                        _backgroundThreshold = prep.MaskThreshold.Value;
                    else if (double.TryParse(prep.Threshold, CultureInfo.InvariantCulture, out double thresh))
                        _backgroundThreshold = thresh;
                    
                    // MaskRule 2중 구조 컨디션 복원
                    RestoreMaskRuleConditions(prep);
                    
                    // 엔진에도 복원된 설정 적용
                    if (_selectedMaskMode == MaskMode.MaskRule && MaskRuleConditions.ConditionGroups.Count > 0)
                    {
                        _hsiEngine.SetMaskRuleFromCollection(MaskRuleConditions);
                    }
                    else
                    {
                        _hsiEngine.SetMaskSettings(_selectedMaskMode, null, _maskBandIndex, _maskLessThan, _backgroundThreshold);
                    }
                }
                else
                {
                    // C# 전용 필드가 없으면 기존 방식: 엔진에서 읽기
                    var maskInfo = _hsiEngine.GetMaskRuleConditionInfo();
                    _maskBandIndex = maskInfo.bandIndex;
                    _maskLessThan = maskInfo.isLess;
                    _backgroundThreshold = maskInfo.threshold;
                    
                    var currentMode = _hsiEngine.GetMaskSettings().mode;
                    _selectedMaskMode = currentMode;
                }
                
                OnPropertyChanged(nameof(MaskBandIndex));
                OnPropertyChanged(nameof(MaskLessThan));
                OnPropertyChanged(nameof(BackgroundThreshold));
                OnPropertyChanged(nameof(SelectedMaskMode));
                OnPropertyChanged(nameof(IsMaskRuleActive));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load MaskRule settings");
            }
        }

        /// <summary>
        /// AI가 추가함: model_config.json에서 MaskRule 2중 구조 컨디션을 복원
        /// </summary>
        private void RestoreMaskRuleConditions(PreprocessingConfig prep)
        {
            // 기존 컨디션 초기화 (이벤트 발화 방지를 위해 직접 Clear)
            MaskRuleConditions.ConditionGroups.Clear();
            
            if (prep.MaskRuleConditionsData == null || prep.MaskRuleConditionsData.Count == 0)
                return;
            
            foreach (var groupData in prep.MaskRuleConditionsData)
            {
                var group = new MaskRuleConditionGroup();
                
                if (Enum.TryParse<MaskRuleLogicalOperator>(groupData.GroupOperator, out var groupOp))
                    group.GroupOperator = groupOp;
                
                foreach (var condData in groupData.Conditions)
                {
                    var condition = new MaskRuleCondition
                    {
                        BandIndex = condData.BandIndex,
                        Threshold = condData.Threshold,
                        IsLess = condData.IsLess
                    };
                    
                    if (Enum.TryParse<MaskRuleLogicalOperator>(condData.NextOperator, out var nextOp))
                        condition.NextOperator = nextOp;
                    
                    group.Conditions.Add(condition);
                }
                
                MaskRuleConditions.ConditionGroups.Add(group);
            }
        }

        // AI가 추가함: MaskBandIndex 변경 시 (Mean, BandPixel, MaskRule 모드 모두 처리)
        partial void OnMaskBandIndexChanged(int value)
        {
            // Mean 모드에서도 BandIndex 변경 시 엔진에 설정 적용
            if (SelectedMaskMode == MaskMode.Mean)
            {
                _hsiEngine.SetMaskSettings(SelectedMaskMode, null, value, MaskLessThan, BackgroundThreshold);
            }
            else if (SelectedMaskMode == MaskMode.BandPixel)
            {
                _hsiEngine.SetMaskSettings(SelectedMaskMode, null, value, MaskLessThan, BackgroundThreshold);
            }
            else if (SelectedMaskMode == MaskMode.MaskRule)
            {
                _hsiEngine.UpdateMaskRuleCondition(value, BackgroundThreshold, MaskLessThan);
            }
            SaveMaskSettingsToModelConfig();
        }

        // AI가 추가함: Less Than 변경 시 (Mean, BandPixel, MaskRule 모드 모두 처리)
        partial void OnMaskLessThanChanged(bool value)
        {
            // Mean 모드에서도 LessThan 변경 시 엔진에 설정 적용
            if (SelectedMaskMode == MaskMode.Mean)
            {
                _hsiEngine.SetMaskSettings(SelectedMaskMode, null, MaskBandIndex, value, BackgroundThreshold);
            }
            else if (SelectedMaskMode == MaskMode.BandPixel)
            {
                _hsiEngine.SetMaskSettings(SelectedMaskMode, null, MaskBandIndex, value, BackgroundThreshold);
            }
            else if (SelectedMaskMode == MaskMode.MaskRule)
            {
                _hsiEngine.UpdateMaskRuleCondition(MaskBandIndex, BackgroundThreshold, value);
            }
            SaveMaskSettingsToModelConfig();
        }

        // AI가 추가함: Mask Mode 변경 시 전체 설정 업데이트
        partial void OnSelectedMaskModeChanged(MaskMode value)
        {
            // Mean 모드에서는 maskRule을 clear하기 위해 null 전달
            // BandPixel/MaskRule 모드에서는 기존 설정 유지
            _hsiEngine.SetMaskSettings(value, null, MaskBandIndex, MaskLessThan, BackgroundThreshold);
            SaveMaskSettingsToModelConfig();
        }

        // AI가 추가함: IsMaskRuleActive 변경 시 저장
        partial void OnIsMaskRuleActiveChanged(bool value)
        {
            SaveMaskSettingsToModelConfig();
        }

        /// <summary>
        /// AI가 추가함: 배경 마스킹 설정을 model_config.json에 저장
        /// 기존 Python이 생성한 필드는 건드리지 않고, C# 전용 필드만 추가/덮어쓰기
        /// </summary>
        private void SaveMaskSettingsToModelConfig()
        {
            try
            {
                var modelPath = SettingsService.Instance.Settings.LastModelPath;
                if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
                    return;

                var json = File.ReadAllText(modelPath);
                var jObj = JObject.Parse(json);
                
                var prep = jObj["Preprocessing"] as JObject;
                if (prep == null)
                {
                    prep = new JObject();
                    jObj["Preprocessing"] = prep;
                }
                
                // C# 전용 필드만 추가/덮어쓰기 (기존 Python 필드는 그대로 유지)
                prep["MaskMode"] = SelectedMaskMode.ToString();
                prep["MaskBandIndex"] = MaskBandIndex;
                prep["MaskLessThan"] = MaskLessThan;
                prep["IsMaskRuleActive"] = IsMaskRuleActive;
                prep["MaskThreshold"] = BackgroundThreshold;
                
                // MaskRule 2중 구조 컨디션 저장
                var groupsArray = new JArray();
                foreach (var group in MaskRuleConditions.ConditionGroups)
                {
                    var conditionsArray = new JArray();
                    foreach (var cond in group.Conditions)
                    {
                        conditionsArray.Add(new JObject
                        {
                            ["BandIndex"] = cond.BandIndex,
                            ["Threshold"] = cond.Threshold,
                            ["IsLess"] = cond.IsLess,
                            ["NextOperator"] = cond.NextOperator.ToString()
                        });
                    }
                    groupsArray.Add(new JObject
                    {
                        ["GroupOperator"] = group.GroupOperator.ToString(),
                        ["Conditions"] = conditionsArray
                    });
                }
                prep["MaskRuleConditionsData"] = groupsArray;
                
                File.WriteAllText(modelPath, jObj.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save mask settings to model config");
            }
        }
        
        // AI가 추가함: 카메라 파라미터 변경 시 적용
        partial void OnCameraExposureTimeChanged(double value)
        {
            _ = ApplyCameraExposureAsync(value);
        }
        
        partial void OnCameraFrameRateChanged(double value)
        {
            _ = ApplyCameraFrameRateAsync(value);
        }
        
        private async Task ApplyCameraExposureAsync(double value)
        {
            try
            {
                if (_cameraService.IsConnected)
                {
                    await _cameraService.SetParameterAsync("ExposureTime", value);
                    Log.Information("카메라 ExposureTime 설정: {Value} μs", value);
                }
                SettingsService.Instance.Settings.CameraExposureTime = value;
                SettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "카메라 ExposureTime 설정 실패");
            }
        }
        
        private async Task ApplyCameraFrameRateAsync(double value)
        {
            try
            {
                if (_cameraService.IsConnected)
                {
                    await _cameraService.SetParameterAsync("AcquisitionFrameRate", value);
                    Log.Information("카메라 FrameRate 설정: {Value} FPS", value);
                }
                SettingsService.Instance.Settings.CameraFrameRate = value;
                SettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "카메라 FrameRate 설정 실패");
            }
        }
        
        // AI가 추가함: Blob 파라미터 변경 핸들러
        partial void OnBlobMinPixelsChanged(int value) => UpdateBlobSettings();
        partial void OnBlobLineGapChanged(int value) => UpdateBlobSettings();
        partial void OnBlobPixelGapChanged(int value) => UpdateBlobSettings();

        private void UpdateBlobSettings()
        {
            _hsiEngine.UpdateBlobTrackerSettings(BlobMinPixels, BlobLineGap, BlobPixelGap);
            var s = SettingsService.Instance.Settings;
            s.BlobMinPixels = BlobMinPixels;
            s.BlobLineGap = BlobLineGap;
            s.BlobPixelGap = BlobPixelGap;
            SettingsService.Instance.Save();
        }

        // AI가 추가함: Ejection Logic 파라미터
        // AI가 추가함: Ejection Logic 파라미터
        [ObservableProperty] private int _ejectionDelayMs = 300;
        [ObservableProperty] private int _ejectionDurationMs = 10;
        [ObservableProperty] private int _ejectionBlowMargin = 0;

        // Y-Correction Rules
        public ObservableCollection<YCorrectionRule> YCorrectionRules { get; } = new ObservableCollection<YCorrectionRule>();

        // AI가 추가함: MaskRule Conditions
        [ObservableProperty] private MaskRuleLogicalOperator _maskRuleLogicalOperator = MaskRuleLogicalOperator.AND;
        public MaskRuleConditionCollection MaskRuleConditions { get; } = new MaskRuleConditionCollection();

        // AI가 추가함: MaskRuleCondition 변경 시 자동으로 엔진에 적용
        public SettingViewModel()
        {
            // ... existing constructor code ...
            
            // MaskRuleCondition 변경 시 자동으로 HsiEngine에 적용
            MaskRuleConditions.OnConditionChanged += ApplyMaskRuleConditions;
        }

        /// <summary>
        /// AI가 추가함: ESI 디렉토리 폴더 선택
        /// </summary>
        [RelayCommand]
        private void SelectEsiDirectory()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select ESI Directory",
                InitialDirectory = EsiDirectoryPath
            };

            if (dialog.ShowDialog() == true)
            {
                EsiDirectoryPath = dialog.FolderName;
                SettingsService.Instance.Settings.EsiDirectoryPath = EsiDirectoryPath;
                SettingsService.Instance.Save();
                WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<string>(nameof(SystemSettings.EsiDirectoryPath), EsiDirectoryPath));
            }
        }

        [RelayCommand]
        private void AddYCorrectionRule()
        {
            YCorrectionRules.Add(new YCorrectionRule { ThresholdY = 0, CorrectionMs = 0 });
            SettingsService.Instance.Save();
        }

        [RelayCommand]
        private void RemoveYCorrectionRule(YCorrectionRule rule)
        {
            if (rule != null && YCorrectionRules.Contains(rule))
            {
                YCorrectionRules.Remove(rule);
                SettingsService.Instance.Save();
            }
        }
        
        [RelayCommand]
        private void SaveYCorrectionRules()
        {
            SettingsService.Instance.Save();
        }

        // AI가 추가함: MaskRule 2중 구조 Commands
        // Outer level: ConditionGroup 추가 (AND 또는 OR로 연결)
        [RelayCommand]
        private void AddMaskRuleGroupAnd()
        {
            MaskRuleConditions.AddGroup(MaskRuleLogicalOperator.AND);
            ApplyMaskRuleConditions();
        }

        [RelayCommand]
        private void AddMaskRuleGroupOr()
        {
            MaskRuleConditions.AddGroup(MaskRuleLogicalOperator.OR);
            ApplyMaskRuleConditions();
        }

        [RelayCommand]
        private void RemoveMaskRuleGroup(MaskRuleConditionGroup group)
        {
            if (group != null)
            {
                MaskRuleConditions.RemoveGroup(group);
                ApplyMaskRuleConditions();
            }
        }

        // Inner level: ConditionGroup 내에 Rule 추가 (AND 또는 OR로 연결)
        [RelayCommand]
        private void AddMaskRuleAnd(MaskRuleConditionGroup group)
        {
            if (group != null)
            {
                group.AddCondition(MaskRuleLogicalOperator.AND);
                ApplyMaskRuleConditions();
            }
        }

        [RelayCommand]
        private void AddMaskRuleOr(MaskRuleConditionGroup group)
        {
            if (group != null)
            {
                group.AddCondition(MaskRuleLogicalOperator.OR);
                ApplyMaskRuleConditions();
            }
        }

        [RelayCommand]
        private void RemoveMaskRuleFromGroup(MaskRuleCondition condition)
        {
            // Find the group containing this condition
            foreach (var group in MaskRuleConditions.ConditionGroups)
            {
                if (group.Conditions.Contains(condition))
                {
                    group.RemoveCondition(condition);
                    ApplyMaskRuleConditions();
                    break;
                }
            }
        }

        // AI가 추가함: MaskRule 조건을 엔진에 적용 + model_config.json에 저장
        private void ApplyMaskRuleConditions()
        {
            if (SelectedMaskMode == MaskMode.MaskRule && MaskRuleConditions.ConditionGroups.Count > 0)
            {
                _hsiEngine.SetMaskRuleFromCollection(MaskRuleConditions);
            }
            SaveMaskSettingsToModelConfig();
        }

        // AI가 추가함: MaskRuleCondition 변경 시 엔진에 적용
        private void OnMaskRuleConditionChanged()
        {
            ApplyMaskRuleConditions();
        }

        // Note: Using PropertyChanged event instead of OnChanged partial methods
        // due to CommunityToolkit.Mvvm 8.x source generator creating conflicting implementations
        private void OnPropertyChangedHandler(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(TargetFps):
                    SettingsService.Instance.Settings.TargetFps = TargetFps;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<double>(nameof(SystemSettings.TargetFps), TargetFps));
                    break;
                case nameof(ConfidenceThreshold):
                    SettingsService.Instance.Settings.ConfidenceThreshold = ConfidenceThreshold;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<double>(nameof(SystemSettings.ConfidenceThreshold), ConfidenceThreshold));
                    break;
                case nameof(BackgroundThreshold):
                    Debug.WriteLine($"[SettingVM] BackgroundThreshold changed: {BackgroundThreshold}");
                    SettingsService.Instance.Settings.BackgroundThreshold = BackgroundThreshold;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<double>(nameof(SystemSettings.BackgroundThreshold), BackgroundThreshold));
                    break;
                case nameof(BlobMinPixels):
                    SettingsService.Instance.Settings.BlobMinPixels = BlobMinPixels;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.BlobMinPixels), BlobMinPixels));
                    break;
                case nameof(BlobLineGap):
                    SettingsService.Instance.Settings.BlobLineGap = BlobLineGap;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.BlobLineGap), BlobLineGap));
                    break;
                case nameof(BlobPixelGap):
                    SettingsService.Instance.Settings.BlobPixelGap = BlobPixelGap;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.BlobPixelGap), BlobPixelGap));
                    break;
                case nameof(EsiDirectoryPath):
                    SettingsService.Instance.Settings.EsiDirectoryPath = EsiDirectoryPath;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<string>(nameof(SystemSettings.EsiDirectoryPath), EsiDirectoryPath));
                    break;
                case nameof(EjectionDelayMs):
                    SettingsService.Instance.Settings.EjectionDelayMs = EjectionDelayMs;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.EjectionDelayMs), EjectionDelayMs));
                    break;
                case nameof(CameraExposureTime):
                    SettingsService.Instance.Settings.CameraExposureTime = CameraExposureTime;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<double>(nameof(SystemSettings.CameraExposureTime), CameraExposureTime));
                    break;
                case nameof(CameraFrameRate):
                    SettingsService.Instance.Settings.CameraFrameRate = CameraFrameRate;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<double>(nameof(SystemSettings.CameraFrameRate), CameraFrameRate));
                    break;
                case nameof(AirGunChannelCount):
                    SettingsService.Instance.Settings.AirGunChannelCount = AirGunChannelCount;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.AirGunChannelCount), AirGunChannelCount));
                    break;
                case nameof(CameraSensorSize):
                    SettingsService.Instance.Settings.CameraSensorSize = CameraSensorSize;
                    SettingsService.Instance.Save();
                    WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.CameraSensorSize), CameraSensorSize));
                    break;
            }
        }

        // AI가 추가함: EtherCAT / 센서 매핑 설정 변경 핸들러
        partial void OnFieldOfViewChanged(int value)
        {
            SettingsService.Instance.Settings.FieldOfView = value;
            SettingsService.Instance.Save();
            WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.FieldOfView), value));
        }
        partial void OnIsChannelReverseChanged(bool value)
        {
            SettingsService.Instance.Settings.IsChannelReverse = value;
            SettingsService.Instance.Save();
            WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<bool>(nameof(SystemSettings.IsChannelReverse), value));
        }
        partial void OnSelectedNetworkInterfaceChanged(string value)
        {
            SettingsService.Instance.Settings.SelectedNetworkInterface = value;
            SettingsService.Instance.Save();
        }
        partial void OnEtherCATCycleFrequencyChanged(uint value)
        {
            SettingsService.Instance.Settings.EtherCATCycleFrequency = value;
            SettingsService.Instance.Save();
            WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.EtherCATCycleFrequency), (int)value));
        }

        /// <ai>AI가 작성함: NIC 목록 로드</ai>
        private void LoadNetworkInterfaces()
        {
            NetworkInterfaces.Clear();
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Select(n => n.Name);
                foreach (var nic in nics)
                {
                    NetworkInterfaces.Add(nic);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "NIC 목록 로드 실패");
            }
        }

        /// <ai>AI가 작성함: NIC 목록 새로고침</ai>
        [RelayCommand]
        private void RefreshNetworkInterfaces()
        {
            LoadNetworkInterfaces();
        }

        /// <ai>AI가 작성함: EtherCAT 연결</ai>
        [RelayCommand]
        private void ConnectEtherCAT()
        {
            if (string.IsNullOrEmpty(SelectedNetworkInterface))
            {
                AirGunStatusText = "네트워크 인터페이스를 선택해주세요";
                return;
            }
            _etherCATService.Connect(SelectedNetworkInterface, (int)EtherCATCycleFrequency);
            AirGunStatusText = "연결 중...";
        }

        /// <ai>AI가 작성함: EtherCAT 연결 해제</ai>
        [RelayCommand]
        private async Task DisconnectEtherCATAsync()
        {
            await _etherCATService.DisconnectAsync();
            IsEtherCATConnected = false;
            IsMasterOn = false;
            AirGunStatusText = "Disconnected";
        }

        /// <ai>AI가 작성함: 마스터 ON/OFF 토글</ai>
        [RelayCommand]
        private void ToggleMaster()
        {
            if (!_etherCATService.IsConnected)
            {
                AirGunStatusText = "EtherCAT 미연결 상태";
                return;
            }
            var newState = !_etherCATService.IsMasterOn;
            _etherCATService.SetMasterOn(newState);
            IsMasterOn = newState;
        }

        /// <ai>AI가 작성함: 전체 채널 순차 테스트</ai>
        [RelayCommand]
        private async Task TestAllChannelsAsync()
        {
            if (!_etherCATService.IsConnected || !_etherCATService.IsMasterOn) return;
            
            IsTestRunning = true;
            _ctsTest = new CancellationTokenSource();
            try
            {
                await _etherCATService.TestAllChannelAsync(TestStartChannel, TestBlowTime, TestDelay, _ctsTest.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Information("전체 채널 테스트 취소됨");
            }
            finally
            {
                IsTestRunning = false;
            }
        }

        /// <ai>AI가 작성함: 전체 채널 테스트 취소</ai>
        [RelayCommand]
        private void CancelTestAllChannels()
        {
            _ctsTest?.Cancel();
            _etherCATService.CancelTestAllChannel();
            IsTestRunning = false;
        }

        /// <ai>AI가 작성함: 단일 채널 테스트 발사</ai>
        [RelayCommand]
        private async Task TestSingleChannelAsync()
        {
            if (!_etherCATService.IsConnected || !_etherCATService.IsMasterOn) return;
            await _etherCATService.FireChannelAsync(TestSingleChannel, TestBlowTime);
        }

        /// <ai>AI가 작성함: 비상 정지 — 마스터 OFF + 모든 채널 OFF</ai>
        [RelayCommand]
        private async Task EmergencyStopAsync()
        {
            await _etherCATService.CancelAllAsync();
            IsEtherCATConnected = _etherCATService.IsConnected;
            IsMasterOn = false;
            IsTestRunning = false;
            AirGunStatusText = "⛔ 비상 정지 실행됨";
        }

        partial void OnEjectionDelayMsChanged(int value)
        {
            SettingsService.Instance.Settings.EjectionDelayMs = value;
            SettingsService.Instance.Save();
        }
        partial void OnEjectionDurationMsChanged(int value)
        {
            SettingsService.Instance.Settings.EjectionDurationMs = value;
            SettingsService.Instance.Save();
            WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.EjectionDurationMs), value));
        }
        partial void OnEjectionBlowMarginChanged(int value)
        {
            SettingsService.Instance.Settings.EjectionBlowMargin = value;
            SettingsService.Instance.Save();
            WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<int>(nameof(SystemSettings.EjectionBlowMargin), value));
        }

        #region Feeder 관련 로직

        /// <ai>AI가 작성함: 피더 초기화 — 설정에서 피더 개수/값을 읽어 FeederList 생성</ai>
        private void FeedersInit()
        {
            FeederList.Clear();
            var s = SettingsService.Instance.Settings;
            var feederValues = s.FeederValues;

            for (var i = 0; i < FeederCount; i++)
            {
                var value = (feederValues != null && i < feederValues.Count) ? feederValues[i] : 1;
                var feeder = new Feeder(i, value);
                feeder.PropertyChanged += OnFeederPropertyChanged;
                FeederList.Add(feeder);
                Log.Information("{Feeder} — 피더 리스트 초기화", feeder);
            }
        }

        /// <ai>AI가 작성함: 사용 가능한 시리얼 포트 목록 로드</ai>
        private void LoadAvailableSerialPorts()
        {
            AvailableSerialPorts.Clear();
            try
            {
                var ports = _serialCommandService.GetAvailablePorts();
                foreach (var port in ports)
                {
                    AvailableSerialPorts.Add(port);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "시리얼 포트 목록 로드 실패");
            }
        }

        /// <ai>AI가 작성함: 시리얼 포트 목록 새로고침</ai>
        [RelayCommand]
        private void RefreshSerialPorts()
        {
            LoadAvailableSerialPorts();
        }

        /// <ai>AI가 작성함: 시리얼 포트 연결 및 피더 StartUp 실행</ai>
        [RelayCommand]
        private async Task ConnectSerialPortAsync()
        {
            if (string.IsNullOrEmpty(SelectedSerialPort))
            {
                FeederStatusText = "시리얼 포트를 선택해주세요";
                return;
            }

            try
            {
                FeederStatusText = "연결 중...";
                var feederValues = FeederList.Select(f => f.FeederValue).ToList();
                await _serialCommandService.StartUp(SelectedSerialPort, FeederCount, feederValues);
                FeederStatusText = $"연결됨: {SelectedSerialPort}";
                Log.Information("시리얼 포트 연결 완료: {Port}", SelectedSerialPort);
            }
            catch (Exception ex)
            {
                FeederStatusText = $"연결 실패: {ex.Message}";
                Log.Error(ex, "시리얼 포트 연결 실패");
            }
        }

        /// <ai>AI가 작성함: 시리얼 포트 선택 변경 시 설정 저장</ai>
        partial void OnSelectedSerialPortChanged(string value)
        {
            SettingsService.Instance.Settings.SelectedSerialPort = value;
            SettingsService.Instance.Save();
        }

        /// <ai>AI가 작성함: 피더 카운트 변경 처리</ai>
        partial void OnFeederCountChanged(int value)
        {
            if (value < 0 || value > 9)
            {
                Log.Warning("피더 카운트 범위 초과: {Value} (0~9 허용)", value);
                return;
            }

            SettingsService.Instance.Settings.FeederCount = value;
            SettingsService.Instance.Save();

            // 피더 사용/미사용 처리 후 리스트 재구성
            _ = UpdateFeederCountAsync(value);
        }

        /// <ai>AI가 작성함: 피더 카운트 변경 시 시리얼 명령 전송 + 리스트 재구성</ai>
        private async Task UpdateFeederCountAsync(int newCount)
        {
            try
            {
                // 기존 피더 모두 OFF 후 새 카운트만큼 ON
                for (var i = 0; i < 9; i++)
                    await _serialCommandService.FeederNotUseCommandAsync(i);

                for (var i = 0; i < newCount; i++)
                    await _serialCommandService.FeederUseCommandAsync(i);

                FeedersInit();
                Log.Information("피더 카운트 변경: {Count}", newCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "피더 카운트 변경 중 오류");
            }
        }

        /// <ai>AI가 작성함: 전체 피더 값 변경 시 개별 피더에 반영</ai>
        partial void OnAllFeederValueChanged(int value)
        {
            if (FeederCount == 0) return;
            if (value < 1 || value > 99) return;

            foreach (var feeder in FeederList)
                feeder.FeederValue = value;
            
            // 메시지로 전송
            var feederValues = FeederList.Select(f => f.FeederValue).ToList();
            SettingsService.Instance.Settings.FeederValues = feederValues;
            SettingsService.Instance.Save();
            WeakReferenceMessenger.Default.Send(new SettingsChangedMessage<List<int>>(nameof(SystemSettings.FeederValues), feederValues));
        }

        /// <ai>AI가 작성함: 개별 피더 값 1 증가</ai>
        [RelayCommand]
        private void IncreaseFeederValue(Feeder feeder)
        {
            if (feeder == null) return;
            var newValue = Math.Min(feeder.FeederValue + 1, 99);
            if (newValue != feeder.FeederValue)
            {
                feeder.FeederValue = newValue;
                Log.Information("피더 증가: {Number} = {Value}", feeder.FeederNumber, feeder.FeederValue);
                _ = SendFeederValueAsync(feeder.FeederNumber, feeder.FeederValue);
                SaveFeederValueToSettings(feeder.FeederNumber, feeder.FeederValue);
            }
        }

        /// <ai>AI가 작성함: 개별 피더 값 1 감소</ai>
        [RelayCommand]
        private void DecreaseFeederValue(Feeder feeder)
        {
            if (feeder == null) return;
            var newValue = Math.Max(feeder.FeederValue - 1, 1);
            if (newValue != feeder.FeederValue)
            {
                feeder.FeederValue = newValue;
                Log.Information("피더 감소: {Number} = {Value}", feeder.FeederNumber, feeder.FeederValue);
                _ = SendFeederValueAsync(feeder.FeederNumber, feeder.FeederValue);
                SaveFeederValueToSettings(feeder.FeederNumber, feeder.FeederValue);
            }
        }

        /// <ai>AI가 작성함: 전체 피더 값 1 증가</ai>
        [RelayCommand]
        private void IncreaseAllFeederValue()
        {
            var newValue = Math.Min(AllFeederValue + 1, 99);
            if (newValue != AllFeederValue)
            {
                AllFeederValue = newValue;
                foreach (var feeder in FeederList)
                    _ = SendFeederValueAsync(feeder.FeederNumber, feeder.FeederValue);
            }
        }

        /// <ai>AI가 작성함: 전체 피더 값 1 감소</ai>
        [RelayCommand]
        private void DecreaseAllFeederValue()
        {
            var newValue = Math.Max(AllFeederValue - 1, 1);
            if (newValue != AllFeederValue)
            {
                AllFeederValue = newValue;
                foreach (var feeder in FeederList)
                    _ = SendFeederValueAsync(feeder.FeederNumber, feeder.FeederValue);
            }
        }

        /// <ai>AI가 작성함: 슬라이더 드래그 완료 시 시리얼 명령어 전송</ai>
        [RelayCommand]
        private void FeederSliderDragCompleted(object parameter)
        {
            if (parameter is Feeder feeder)
            {
                Log.Information("피더 슬라이더 완료: {Number} = {Value}", feeder.FeederNumber, feeder.FeederValue);
                _ = SendFeederValueAsync(feeder.FeederNumber, feeder.FeederValue);
                SaveFeederValueToSettings(feeder.FeederNumber, feeder.FeederValue);
            }
            else if (parameter is string paramName && paramName == "AllFeederValue")
            {
                Log.Information("전체 피더 슬라이더 완료: {Value}", AllFeederValue);
                foreach (var f in FeederList)
                    _ = SendFeederValueAsync(f.FeederNumber, f.FeederValue);
            }
        }

        /// <ai>AI가 작성함: 피더 값 시리얼 전송</ai>
        private async Task SendFeederValueAsync(int feederNumber, int feederValue)
        {
            try
            {
                await _serialCommandService.SetFeederValueCommandAsync(feederNumber, feederValue);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "피더 값 전송 실패: {Number}={Value}", feederNumber, feederValue);
            }
        }

        /// <ai>AI가 작성함: 피더 값을 시스템 설정에 저장</ai>
        private void SaveFeederValueToSettings(int feederNumber, int feederValue)
        {
            var s = SettingsService.Instance.Settings;
            // 리스트 크기 보장
            while (s.FeederValues.Count <= feederNumber)
                s.FeederValues.Add(1);
            s.FeederValues[feederNumber] = feederValue;
            SettingsService.Instance.Save();
        }

        /// <ai>AI가 작성함: 피더 객체 속성 변경 이벤트 — 실시간 설정 저장</ai>
        private void OnFeederPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not Feeder feeder) return;
            // UI 반영을 위해 설정에 실시간 저장 (시리얼 전송은 드래그 완료 시에만)
            SaveFeederValueToSettings(feeder.FeederNumber, feeder.FeederValue);
        }

        #endregion

        #region Recipe / Sort Class 관련 로직

        /// <ai>AI가 작성함: 분류 클래스 목록 (UI 바인딩용)</ai>
        public ObservableCollection<SortClass> SortClasses { get; } = new();

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// 모델 로드 시 클래스 목록을 생성하고 이전 선택 상태를 복원해요.
        /// </summary>
        public void PopulateSortClasses(ModelConfig config)
        {
            // 기존 구독 해제
            foreach (var sc in SortClasses)
                sc.PropertyChanged -= OnSortClassPropertyChanged;
            SortClasses.Clear();

            if (config?.Labels == null) return;

            var savedSelection = SettingsService.Instance.Settings.SelectedSortClasses ?? new List<int>();

            foreach (var kvp in config.Labels.OrderBy(k => int.TryParse(k.Key, out var n) ? n : 999))
            {
                if (!int.TryParse(kvp.Key, out int index)) continue;

                string name = kvp.Value;
                string colorHex = config.Colors != null && config.Colors.TryGetValue(kvp.Key, out var c) ? c : "#808080";
                bool isSelected = savedSelection.Contains(index);

                var sortClass = new SortClass(index, name, colorHex, isSelected);
                sortClass.PropertyChanged += OnSortClassPropertyChanged;
                SortClasses.Add(sortClass);
            }

            // 초기 타겟 적용
            ApplyEjectionTargets();
        }

        /// <ai>AI가 작성함: SortClass 선택 토글 커맨드</ai>
        [RelayCommand]
        private void ToggleSortClass(SortClass sortClass)
        {
            if (sortClass == null) return;
            sortClass.IsSelected = !sortClass.IsSelected;
            // PropertyChanged 이벤트에서 ApplyEjectionTargets 호출됨
        }

        /// <ai>AI가 작성함: 전체 선택</ai>
        [RelayCommand]
        private void SelectAllSortClasses()
        {
            foreach (var sc in SortClasses)
                sc.IsSelected = true;
        }

        /// <ai>AI가 작성함: 전체 해제</ai>
        [RelayCommand]
        private void DeselectAllSortClasses()
        {
            foreach (var sc in SortClasses)
                sc.IsSelected = false;
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// 리버스 선택: 현재 선택된 클래스는 해제하고, 해제된 클래스는 선택해요.
        /// </summary>
        [RelayCommand]
        private void ReverseSortClasses()
        {
            foreach (var sc in SortClasses)
                sc.IsSelected = !sc.IsSelected;
        }

        /// <summary>
        /// <ai>AI가 작성함</ai>
        /// 선택된 클래스 인덱스를 엔진에 전달하고 설정에 저장해요.
        /// </summary>
        private void ApplyEjectionTargets()
        {
            var selected = SortClasses.Where(sc => sc.IsSelected).Select(sc => sc.Index).ToList();

            // 선택된 것이 없으면 null (전부 사출하지 않음 = 빈 HashSet)
            // 전부 선택이면 null (전부 사출 = 필터 없음)
            HashSet<int>? targets;
            if (selected.Count == 0)
                targets = new HashSet<int>(); // 아무것도 사출하지 않음
            else if (selected.Count == SortClasses.Count)
                targets = null; // 전부 사출 (필터 없음, 성능 최적)
            else
                targets = new HashSet<int>(selected);

            _hsiEngine.SetEjectionTargets(targets);

            // 설정 저장
            SettingsService.Instance.Settings.SelectedSortClasses = selected;
            SettingsService.Instance.Save();
        }

        /// <ai>AI가 작성함: SortClass 속성 변경 시 타겟 갱신</ai>
        private void OnSortClassPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SortClass.IsSelected))
            {
                ApplyEjectionTargets();
            }
        }

        #endregion
    }
}


