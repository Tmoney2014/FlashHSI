using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Control.Camera;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Settings;
using Serilog;
using System;
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
        private readonly IMessenger _messenger;
        
        // 시뮬레이션/엔진 설정
        [ObservableProperty] private string _headerPath = "";
        [ObservableProperty] private double _targetFps = 100.0;
        [ObservableProperty] private double _confidenceThreshold = 0.75;
        [ObservableProperty] private double _backgroundThreshold = 3000.0;
        [ObservableProperty] private bool _isWhiteRefLoaded;
        [ObservableProperty] private bool _isDarkRefLoaded;
        
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

        /// <ai>AI가 수정함: ICameraService 주입 추가</ai>
        public SettingViewModel(HsiEngine engine, ICameraService cameraService, IMessenger messenger)
        {
            _hsiEngine = engine;
            _cameraService = cameraService;
            _messenger = messenger;
            
            var s = SettingsService.Instance.Settings;
            _headerPath = s.LastHeaderPath;
            _targetFps = s.TargetFps;
            _confidenceThreshold = s.ConfidenceThreshold;
            _backgroundThreshold = s.BackgroundThreshold;
            
            _isWhiteRefLoaded = !string.IsNullOrEmpty(s.LastWhiteRefPath);
            _isDarkRefLoaded = !string.IsNullOrEmpty(s.LastDarkRefPath);
            
            // 카메라 설정 로드
            _cameraExposureTime = s.CameraExposureTime > 0 ? s.CameraExposureTime : 1000.0;
            _cameraFrameRate = s.CameraFrameRate > 0 ? s.CameraFrameRate : 100.0;
            
            // Blob 설정 로드
            _blobMinPixels = s.BlobMinPixels;
            _blobLineGap = s.BlobLineGap;
            _blobPixelGap = s.BlobPixelGap;
            
            // 초기값 엔진 적용
            _hsiEngine.UpdateBlobTrackerSettings(_blobMinPixels, _blobLineGap, _blobPixelGap);
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
    }
}

