using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlashHSI.Core.Control.Camera;
using FlashHSI.Core.Engine;
using FlashHSI.UI.Services;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlashHSI.UI.ViewModels
{
    /// <summary>
    /// 라이브 카메라 스트림 및 제어를 담당하는 ViewModel
    /// </summary>
    /// <ai>AI가 작성함</ai>
    public partial class LiveViewModel : ObservableObject
    {
        private readonly ICameraService _cameraService;
        private readonly HsiEngine _hsiEngine;
        private readonly WaterfallService _waterfallService;

        // 카메라 상태
        [ObservableProperty] private bool _isCameraConnected;
        [ObservableProperty] private bool _isLive;
        [ObservableProperty] private bool _isPredicting; // AI가 추가함: 분류 진행 상태
        [ObservableProperty] private string _cameraName = "연결 필요";
        [ObservableProperty] private string _statusMessage = "Ready";

        // Waterfall 이미지 (MainViewModel에서 이동)
        [ObservableProperty] private ImageSource? _waterfallImage;

        /// <ai>AI가 작성함: DI 생성자</ai>
        public LiveViewModel(
            ICameraService cameraService,
            HsiEngine hsiEngine,
            WaterfallService waterfallService)
        {
            _cameraService = cameraService;
            _hsiEngine = hsiEngine;
            _waterfallService = waterfallService;

            // 프레임 처리 이벤트 구독 (MainViewModel에서 이동)
            _hsiEngine.FrameProcessed += OnFrameProcessed;
            
            // AI가 추가함: 카메라 프레임 이벤트 → 분류 파이프라인 연결
            _cameraService.FrameReceived += OnCameraFrameReceived;
            
            // AI가 추가함: 카메라 연결 끊김 이벤트
            _cameraService.ConnectionLost += OnCameraConnectionLost;

            Log.Information("LiveViewModel 생성됨");
        }
        
        /// <summary>
        /// AI가 추가함: 카메라 프레임 수신 → 분류 처리
        /// </summary>
        private void OnCameraFrameReceived(ushort[] data, int width, int height)
        {
            if (!IsPredicting) return; // 분류 모드가 아니면 무시
            
            _hsiEngine.ProcessCameraFrame(data, width, height);
        }
        
        /// <summary>
        /// AI가 추가함: 카메라 연결 끊김 처리
        /// </summary>
        private void OnCameraConnectionLost(string reason)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsCameraConnected = false;
                IsLive = false;
                IsPredicting = false;
                StatusMessage = $"카메라 연결 끊김: {reason}";
                Log.Warning("카메라 연결 끊김: {Reason}", reason);
            });
        }

        /// <summary>
        /// 카메라 연결/해제 토글
        /// </summary>
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
                    Log.Information("카메라 연결 해제");
                    
                    // AI: Ensure Live is also stopped if camera disconnects
                    if (IsLive)
                    {
                        IsLive = false;
                        _hsiEngine.Stop();
                    }
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
                        Log.Information("카메라 연결 성공");
                    }
                    else
                    {
                        IsCameraConnected = false;
                        StatusMessage = "카메라 연결 실패";
                        Log.Warning("카메라 연결 실패");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"연결 오류: {ex.Message}";
                Log.Error(ex, "카메라 연결 오류");
            }
        }

        /// <summary>
        /// 라이브 스트리밍 시작/중지 토글
        /// </summary>
        [RelayCommand]
        private async Task ToggleLive()
        {
            try
            {
                if (IsLive)
                {
                    // 라이브 중지
                    _hsiEngine.Stop();
                    await _cameraService.StopAcquisitionAsync();
                    IsLive = false;
                    StatusMessage = "라이브 중지됨";
                    Log.Information("라이브 스트림 중지");
                }
                else
                {
                    // 라이브 시작
                    if (!IsCameraConnected)
                    {
                        StatusMessage = "카메라를 먼저 연결하세요";
                        return;
                    }

                    await _cameraService.StartAcquisitionAsync();
                    _hsiEngine.StartLive();
                    IsLive = true;
                    StatusMessage = "라이브 스트리밍 중...";
                    Log.Information("라이브 스트림 시작");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"라이브 오류: {ex.Message}";
                Log.Error(ex, "라이브 스트림 오류");
            }
        }

        /// <summary>
        /// 프레임 처리 이벤트 핸들러 (MainViewModel에서 이동)
        /// </summary>
        private void OnFrameProcessed(int[] data, int width, System.Collections.Generic.List<FlashHSI.Core.Analysis.ActiveBlob.BlobSnapshot> blobs)
        {
            // The original code had `if (Application.Current == null) return;`
            // The provided snippet removed it and added `if (_isPaused) return;`
            // Assuming `_isPaused` and `SelectedViewMode` are intended to be added later or are part of a larger context not provided.
            // For now, I will keep the original `Application.Current` check and integrate the new logging and waterfall logic.
            // If `_isPaused` or `SelectedViewMode` are critical, they need to be defined in LiveViewModel.

            if (Application.Current == null) return;

            // Debug Log
            if (blobs.Count > 0 && DateTime.Now.Second % 2 == 0) 
            {
                 Log.Information($"[LiveViewModel] Frame Rx. Blobs={blobs.Count}");
            }

            // Visualize (Waterfall) -> Thread Safe
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // if (_isPaused) return; // Optional check
                // if (SelectedViewMode == LiveViewMode.Classification)
                // {
                //     // Linear Mode
                //     // Draw Line
                // }

                if (_waterfallService.DisplayImage == null)
                {
                    _waterfallService.Initialize(width, 400);
                    WaterfallImage = _waterfallService.DisplayImage;
                }
                
                _waterfallService.AddLine(data, width, blobs);
            }, DispatcherPriority.Render);
        }
        
        /// <summary>
        /// AI가 추가함: 분류 예측 시작/정지 토글
        /// </summary>
        [RelayCommand]
        private void TogglePrediction()
        {
            if (!IsLive)
            {
                StatusMessage = "먼저 라이브 스트리밍을 시작하세요";
                return;
            }
            
            IsPredicting = !IsPredicting;
            StatusMessage = IsPredicting ? "🔮 분류 진행 중..." : "분류 중지됨";
            Log.Information("분류 상태 변경: {State}", IsPredicting);
        }
    }
}
