# FlashHSI.UI — WPF 프레젠테이션 레이어

**역할**: DI 등록·앱 진입점·MVVM ViewModel·WPF View. Core 서비스를 주입받아 UI를 구동.

---

## STRUCTURE

```
FlashHSI.UI/
├── App.xaml / App.xaml.cs     # ⭐ 앱 진입점 + DI 컨테이너 구성
├── MainWindow.xaml / .cs      # 최상위 Shell 창
├── ViewModels/                # MVVM ViewModel (CommunityToolkit.Mvvm)
│   ├── MainViewModel.cs       # DI 허브, 종료 시퀀스, 하드웨어 트리거
│   ├── HomeViewModel.cs       # 홈 탭 (시뮬레이션 시작/정지, 모델 로드)
│   ├── LiveViewModel.cs       # 라이브 카메라 스트림, FrameProcessed 구독
│   ├── SettingViewModel.cs    # 설정 탭 (마스킹, 레퍼런스, SortClass)
│   ├── StatisticViewModel.cs  # 통계 탭 (분류 결과 집계)
│   └── LogViewModel.cs        # 로그 탭
├── Views/Pages/               # 각 탭 XAML View (.xaml + .cs)
├── Services/                  # UI 전용 서비스
│   ├── WaterfallService.cs    # 폭포수 이미지 렌더링 (vizRow → Bitmap)
│   ├── MessengerLogMessageSender.cs  # ILogMessageSender → Messenger 브릿지
│   └── WindowModalService.cs  # 모달 다이얼로그
├── Behaviors/                 # Microsoft.Xaml.Behaviors XAML 첨부 동작
├── Converters/                # IValueConverter 구현 (9종)
└── AssemblyInfo.cs
```

---

## WHERE TO LOOK

| Task | 위치 |
|------|------|
| 새 서비스 DI 등록 | `App.xaml.cs` → `ConfigureServices()` — **유일한 등록 지점** |
| 새 탭/페이지 추가 | `Views/Pages/` + `ViewModels/` + DI 등록 |
| 폭포수 렌더링 수정 | `Services/WaterfallService.cs` |
| 종료 시퀀스 변경 | `MainViewModel.cs` → `WindowClosing()`, `PerformFullShutDown()` |
| 모델 로드 후 UI 업데이트 | `MainViewModel.OnModelLoaded()` |
| ArrayPool vizRow 반환 위치 | `LiveViewModel.cs` (FrameProcessed 핸들러 내 `Dispatcher.InvokeAsync` 완료 후) |
| 새 Converter 추가 | `Converters/` (네이밍: `XxxToYyyConverter.cs`) |

---

## DI 등록 목록 (`App.xaml.cs`)

| 타입 | 수명 | 비고 |
|------|------|------|
| `IMessenger` | Singleton | `WeakReferenceMessenger.Default` |
| `ILogMessageSender` | Singleton | `MessengerLogMessageSender` |
| `HsiEngine` | Singleton | Core 엔진 |
| `WaterfallService` | Singleton | |
| `IEtherCATService` | Singleton | `EtherCATService` |
| `SerialCommandService` | Singleton | |
| `ICaptureService` | Singleton | `CaptureService` |
| `ICameraService` | Singleton | `PleoraCameraService` |
| `MemoryMonitoringService` | Singleton | 시작 시 `StartMonitoring()` 호출 |
| `IWindowModalService` | Singleton | |
| `CommonDataShareService` | Singleton | |
| 모든 ViewModel | Singleton | MainViewModel 포함 |

---

## THREADING MODEL

- **UI Thread**: WPF Dispatcher — 렌더링 및 입력.
- **Simulation Thread**: `HsiEngine.StartSimulation()` → `ThreadPriority.AboveNormal` 별도 스레드.
- **Live Mode**: `PleoraCameraService` 콜백 → `HsiEngine.ProcessCameraFrame()` 직접 호출, 별도 스레드 없음.
- UI 업데이트는 반드시 `Application.Current.Dispatcher.InvokeAsync()` 경유.

---

## MESSAGING (CommunityToolkit.Mvvm)

| 메시지 | 발신자 | 수신자 | 용도 |
|--------|--------|--------|------|
| `SnackbarMessage` | Core/UI 서비스 | `MainViewModel` | 하단 알림 |
| `BusyMessage` | 서비스 | `MainViewModel` | 로딩 오버레이 |
| `SystemMessage` | ViewModel | `MainViewModel` | 상태바 텍스트 |
| `SettingsChangedMessage<T>` | `SettingsService` | `HsiEngine` | 실시간 설정 반영 |

---

## ANTI-PATTERNS

| 금지 | 이유 |
|------|------|
| UI 스레드에서 `ArrayPool` 반환 전 재사용 | `vizRow` 레이스 컨디션 |
| ViewModel에서 `new HsiEngine()` 직접 생성 | DI 우회, 싱글톤 깨짐 |
| `App.xaml.cs` 외 다른 곳에서 DI 등록 | 등록 지점 분산 |
| `ConfigureServices()` 외부에서 `IServiceProvider` 재구성 | 예측 불가 상태 |
| `Dispatcher.Invoke()` (동기) 사용 | UI 데드락 위험 → `InvokeAsync()` 사용 |

---

## NOTES

- `MaterialDesignThemes` (4.9.0) — 테마/스타일의 원천. XAML 스타일 오버라이드 시 주의.
- `SnackbarMessageQueue`는 `MainViewModel`에서 `DiscardDuplicates = true` 설정됨.
- 앱 종료 시 램프 온도 > 0이면 냉각 대기 루프(`DispatcherTimer`) 진입 후 완전 종료.
- `OnStartup()`에서 GC `SustainedLowLatency` + `ProcessPriorityClass.High` 설정.
