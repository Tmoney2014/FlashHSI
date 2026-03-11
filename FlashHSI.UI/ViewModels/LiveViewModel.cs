using System.Buffers;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FlashHSI.Core.Control.Camera;
using FlashHSI.Core.Control.Hardware;
using FlashHSI.Core.Control.Serial;
using FlashHSI.Core.Engine;
using FlashHSI.Core.Messages;
using FlashHSI.Core.Services; // AI가 추가함: ICaptureService, SettingsService
using FlashHSI.UI.Services;
using Serilog;

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
        private readonly CommonDataShareService _commonDataShareService;
        private readonly IMessenger _messenger;
        private readonly IEtherCATService _etherCATService; // AI: Inject EtherCAT Service
        private readonly SerialCommandService _serialCommandService;
        private readonly ICaptureService _captureService; // AI가 추가함: 캡처 전용 공통 서비스

        // 카메라 상태
        [ObservableProperty] private bool _isCameraConnected;
        [ObservableProperty] private bool _isLive;
        [ObservableProperty] private bool _isPredicting; // AI가 추가함: 분류 진행 상태 (홈/라이브에서 공유)
        [ObservableProperty] private bool _isCapturing; // AI가 추가함: 캡처(데이터 저장) 상태
        [ObservableProperty] private bool _isSimulating; // AI가 추가함: 시뮬레이션 상태
        [ObservableProperty] private string _cameraName = "연결 필요";
        [ObservableProperty] private int _capturedFrameCount; // AI가 추가함: 캡처된 프레임 수

        // Waterfall 이미지 (MainViewModel에서 이동)
        [ObservableProperty] private ImageSource? _waterfallImage;

        /// <ai>AI가 작성함: DI 생성자</ai>
        public LiveViewModel(
            ICameraService cameraService,
            HsiEngine hsiEngine,
            WaterfallService waterfallService,
            CommonDataShareService commonDataShareService,
            IMessenger messenger,
            IEtherCATService etherCATService,
            SerialCommandService serialCommandService,
            ICaptureService captureService) // AI가 추가함: ICaptureService 주입
        {
            _cameraService = cameraService;
            _hsiEngine = hsiEngine;
            _waterfallService = waterfallService;
            _commonDataShareService = commonDataShareService;
            _messenger = messenger;
            _etherCATService = etherCATService;
            _serialCommandService = serialCommandService;
            _captureService = captureService; // 할당

            // 프레임 처리 이벤트 구독 (MainViewModel에서 이동)
            _hsiEngine.FrameProcessed += OnFrameProcessed;
            
            // AI가 추가함: 시뮬레이션 상태 변경 이벤트 구독
            _hsiEngine.SimulationStateChanged += s => Application.Current.Dispatcher.InvokeAsync(() => IsSimulating = s);

            // AI가 추가함: 카메라 프레임 이벤트 → 분류 파이프라인 연결
            _cameraService.FrameReceived += OnCameraFrameReceived;

            // AI가 추가함: 카메라 연결 끊김 이벤트
            _cameraService.ConnectionLost += OnCameraConnectionLost;

            // AI가 추가함: 카메라 연결 성공 이벤트 (홈에서 연결 시 Live 탭에도 반영)
            _cameraService.Connected += () =>
            {
                Application.Current.Dispatcher.InvokeAsync(SyncCameraState);
            };

            // AI가 추가함: 캡처 서비스의 프레임 수 업데이트 구독
            // AI가 수정함: 데드락 방지 — Invoke(동기) → InvokeAsync(비동기)
            _captureService.CapturedFrameCountChanged += (count) =>
            {
                Application.Current.Dispatcher.InvokeAsync(() => CapturedFrameCount = count);
            };

            Log.Information("LiveViewModel 생성됨");

            // AI가 추가함: 생성될 때 이미 카메라가 연결되어 있다면 상태 반영
            SyncCameraState();
        }

        /// <summary>
        /// AI가 추가함: 네비게이션 진입 또는 생성 시 카메라 연결 상태 동기화
        /// </summary>
        private void SyncCameraState()
        {
            if (_cameraService.IsConnected)
            {
                IsCameraConnected = true;
                CameraName = "FX50 Connected"; // 또는 실제 정보 조회
                SendStatus("카메라 연결됨");
            }
            else
            {
                IsCameraConnected = false;
                CameraName = "연결 필요";
                SendStatus("Ready");
            }
        }

        /// <summary>
        /// 통합 상태 메시지 전송 (SystemMessage 경유 → MainViewModel 상태바)
        /// </summary>
        private void SendStatus(string message)
        {
            _messenger.Send(new SystemMessage(message));
        }

        /// <summary>
        /// AI가 추가함: 카메라 프레임 수신 → 분류 처리 + 캡처 버퍼링
        /// </summary>
        private void OnCameraFrameReceived(ushort[] data, int width, int height)
        {
            // AI가 수정함: 캡처 중이면 프레임을 캡처 서비스로 전달
            if (IsCapturing)
            {
                _captureService.AddFrame(data, width, height);
            }

            // AI가 수정함: 라이브 OR 분류 시작이면 프레임 처리 (둘 다 독립적으로 동작)
            if (!IsLive && !IsPredicting) return;

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
                SendStatus($"카메라 연결 끊김: {reason}");
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
                    SendStatus("카메라 연결 해제 중...");
                    await _cameraService.DisconnectAsync();
                    IsCameraConnected = false;
                    CameraName = "연결 필요";
                    SendStatus("카메라 연결 해제됨");
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
                    SendStatus("카메라 연결 중...");
                    bool connected = await _cameraService.ConnectAsync();

                    if (connected)
                    {
                        IsCameraConnected = true;
                        CameraName = "FX50 Connected"; // TODO: 실제 카메라 이름 조회
                        SendStatus("카메라 연결 성공");
                        Log.Information("카메라 연결 성공");
                    }
                    else
                    {
                        IsCameraConnected = false;
                        SendStatus("카메라 연결 실패");
                        Log.Warning("카메라 연결 실패");
                    }
                }
            }
            catch (Exception ex)
            {
                SendStatus($"연결 오류: {ex.Message}");
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
                    // 라이브 중지 → 분류도 꺼져있으면 어큐제이션 중지
                    _hsiEngine.Stop();
                    if (!IsPredicting)
                    {
                        await _cameraService.StopAcquisitionAsync();
                    }
                    IsLive = false;
                    SendStatus("라이브 중지됨");
                    var logMsg = IsPredicting ? "어큐제이션 계속 진행" : "어큐제이션 중지";
                    Log.Information("라이브 중지: {LogMsg}", logMsg);
                }
                else
                {
                    // 라이브 시작 → 분류가 꺼져있으면 어큐제이션 시작
                    if (!IsCameraConnected)
                    {
                        SendStatus("카메라를 먼저 연결하세요");
                        return;
                    }

                    if (!IsPredicting)
                    {
                        await _cameraService.StartAcquisitionAsync();
                        _hsiEngine.StartLive();
                    }
                    else
                    {
                        // 분류가 이미 진행중이면 라이브만 시작
                        _hsiEngine.StartLive();
                    }
                    IsLive = true;
                    SendStatus("라이브 스트리밍 중...");
                    var logMsg = IsPredicting ? "어큐제이션 이미 진행중" : "어큐제이션 시작";
                    Log.Information("라이브 시작: {LogMsg}", logMsg);
                }
            }
            catch (Exception ex)
            {
                SendStatus($"라이브 오류: {ex.Message}");
                Log.Error(ex, "라이브 스트림 오류");
            }
        }

        /// <summary>
        /// 프레임 처리 이벤트 핸들러 (Zero-Allocation 대응)
        /// </summary>
        // AI가 수정함: vizRow(data)도 ArrayPool에서 대여한 버퍼이므로 UI 사용 후 반환 필요
        private void OnFrameProcessed(int[] data, int width, int[] contourData, int contourLen)
        {
            if (Application.Current == null)
            {
                // AI가 수정함: data(vizRow)도 반환해야 함
                if (data != null) ArrayPool<int>.Shared.Return(data);
                if (contourData != null) ArrayPool<int>.Shared.Return(contourData);
                return;
            }

            // Visualize (Waterfall) -> Thread Safe
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_waterfallService.DisplayImage == null)
                    {
                        _waterfallService.Initialize(width, 400);
                        WaterfallImage = _waterfallService.DisplayImage;
                    }

                    _waterfallService.AddLine(data, width, contourData, contourLen);
                }
                finally
                {
                    // AI가 수정함: 엔진에서 ArrayPool로 빌려온 버퍼를 UI 출력 후 즉시 반환 (Zero-Allocation 핵심)
                    if (data != null)
                    {
                        ArrayPool<int>.Shared.Return(data);
                    }
                    if (contourData != null)
                    {
                        ArrayPool<int>.Shared.Return(contourData);
                    }
                }
            }, DispatcherPriority.Render);
        }

        /// <summary>
        /// AI가 추가함: 분류 예측 시작/정지 토글
        /// </summary>
        [RelayCommand]
        private async Task TogglePrediction()
        {
            if (!IsCameraConnected)
            {
                SendStatus("먼저 카메라를 연결하세요");
                return;
            }

            if (!IsPredicting)
            {
                // 분류 시작 → 라이브가 꺼져있으면 어큐제이션 시작
                if (!IsLive)
                {
                    await _cameraService.StartAcquisitionAsync();
                    _hsiEngine.StartLive();
                }
                IsPredicting = true;
                SendStatus("🔮 분류 진행 중...");
                var logMsg = IsLive ? "어큐제이션 이미 진행중" : "어큐제이션 시작";
                Log.Information("분류 시작: {LogMsg}", logMsg);
            }
            else
            {
                // 분류 중지 → 라이브도 꺼져있으면 어큐제이션 중지
                IsPredicting = false;
                if (!IsLive)
                {
                    await _cameraService.StopAcquisitionAsync();
                    _hsiEngine.Stop();
                }
                SendStatus("분류 중지됨");
                var logMsg = IsLive ? "어큐제이션 계속 진행" : "어큐제이션 중지";
                Log.Information("분류 중지: {LogMsg}", logMsg);
            }
        }

        /// <summary>
        /// AI가 추가함: 시뮬레이션 시작/중지 토글
        /// </summary>
        [RelayCommand]
        private void ToggleSimulation()
        {
            if (IsSimulating)
            {
                _hsiEngine.Stop();
                SendStatus("시뮬레이션 중지됨");
                Log.Information("시뮬레이션 중지");
            }
            else
            {
                var hdr = FlashHSI.Core.Settings.SettingsService.Instance.Settings.LastHeaderPath;
                if (!string.IsNullOrEmpty(hdr))
                {
                    _hsiEngine.StartSimulation(hdr);
                    SendStatus("시뮬레이션 실행 중...");
                    Log.Information("시뮬레이션 시작: {Path}", hdr);
                }
                else
                {
                    SendStatus("시뮬레이션 데이터 파일이 없습니다");
                    Log.Warning("시뮬레이션 데이터 파일이 설정되지 않음");
                }
            }
        }

        /// <summary>
        /// AI가 추가함: 캡처(프레임 데이터 저장) 시작/중지 토글
        /// 시작: 프레임 버퍼링 시작, 중지: 축적된 프레임을 바이너리 파일로 저장
        /// </summary>
        /// <ai>AI가 작성함</ai>
        [RelayCommand]
        private async Task ToggleCapture()
        {
            // AI가 수정함: 라이브 또는 시뮬레이션 중 어느 쪽이든 캡처 허용
            if (!IsLive && !IsSimulating)
            {
                SendStatus("먼저 라이브 스트리밍 또는 시뮬레이션을 시작하세요");
                return;
            }

            // AI가 추가함: MROI 활성 시 캡처 차단 (캡처는 전체 밴드가 필요)
            if (FlashHSI.Core.Settings.SettingsService.Instance.Settings.IsMroiEnabled)
            {
                SendStatus("MROI 활성 상태에서는 캡처할 수 없습니다. 설정에서 MROI를 OFF 하세요.");
                Log.Warning("MROI 활성 상태에서 캡처 시도 차단");
                return;
            }

            if (IsCapturing)
            {
                // 캡처 중지 → 파일 저장
                IsCapturing = false;
                SendStatus("캡처 중지 — 파일 저장 중...");
                Log.Information("캡처 중지, 프레임 수: {Count}", _captureService.CurrentCapturedFrameCount);

                await SaveCaptureBufferAsync();
            }
            else
            {
                // 캡처 시작 → 버퍼 초기화
                _captureService.ClearBuffer();
                IsCapturing = true;
                SendStatus("📹 캡처 중... (프레임 수집)");
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

            // AI가 수정함: 캡처 서비스에서 버퍼와 메타데이터를 가져옴
            (frames, width, height) = _captureService.GetCapturedDataAndClear();

            if (frames.Count == 0)
            {
                SendStatus("캡처된 프레임이 없습니다");
                return;
            }

            try
            {
                var settings = FlashHSI.Core.Settings.SettingsService.Instance.Settings;
                string captureDir = settings.CaptureDirectoryPath;
                string baseName = settings.CaptureBaseName;

                if (!Directory.Exists(captureDir))
                {
                    Directory.CreateDirectory(captureDir);
                }

                // AI가 수정함: 파일 생성/복사/헤더 작성 로직 전체를 CaptureService로 위임
                await _captureService.SaveCaptureAsync(
                    baseName: baseName,
                    captureDirectory: captureDir,
                    frames: frames,
                    whiteRefPath: _hsiEngine.CurrentWhiteRefPath,
                    darkRefPath: _hsiEngine.CurrentDarkRefPath,
                    width: width,
                    height: height,
                    cameraService: _cameraService);

                // AI가 수정함: Invoke(동기) → InvokeAsync(비동기) — UI 데드락 방지
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"캡처 완료 ({frames.Count} 프레임)\n저장 폴더:\n{captureDir}", "캡처 성공", MessageBoxButton.OK, MessageBoxImage.Information);
                });

                SendStatus($"캡처 저장 완료: {frames.Count}프레임 → {Path.GetFileName(baseName)}");
                Log.Information("캡처 저장 완료: {Path}, 프레임: {Count}, {Width}x{Height}",
                    Path.Combine(captureDir, baseName), frames.Count, width, height);

                // Snackbar 알림
                _messenger.Send(new SnackbarMessage($"캡처 저장: {frames.Count}프레임 → {Path.GetFileName(baseName)}"));
            }
            catch (Exception ex)
            {
                SendStatus($"캡처 저장 실패: {ex.Message}");
                Log.Error(ex, "캡처 파일 저장 실패");
                // AI가 수정함: Invoke(동기) → InvokeAsync(비동기) — UI 데드락 방지
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"캡처 저장 중 오류가 발생했습니다.\n\n{ex.Message}", "캡처 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }
}
