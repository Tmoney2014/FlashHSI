# FlashHSI.Core — 연산 라이브러리 지식베이스

**역할**: UI 의존성 없는 순수 연산 레이어. 분류, 전처리, IO, 하드웨어 제어 모두 여기에.

---

## STRUCTURE

```
FlashHSI.Core/
├── Analysis/          # BlobTracker, ActiveBlob — 연속 객체 추적
├── Classifiers/       # LinearClassifier (LDA/SVM/PLS 통합)
├── Control/
│   ├── Camera/        # ICameraService, PleoraCameraService (Pleora SDK)
│   ├── Hardware/      # IEtherCATService, EtherCATService (에어건 I/O)
│   └── Serial/        # SerialCommandService (DIO 보드 제어)
├── Engine/            # HsiEngine — 메인 실행 엔진 (루프/마스킹/블롭)
├── Interfaces/        # IClassifier, IFeatureExtractor, IHsiFrameProcessor
├── IO/                # EnviReader — ENVI HSI 파일 읽기
├── Logging/           # ILogMessageSender, LoggingConfig
├── Masking/           # MaskRule, MaskRuleParser
├── Memory/            # BufferPool — Zero-Allocation 지원
├── Messages/          # CommunityToolkit.Mvvm 메시지 타입들
├── Models/            # ClassInfo, ModelCard, SortClass, Feeder 등
├── Pipelines/         # HsiPipeline — 전처리→분류 파이프라인 오케스트레이터
├── Preprocessing/     # LogGapFeatureExtractor, RawGapFeatureExtractor, Processors
├── Services/          # CaptureService, CommonDataShareService, MemoryMonitoringService
├── Settings/          # SettingsService, SystemSettings, MaskRuleCondition*
├── Utilities/         # PrecisionTimer
├── libs/              # 네이티브 DLL (PvDotNet.dll, PvGUIDotNet.dll) — Pleora 카메라 드라이버
├── ModelData.cs       # ModelConfig 역직렬화 대상 (model.json 스키마)
└── Enums/             # DeviceStatus 등 공용 열거형
```

---

## WHERE TO LOOK

| Task | 파일 |
|------|------|
| 새 분류 알고리즘 추가 | `Interfaces/IClassifier.cs` → `Classifiers/` 아래 구현 |
| 전처리 단계 추가 | `Interfaces/IHsiFrameProcessor.cs` → `Preprocessing/Processors.cs` |
| Feature 추출 방식 변경 | `Preprocessing/LogGapFeatureExtractor.cs` or `RawGapFeatureExtractor.cs` |
| 배경 마스킹 규칙 파싱 | `Masking/MaskRule.cs` — `MaskRuleParser.Parse()` |
| model.json 필드 추가/변경 | `ModelData.cs` + `docs/CSHARP_INFERENCE_RUNTIME_SPEC.md` |
| 블롭 추적 설정 | `Analysis/BlobTracker.cs` — `MinPixels`, `MaxLineGap`, `MaxPixelGap` |
| 에어건 사출 타이밍 | `Control/EjectionService.cs` |
| 카메라 스트림 연결 | `Control/Camera/PleoraCameraService.cs` |
| 설정 JSON 읽기/쓰기 | `Settings/SettingsService.cs` (Singleton) |
| ENVI 파일 읽기 | `IO/EnviReader.cs` |

---

## HOT PATH 규칙 (성능 임계 코드)

```
HsiEngine.RunLoop() / ProcessCameraFrame()
  └─ HsiPipeline.ProcessFrame()     ← unsafe stackalloc (rawBuffer, featureBuffer)
       ├─ IFeatureExtractor.Extract()
       └─ LinearClassifier.Predict() ← [AggressiveInlining], flattened weight array
```

- **추론 시간 목표**: 1 Line (640px) < **1.0ms**. 테스트(`BenchmarkTests.cs`)에서 < 1.4ms 검증.
- Hot Path 내부에서 `new` 절대 금지 → `ArrayPool<T>.Shared.Rent()` 또는 `stackalloc` 사용.
- `vizRow`, `contourData`는 `ArrayPool`에서 Rent → UI에 전달 → **UI 측에서 Return** (엔진에서 반환하면 레이스 컨디션).

---

## 분류 로직

| 모델 타입 | `OriginalType` 포함 문자열 | 판단 방식 |
|----------|--------------------------|---------|
| LDA | (default) | Softmax → `ConfidenceThreshold` 적용 → Unknown 가능 |
| SVM | `"SVM"` or `"SVC"` | ArgMax only (Threshold 무시, Winner-Takes-All) |
| PLS-DA | `"PLS"` | 점수 clamp(0,1) → `ConfidenceThreshold` 적용 |

---

## 마스킹 순서 (필수 준수)

```
Raw 데이터 수신
  → MaskRules 평가 (Raw DN 값 기준)  ← 항상 Raw 단계
  → [배경] classificationRow[x] = -1
  → [객체] HsiPipeline.ProcessFrame()
```

- `MaskRules` 는 **Raw DN 값** 기준 (`b80 > 35000` ✅ / `b80 > 0.5` ❌).
- Calibration(White/Dark)은 Feature 추출 시 적용, 마스킹 단계 이전 아님.

---

## ANTI-PATTERNS

| 금지 | 이유 |
|------|------|
| Hot Path 내 `new` | GC Pause |
| `MaskRules`를 반사율(0~1)로 작성 | Raw DN 기준이어야 함 |
| UI 네임스페이스 참조 | 레이어 오염 |
| `AllowUnsafeBlocks` 해제 | 포인터 연산 전체 중단됨 |
| `stackalloc` 에 밴드 수 2000 초과 | 스택 오버플로우 위험 |
| `PvDotNet.dll` 교체/업데이트 시 테스트 없이 배포 | 카메라 연결 단절 |

---

## NOTES

- `MaskMode` 3종: `Mean` (전체 평균), `BandPixel` (단일 밴드), `MaskRule` (파싱된 규칙).
- `HsiEngine.LoadModel()` 호출 시 `MaskRule` 자동 파싱 및 모드 전환.
- `EtherCAT.NET` 패키지는 alpha 버전(`1.0.0-alpha.9.final`) — 안정성 유의.
- `libs/` 내 DLL은 Windows 전용. 빌드 서버에서 경로 확인 필수.
