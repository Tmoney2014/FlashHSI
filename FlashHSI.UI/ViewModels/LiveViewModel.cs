using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Control.Camera;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Messages;
using FlashHSI.UI.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly IMessenger _messenger;

        // 카메라 상태
        [ObservableProperty] private bool _isCameraConnected;
        [ObservableProperty] private bool _isLive;
        [ObservableProperty] private bool _isPredicting; // AI가 추가함: 분류 진행 상태
        [ObservableProperty] private bool _isCapturing; // AI가 추가함: 캡처(데이터 저장) 상태
        [ObservableProperty] private string _cameraName = "연결 필요";
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private int _capturedFrameCount; // AI가 추가함: 캡처된 프레임 수

        // Waterfall 이미지 (MainViewModel에서 이동)
        [ObservableProperty] private ImageSource? _waterfallImage;
        
        // AI가 추가함: 캡처 프레임 버퍼
        private readonly List<ushort[]> _captureBuffer = new();
        private int _captureWidth;
        private int _captureHeight;

        /// <ai>AI가 작성함: DI 생성자</ai>
        public LiveViewModel(
            ICameraService cameraService,
            HsiEngine hsiEngine,
            WaterfallService waterfallService,
            IMessenger messenger)
        {
            _cameraService = cameraService;
            _hsiEngine = hsiEngine;
            _waterfallService = waterfallService;
            _messenger = messenger;

            // 프레임 처리 이벤트 구독 (MainViewModel에서 이동)
            _hsiEngine.FrameProcessed += OnFrameProcessed;
            
            // AI가 추가함: 카메라 프레임 이벤트 → 분류 파이프라인 연결
            _cameraService.FrameReceived += OnCameraFrameReceived;
            
            // AI가 추가함: 카메라 연결 끊김 이벤트
            _cameraService.ConnectionLost += OnCameraConnectionLost;

            Log.Information("LiveViewModel 생성됨");
        }
        
        /// <summary>
        /// AI가 추가함: 카메라 프레임 수신 → 분류 처리 + 캡처 버퍼링
        /// </summary>
        private void OnCameraFrameReceived(ushort[] data, int width, int height)
        {
            // AI가 추가함: 캡처 중이면 프레임을 버퍼에 저장
            if (IsCapturing)
            {
                lock (_captureBuffer)
                {
                    var copy = new ushort[data.Length];
                    Array.Copy(data, copy, data.Length);
                    _captureBuffer.Add(copy);
                    _captureWidth = width;
                    _captureHeight = height;
                    CapturedFrameCount = _captureBuffer.Count;
                }
            }
            
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
        
        /// <summary>
        /// AI가 추가함: 캡처(프레임 데이터 저장) 시작/중지 토글
        /// 시작: 프레임 버퍼링 시작, 중지: 축적된 프레임을 바이너리 파일로 저장
        /// </summary>
        /// <ai>AI가 작성함</ai>
        [RelayCommand]
        private async Task ToggleCapture()
        {
            if (!IsLive)
            {
                StatusMessage = "먼저 라이브 스트리밍을 시작하세요";
                return;
            }
            
            if (IsCapturing)
            {
                // 캡처 중지 → 파일 저장
                IsCapturing = false;
                StatusMessage = "캡처 중지 — 파일 저장 중...";
                Log.Information("캡처 중지, 프레임 수: {Count}", _captureBuffer.Count);
                
                await SaveCaptureBufferAsync();
            }
            else
            {
                // 캡처 시작 → 버퍼 초기화
                lock (_captureBuffer)
                {
                    _captureBuffer.Clear();
                    CapturedFrameCount = 0;
                }
                IsCapturing = true;
                StatusMessage = "📹 캡처 중... (프레임 수집)";
                Log.Information("캡처 시작");
            }
        }
        
        /// <summary>
        /// AI가 추가함: 캡처 버퍼를 바이너리 파일로 저장
        /// 형식: raw ushort16 데이터 (ENVI BSQ와 호환)
        /// </summary>
        /// <ai>AI가 작성함</ai>
        private async Task SaveCaptureBufferAsync()
        {
            List<ushort[]> frames;
            int width, height;
            
            lock (_captureBuffer)
            {
                if (_captureBuffer.Count == 0)
                {
                    StatusMessage = "캡처된 프레임이 없습니다";
                    return;
                }
                
                frames = new List<ushort[]>(_captureBuffer);
                width = _captureWidth;
                height = _captureHeight;
                _captureBuffer.Clear();
            }
            
            try
            {
                // 저장 경로 지정 (Documents/FlashHSI/Captures/)
                var captureDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "FlashHSI", "Captures");
                Directory.CreateDirectory(captureDir);
                
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var dataPath = Path.Combine(captureDir, $"capture_{timestamp}.raw");
                var headerPath = Path.Combine(captureDir, $"capture_{timestamp}.hdr");
                
                // 바이너리 데이터 저장 (백그라운드 스레드)
                await Task.Run(() =>
                {
                    using var fs = new FileStream(dataPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
                    using var bw = new BinaryWriter(fs);
                    
                    foreach (var frame in frames)
                    {
                        foreach (var val in frame)
                        {
                            bw.Write(val);
                        }
                    }
                });
                
                // ENVI 호환 헤더 저장
                var headerContent = $@"ENVI
description = {{FlashHSI Capture {timestamp}}}
samples = {width}
lines = {frames.Count}
bands = {height}
header offset = 0
data type = 12
interleave = bil
byte order = 0
";
                await File.WriteAllTextAsync(headerPath, headerContent);
                
                StatusMessage = $"캡처 저장 완료: {frames.Count}프레임 → {Path.GetFileName(dataPath)}";
                Log.Information("캡처 저장 완료: {Path}, 프레임: {Count}, {Width}x{Height}", 
                    dataPath, frames.Count, width, height);
                
                // Snackbar 알림
                _messenger.Send(new SnackbarMessage($"캡처 저장: {frames.Count}프레임 → {Path.GetFileName(dataPath)}"));
            }
            catch (Exception ex)
            {
                StatusMessage = $"캡처 저장 실패: {ex.Message}";
                Log.Error(ex, "캡처 파일 저장 실패");
            }
        }
    }
}
