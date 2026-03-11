# FlashHSI — 프로젝트 진입점 지식베이스

**Stack**: C# 12.0 · .NET 8 · WPF · MVVM (CommunityToolkit.Mvvm)  
**Purpose**: 산업용 초분광 선별기(Hyperspectral Sorter) 실시간 분류 런타임.  
Python에서 학습된 선형 모델(`model.json`)을 로드하여 >200 FPS로 픽셀 단위 분류 수행.

> 이 문서는 **프로젝트 전체 진입점**이다. 상세 내용은 각 레이어의 AGENTS.md를 참조한다.  
> `FlashHSI.Core/AGENTS.md` · `FlashHSI.UI/AGENTS.md` · `FlashHSI.Tests/AGENTS.md`

---

## STRUCTURE

```
FlashHSI/
├── FlashHSI.Core/        # 순수 연산 라이브러리 — UI 의존성 없음
├── FlashHSI.UI/          # WPF 앱 — DI 등록, MVVM ViewModel, View
├── FlashHSI.Tests/       # xUnit 유닛 테스트 + 성능 게이트 (BenchmarkDotNet)
├── docs/
│   ├── CSHARP_INFERENCE_RUNTIME_SPEC.md   # model.json 스키마 + 추론 파이프라인 스펙
│   └── BACKGROUND_MASKING_DESIGN.md       # 배경 마스킹 설계 원칙
├── .agent/workflows/
│   ├── commit.md          # /commit 슬래시 커맨드
│   └── daily-report.md    # /daily-report 슬래시 커맨드
├── FlashHSI.sln
├── global.json            # .NET SDK 버전 고정
└── README.md
```

---

## WHERE TO LOOK

| 작업 | 파일 |
|------|------|
| 분류 알고리즘 (LDA/SVM/PLS) | `FlashHSI.Core/Classifiers/LinearClassifier.cs` |
| 전처리 파이프라인 | `FlashHSI.Core/Pipelines/HsiPipeline.cs` |
| 메인 엔진 (루프/마스킹/블롭/사출) | `FlashHSI.Core/Engine/HsiEngine.cs` |
| 배경 마스킹 규칙 파싱 | `FlashHSI.Core/Masking/MaskRule.cs` |
| Feature 추출기 | `FlashHSI.Core/Preprocessing/` (`LogGap*`, `RawGap*`) |
| 블롭 추적 | `FlashHSI.Core/Analysis/BlobTracker.cs` |
| 카메라 서비스 | `FlashHSI.Core/Control/Camera/PleoraCameraService.cs` |
| 에어건 사출 | `FlashHSI.Core/Control/EjectionService.cs` |
| EtherCAT I/O | `FlashHSI.Core/Control/Hardware/EtherCATService.cs` |
| 시리얼 제어 | `FlashHSI.Core/Control/Serial/SerialCommandService.cs` |
| ENVI 파일 IO | `FlashHSI.Core/IO/EnviReader.cs` |
| 설정 저장/로드 | `FlashHSI.Core/Settings/SettingsService.cs` |
| model.json 역직렬화 스키마 | `FlashHSI.Core/ModelData.cs` |
| DI 등록 / 앱 진입점 | `FlashHSI.UI/App.xaml.cs` |
| ViewModel 허브 | `FlashHSI.UI/ViewModels/MainViewModel.cs` |
| 폭포수 렌더링 | `FlashHSI.UI/Services/WaterfallService.cs` |
| 성능 게이트 임계값 | `FlashHSI.Tests/BenchmarkTests.cs` |
| model.json 전체 스펙 | `docs/CSHARP_INFERENCE_RUNTIME_SPEC.md` |
| 마스킹 설계 원칙 | `docs/BACKGROUND_MASKING_DESIGN.md` |

---

## ARCHITECTURE

### 레이어 분리

```
FlashHSI.UI  →  FlashHSI.Core  →  libs/ (PvDotNet.dll — Windows 전용)
```

- **Core**: UI 의존성 없음. 순수 C# 알고리즘. `unsafe` + pointer 연산 허용.
- **UI**: Core 서비스를 DI로 주입받음. `App.xaml.cs`의 `ConfigureServices()`가 **유일한** DI 등록 지점.
- **Core → UI 방향 참조 금지** (레이어 오염).

### Hot Path (성능 임계 코드)

```
HsiEngine.RunLoop() / ProcessCameraFrame()
  └─ HsiPipeline.ProcessFrame()          ← unsafe stackalloc (rawBuffer, featureBuffer)
       ├─ IFeatureExtractor.Extract()     ← LogGapFeatureExtractor or RawGapFeatureExtractor
       └─ IClassifier.Predict()          ← LinearClassifier [AggressiveInlining]
```

**성능 목표**: 1 Line (640px) 처리 시간 < **1.0ms** (테스트 게이트: < 1.4ms)

### 핵심 인터페이스

| Interface | 구현체 | 역할 |
|-----------|--------|------|
| `IClassifier` | `LinearClassifier` | LDA / SVM / PLS-DA 통합 판정 |
| `IFeatureExtractor` | `LogGapFeatureExtractor`, `RawGapFeatureExtractor` | Gap Difference Feature 추출 |
| `IHsiFrameProcessor` | `SnvProcessor`, `MinMaxProcessor`, `L2NormalizeProcessor` | 전처리 체인 |
| `ICameraService` | `PleoraCameraService` | Pleora 카메라 스트림 |
| `IEtherCATService` | `EtherCATService` | 에어건 EtherCAT I/O |
| `ICaptureService` | `CaptureService` | ENVI 캡처 |
| `ILogMessageSender` | `MessengerLogMessageSender` | 로그 → Messenger 브릿지 |

### 분류 로직 요약

| 모델 타입 | `OriginalType` 값 | 판정 방식 |
|----------|-----------------|---------|
| LDA | (기본값) | Softmax → ConfidenceThreshold → Unknown(-1) 가능 |
| SVM | `"SVM"` / `"SVC"` | ArgMax only (Threshold 무시, Winner-Takes-All) |
| PLS-DA | `"PLS"` | 점수 clamp(0,1) → ConfidenceThreshold → Unknown(-1) 가능 |

### 마스킹 순서 (필수 준수)

```
Raw 데이터 수신
  → MaskRules 평가 (Raw DN 값 기준)
  → [배경] classificationRow[x] = -1  ← SKIP
  → [객체] HsiPipeline.ProcessFrame()
```

`MaskRules`는 **반드시 Raw DN 값 기준** (`b80 > 35000` ✅ / `b80 > 0.5` ❌).  
Calibration(White/Dark)은 Feature 추출 단계에서 적용하며, 마스킹 이전에 적용하지 않는다.

---

## CONVENTIONS

### 언어
- **주석, 커밋 메시지** → 한국어 ("해요"체)
- **코드 식별자** (변수명, 메서드명, 클래스명) → 영어

### AI 코드 표시 (필수)
```csharp
/// <summary>
/// <ai>AI가 작성함</ai>
/// 메서드 설명
/// </summary>
public void NewMethod() { ... }

// AI가 수정함: [이유]  ← 기존 코드 수정 시
```

### Git 커밋
- 타입 프리픽스: `feat` · `fix` · `docs` · `style` · `refactor` · `perf` · `test` · `chore`
- 형식: `<type>: <subject>` (제목 50자 이내, 명령형, 한국어)
- **명시적 요청 없이는 커밋하지 않는다.**
- **커밋 이후 Push는 하지 않는다.**
- **수정 개연성이 있는 파일들끼리만 묶어 별도 커밋한다** (논리적 단위 분리).

```
feat: Log-Gap-Diff 전처리 로직 구현
fix: 배경 임계값 동적 업데이트 오류 수정
perf: LinearClassifier 내적 연산 SIMD 적용
```

---

## ANTI-PATTERNS (절대 금지)

| 금지 패턴 | 이유 |
|-----------|------|
| `AI지침: 코드변경 금지` 주석이 있는 코드 수정 | 절대 금지 |
| Hot Path 내 `new` 객체 생성 | GC 스파이크 → 레이턴시 급증 |
| Hot Path 수정 시 성능 근거 없이 변경 | 벤치마크 또는 예측 근거 필수 |
| `MaskRules`를 반사율(0~1) 기준으로 작성 | Raw DN 값 기준이어야 함 |
| Core 프로젝트에 UI 의존성 추가 | 레이어 오염 |
| `stackalloc`에 밴드 수 2000 초과 | 스택 오버플로우 위험 |
| `vizRow`/`contourData`를 엔진에서 `ArrayPool.Return` | UI 측에서 반환해야 함 (레이스 컨디션) |
| `Dispatcher.Invoke()` (동기) 사용 | UI 데드락 위험 → `InvokeAsync()` 사용 |

---

## COMMANDS

```bash
# 빌드
dotnet build FlashHSI.sln

# 테스트
dotnet test FlashHSI.Tests/FlashHSI.Tests.csproj

# 성능 게이트 (반드시 Release 빌드)
dotnet test FlashHSI.Tests/FlashHSI.Tests.csproj -c Release

# 실행 (Windows WPF)
dotnet run --project FlashHSI.UI/FlashHSI.UI.csproj
```

---

## RUNTIME GOTCHAS

- **GC 모드**: `App.xaml.cs`에서 `GCSettings.LatencyMode = SustainedLowLatency` 강제. Gen2 GC 억제 목적.
- **Process Priority**: 앱 시작 시 `ProcessPriorityClass.High` 상향.
- **Simulation Thread**: `HsiEngine.StartSimulation()` → `ThreadPriority.AboveNormal` 별도 스레드.
- **Live Mode**: `PleoraCameraService` 콜백 → `HsiEngine.ProcessCameraFrame()` 직접 호출. 별도 스레드 없음.
- **Native DLLs**: `FlashHSI.Core/libs/PvDotNet.dll`, `PvGUIDotNet.dll` — Pleora 카메라 드라이버. **Windows 전용.**
- **No Program.cs**: WPF 앱 진입점은 `App.xaml.cs`의 `OnStartup()`.
- **ArrayPool 반환**: `FrameProcessed` 이벤트로 전달된 `vizRow`/`contourData`는 **UI 측** `Dispatcher.InvokeAsync` 완료 후 반환.
- **LinearClassifier.GlobalLog**: 정적 이벤트. Dispose 시 반드시 구독 해제.
- **EtherCAT.NET**: alpha 버전 (`1.0.0-alpha.9.final`) — 안정성 유의. 업그레이드 시 충분한 검증 필요.
- **램프 종료**: 앱 종료 시 램프 온도 > 0이면 냉각 대기 루프(`DispatcherTimer`) 진입 후 완전 종료.

---

## COMMANDS (AI 개발자 전용)

OpenCode TUI에서 `/` 를 입력하면 사용할 수 있는 커스텀 커맨드. 파일은 `.opencode/commands/` 에 위치.

| 커맨드 | 설명 | 파일 |
|--------|------|------|
| `/commit` | 변경 파일을 논리 그룹으로 분리하여 컨벤션에 맞게 커밋 | `.opencode/commands/commit.md` |
| `/daily-report [날짜]` | git 커밋 내역 분석 → 마크다운 일일 보고서 생성 | `.opencode/commands/daily-report.md` |
