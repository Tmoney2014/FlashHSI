# FlashHSI — PROJECT KNOWLEDGE BASE

**Stack**: C# 12.0 · .NET 8 · WPF · MVVM (CommunityToolkit.Mvvm)  
**Purpose**: 산업용 초분광 선별기(Hyperspectral Sorter) 실시간 분류 런타임. Python에서 학습된 선형 모델(`model.json`)을 로드하여 >200 FPS로 픽셀 단위 분류 수행.

---

## STRUCTURE

```
FlashHSI/
├── FlashHSI.Core/        # 순수 연산 라이브러리 (외부 의존성 제로 지향)
├── FlashHSI.UI/          # WPF 앱, DI 등록, MVVM ViewModel
├── FlashHSI.Tests/       # xUnit 유닛 테스트 + BenchmarkDotNet 벤치마크
├── docs/                 # 설계 문서 (마스킹, 추론 파이프라인 스펙)
├── FlashHSI.sln          # Visual Studio 솔루션
├── Gemini.md             # ⭐ AI 개발자 가이드 (규칙/컨벤션 - 반드시 읽을 것)
├── README.md             # 프로젝트 개요
└── global.json           # SDK 버전 고정
```

---

## WHERE TO LOOK

| Task | Location |
|------|----------|
| 분류 알고리즘 (LDA/SVM/PLS) | `FlashHSI.Core/Classifiers/LinearClassifier.cs` |
| 전처리 파이프라인 | `FlashHSI.Core/Pipelines/HsiPipeline.cs` |
| 메인 엔진 (루프/마스킹/블롭) | `FlashHSI.Core/Engine/HsiEngine.cs` |
| 배경 마스킹 로직 | `FlashHSI.Core/Masking/` |
| Feature 추출기 | `FlashHSI.Core/Preprocessing/` (`LogGap*`, `RawGap*`) |
| 하드웨어 인터페이스 | `FlashHSI.Core/Control/Hardware/`, `Control/Serial/` |
| 카메라 서비스 | `FlashHSI.Core/Control/Camera/PleoraCameraService.cs` |
| ENVI 파일 IO | `FlashHSI.Core/IO/EnviReader.cs` |
| DI 등록 / 앱 진입점 | `FlashHSI.UI/App.xaml.cs` |
| ViewModel 허브 | `FlashHSI.UI/ViewModels/MainViewModel.cs` |
| 설정 저장/로드 | `FlashHSI.Core/Settings/SettingsService.cs` |
| model.json 스펙 | `docs/CSHARP_INFERENCE_RUNTIME_SPEC.md` |
| 마스킹 설계 원칙 | `docs/BACKGROUND_MASKING_DESIGN.md` |

---

## ARCHITECTURE

### 레이어 분리

```
FlashHSI.UI  →  FlashHSI.Core  →  libs/ (PvDotNet.dll)
```

- **Core**: UI 의존성 없음. 순수 C# 알고리즘. `unsafe` + pointer 연산 허용.
- **UI**: Core 서비스를 DI로 주입받음. `App.xaml.cs`의 `ConfigureServices()`가 유일한 등록 지점.

### Hot Path (성능 임계 코드)

```
HsiEngine.RunLoop() / ProcessCameraFrame()
  └─ HsiPipeline.ProcessFrame()          ← unsafe stackalloc
       ├─ IFeatureExtractor.Extract()     ← LogGap or RawGap
       └─ IClassifier.Predict()          ← LinearClassifier (AggressiveInlining)
```

### 핵심 인터페이스

| Interface | 구현체 | 역할 |
|-----------|--------|------|
| `IClassifier` | `LinearClassifier` | LDA/SVM/PLS 통합 |
| `IFeatureExtractor` | `LogGapFeatureExtractor`, `RawGapFeatureExtractor` | Feature 추출 |
| `IHsiFrameProcessor` | `SnvProcessor`, `MinMaxProcessor`, `L2NormalizeProcessor` | 전처리 |
| `ICameraService` | `PleoraCameraService` | Pleora 카메라 |
| `IEtherCATService` | `EtherCATService` | 에어건 I/O |

---

## CONVENTIONS (이 프로젝트 전용)

### 언어
- 모든 **주석, 커밋 메시지** → 한국어 ("해요"체)
- 코드 식별자(변수명, 메서드명) → 영어

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
```
feat: Log-Gap-Diff 전처리 로직 구현
fix: 배경 임계값 동적 업데이트 오류 수정
perf: LinearClassifier 내적 연산 SIMD 적용
```
프리픽스: `feat`, `fix`, `docs`, `refactor`, `perf`, `test`

---

## ANTI-PATTERNS (절대 금지)

| 금지 패턴 | 이유 |
|-----------|------|
| Hot Path 내 `new` 객체 생성 | GC 스파이크 → 레이턴시 급증 |
| `new` 대신 `stackalloc` or `ArrayPool.Rent` | Zero-Allocation 원칙 |
| `AI지침: 코드변경 금지` 주석이 있는 코드 수정 | 절대 금지 |
| Hot Path 수정 시 성능 근거 없이 변경 | 벤치마크 또는 예측 근거 필수 |
| `MaskRules`를 반사율(0~1) 기준으로 작성 | Raw DN 값 기준이어야 함 |
| Core 프로젝트에 UI 의존성 추가 | 레이어 오염 |

---

## COMMANDS

```bash
# 빌드
dotnet build FlashHSI.sln

# 테스트
dotnet test FlashHSI.Tests/FlashHSI.Tests.csproj

# 실행 (Windows WPF)
dotnet run --project FlashHSI.UI/FlashHSI.UI.csproj
# 또는 Visual Studio에서 FlashHSI.UI 프로젝트를 시작 프로젝트로 설정 후 F5

# 벤치마크
dotnet run --project FlashHSI.Tests/FlashHSI.Tests.csproj -c Release
```

---

## RUNTIME GOTCHAS

- **GC**: `App.xaml.cs`에서 `SustainedLowLatency` 모드 강제 설정됨. Gen2 GC 억제 목적.
- **Process Priority**: 앱 시작 시 `High` 우선순위로 상향됨.
- **Simulation Thread**: `ThreadPriority.AboveNormal`로 별도 스레드에서 무한 루프.
- **Live Mode**: `PleoraCameraService` 콜백 → `ProcessCameraFrame()` 직접 호출. 별도 스레드 없음.
- **Native DLLs**: `FlashHSI.Core/libs/PvDotNet.dll`, `PvGUIDotNet.dll` — Pleora 카메라 드라이버. Windows 전용.
- **No Program.cs**: WPF 앱이므로 진입점은 `App.xaml.cs`의 `OnStartup()`.
- **ArrayPool Return**: `FrameProcessed` 이벤트로 전달한 `vizRow`/`contourData` 배열은 UI 측에서 반환 (레이스 컨디션 주의).
- **모델 JSON**: Python에서 학습 후 생성. `docs/CSHARP_INFERENCE_RUNTIME_SPEC.md` 참조.
