using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Control.Camera;
using FlashHSI.Core;                   // AI가 추가함: ModelConfig 사용
using FlashHSI.Core.Control.Hardware; // AI가 추가함: IEtherCATService 사용
using FlashHSI.Core.Control.Serial;   // AI가 추가함: SerialCommandService 사용
using FlashHSI.Core.Engine;
using FlashHSI.Core.Models;            // AI가 추가함: Feeder 모델
using FlashHSI.Core.Settings;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

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
        [ObservableProperty] private bool _isWhiteRefLoaded;
        [ObservableProperty] private bool _isDarkRefLoaded;
        
        // AI가 추가함: 레퍼런스 파일 경로 표시용 바인딩 프로퍼티
        [ObservableProperty] private string _whiteRefPath = "";
        [ObservableProperty] private string _darkRefPath = "";
        
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
            
            // AI가 추가함: 네트워크 인터페이스 목록 초기화
            LoadNetworkInterfaces();
            
            // AI가 추가함: EtherCAT 서비스 로그 구독
            _etherCATService.LogMessage += msg =>
            {
                AirGunStatusText = msg;
                IsEtherCATConnected = _etherCATService.IsConnected;
                IsMasterOn = _etherCATService.IsMasterOn;
            };
            
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
            
            // 초기값 엔진 적용
            _hsiEngine.UpdateBlobTrackerSettings(_blobMinPixels, _blobLineGap, _blobPixelGap);
            
            // AI가 추가함: 피더 설정 로드
            _selectedSerialPort = s.SelectedSerialPort;
            _feederCount = s.FeederCount;
            LoadAvailableSerialPorts();
            FeedersInit();
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
        
        public event Action<string>? ModelLoaded;
        
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
        
        /// <ai>AI가 수정함: 경로 프로퍼티(WhiteRefPath) 업데이트 추가</ai>
        [RelayCommand]
        public void LoadWhiteRef()
        {
             var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr" };
             if (dlg.ShowDialog() == true)
             {
                 _hsiEngine.LoadReference(dlg.FileName, false);
                 WhiteRefPath = dlg.FileName; // AI가 수정함: UI 바인딩용 경로 업데이트
                 SettingsService.Instance.Settings.LastWhiteRefPath = dlg.FileName;
                 SettingsService.Instance.Save();
                 IsWhiteRefLoaded = true;
             }
        }

        /// <ai>AI가 수정함: 경로 프로퍼티(DarkRefPath) 업데이트 추가</ai>
        [RelayCommand]
        public void LoadDarkRef()
        {
             var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "ENVI Header (*.hdr)|*.hdr" };
             if (dlg.ShowDialog() == true)
             {
                 _hsiEngine.LoadReference(dlg.FileName, true);
                 DarkRefPath = dlg.FileName; // AI가 수정함: UI 바인딩용 경로 업데이트
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
            // AI가 수정함: 모드와 관계없이 임계값 동적 업데이트
            _hsiEngine.UpdateBackgroundThreshold(value);
            SettingsService.Instance.Settings.BackgroundThreshold = value;
            SettingsService.Instance.Save();
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

        // AI가 추가함: EtherCAT / 센서 매핑 설정 변경 핸들러
        partial void OnFieldOfViewChanged(int value)
        {
            SettingsService.Instance.Settings.FieldOfView = value;
            SettingsService.Instance.Save();
        }
        partial void OnIsChannelReverseChanged(bool value)
        {
            SettingsService.Instance.Settings.IsChannelReverse = value;
            SettingsService.Instance.Save();
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
        }
        partial void OnEjectionBlowMarginChanged(int value)
        {
            SettingsService.Instance.Settings.EjectionBlowMargin = value;
            SettingsService.Instance.Save();
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
        private void OnFeederPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
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
        private void OnSortClassPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SortClass.IsSelected))
            {
                ApplyEjectionTargets();
            }
        }

        #endregion
    }
}


