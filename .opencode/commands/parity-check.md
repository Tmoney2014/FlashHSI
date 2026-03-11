---
description: C# FlashHSI 전처리 ↔ Python HSI_ML_Analyzer 패리티 검증
agent: build
subtask: true
---

C# FlashHSI 런타임의 전처리/추론 로직이 Python `HSI_ML_Analyzer`의 학습 로직과 수학적으로 동일한 결과를 내는지 검증합니다.

## 배경

FlashHSI는 두 파트 시스템의 **Part 2 런타임**입니다:
- **Part 1 (Python HSI_ML_Analyzer)**: 오프라인 학습기 → `model.json` 출력  
  경로: `C:\Users\user16g\Desktop\HSI_ML_Analyzer`
- **Part 2 (이 레포 FlashHSI)**: >200 FPS 실시간 런타임 — `model.json` 소비

패리티가 깨지면 Python에서 학습된 모델이 FlashHSI에서 다른 분류 결과를 냅니다.

## Python 학습기 구현 위치 (HSI_ML_Analyzer)

| C# 구현 | Python 대응 파일 | 함수 |
|---------|----------------|------|
| `RawGapFeatureExtractor.Extract()` | `Python_Analysis/models/processing.py` | `apply_simple_derivative()` |
| `LogGapFeatureExtractor.Extract()` | `Python_Analysis/models/processing.py` | `apply_absorbance()` + `apply_simple_derivative()` |
| `SnvProcessor.Process()` | `Python_Analysis/models/processing.py` | `apply_snv()` |
| `MinMaxProcessor.Process()` | `Python_Analysis/models/processing.py` | `apply_minmax_norm()` |
| `L2NormalizeProcessor.Process()` | `Python_Analysis/models/processing.py` | `apply_l2_norm()` |
| `HsiPipeline.ProcessFrame()` (순서) | `Python_Analysis/services/processing_service.py` | `process_cube()` |
| `LinearClassifier.Predict()` | `Python_Analysis/services/learning_service.py` | `export_model()` |

## 검증 대상 및 알려진 이슈

### 1. `RawGapFeatureExtractor` ↔ `apply_simple_derivative`

**C# 공식** (`Preprocessing/RawGapFeatureExtractor.cs` L74-75):
```csharp
output[i] = valGap - valTarget;   // Band[i+gapShift] - Band[i]
```

**Python 공식** (`models/processing.py` L151-167):
```python
B = data[:, gap:]    # Band[i+gap]
A = data[:, :-gap]   # Band[i]
result = B - A       # Band[i+gap] - Band[i]
```

**검증 포인트:**
- 방향성: C# `valGap - valTarget` = Python `B - A` ✔
- **C# Clamp 이슈**: `gapIdx = Math.Min(tIdx + gapShift, rawBandCount - 1)` — 범위 초과 시 마지막 밴드 재사용
- Python은 슬라이싱으로 범위 초과 케이스가 발생하지 않음
- `RequiredRawBands`가 올바르게 설정되면 Clamp가 실제로 발동되지 않아야 함 → `model.json`의 `RequiredRawBands` 검증 필요

### 2. `LogGapFeatureExtractor` ↔ `apply_absorbance` + `apply_simple_derivative`

**C# 공식** (`Preprocessing/LogGapFeatureExtractor.cs` L81-82):
```csharp
// Forward Difference: Log(Gap/Target)
output[i] = Math.Log10((valGap + Epsilon) / (valTarget + Epsilon));
// = log10(B/A)   (B = gap band, A = target band)
```

**Python 학습 파이프라인** (`services/processing_service.py`의 `process_cube()` 기준):
1. `apply_absorbance(data)`: `result = -log10(max(R, ε))`
2. `apply_simple_derivative(absorbance_data, gap)`: `Band[i+gap] - Band[i]`
3. 합산: `(-log10(B)) - (-log10(A))` = `log10(A) - log10(B)` = `log10(A/B)`

**⚠️ 부호 불일치 위험:**
- Python 결과: `log10(A/B)` (A = target, B = gap)
- C# 결과: `log10(B/A)` (= `log10(valGap/valTarget)`)
- **수학적으로 부호가 반대**: `log10(A/B) = -log10(B/A)`
- **학습-추론 대칭성 분석**:
  - Python으로 학습 시 특징값이 `log10(A/B)` 기준으로 최적화됨
  - C# 런타임은 `log10(B/A)` = 부호 반전된 특징값 사용
  - LDA 가중치 기준에서: `w · x_python = -(w · x_csharp)`
  - 따라서 **C# 분류 결과가 반전될 수 있음** (또는 학습 시 부호가 반전된 채로 이미 최적화되었는지 확인 필요)

### 3. `SnvProcessor` ↔ `apply_snv`

**C# 공식** (`Preprocessing/Processors.cs` L54-55):
```csharp
// Academic Standard: Sample Standard Deviation (N-1)
double std = Math.Sqrt(sumSqDiff / (length - 1));
```

**Python 공식** (`models/processing.py` L78-79):
```python
std = np.std(data, axis=1, keepdims=True)   # ddof=0 (모표준편차, 분모 N)
```

**⚠️ ddof 불일치:**
- Python: `ddof=0` (분모 N)
- C#: `ddof=1` (분모 N-1)
- **MROI 비호환으로 현재 사용 금지** — 실질 영향 없음. 단, 향후 사용 시 Python을 `np.std(..., ddof=1)` 로 수정 필요.

### 4. `MinMaxProcessor` ↔ `apply_minmax_norm`

**C#** (`Processors.cs` L20-37): per-feature-vector min/max, `if (range > 1e-9)`
**Python** (`processing.py` L103-107): per-pixel min/max, `range_vals[range_vals == 0] = 1e-10`

**✅ 동일**: per-spectrum min-max 정규화. 적용 범위 일치.

### 5. `L2NormalizeProcessor` ↔ `apply_l2_norm`

**C#** (`Processors.cs` L6-17): `sqrt(sum of squares)`, `if (sumSq > 1e-9)`
**Python** (`processing.py` L98-100): `np.linalg.norm`, `l2_norms[...] = 1e-10`

**✅ 동일**: L2 Euclidean norm. Zero-guard 임계값 미세 차이 (실질 무관).

### 6. Pipeline 순서 패리티

**C# 파이프라인** (`HsiPipeline.ProcessFrame()` + `LoadModel()`):
```
Raw ushort
  → RawProcessors (SNV? MinMax? — Raw 도메인)
  → Feature Extraction (LogGap or RawGap)
  → FeatureProcessors (L2? — Feature 도메인)
  → Classifier
```

**Python 파이프라인** (`ProcessingService.process_cube()`):
- 순서: SG → (Raw→Ref/Abs 변환) → SimpleDeriv / 3PointDepth → L2 / MinMax / SNV
- `processing_service.py`를 읽어 정확한 순서 확인 필요

**검증 포인트:**
- Absorbance 모드: Python에서 `apply_absorbance` 후 `apply_simple_derivative` 순서 vs C#의 `LogGapFeatureExtractor` 단일 연산
- SNV 위치: C#에서 `RawProcessor`(Feature Extraction 이전) vs Python에서의 순서

## 검증 절차

1. **C# 파일 읽기** (이미 이 레포에 있음):
   - `FlashHSI.Core/Preprocessing/LogGapFeatureExtractor.cs`
   - `FlashHSI.Core/Preprocessing/RawGapFeatureExtractor.cs`
   - `FlashHSI.Core/Preprocessing/Processors.cs`
   - `FlashHSI.Core/Pipelines/HsiPipeline.cs` (`LoadModel()` 분기 집중 확인)
   - `FlashHSI.Core/ModelData.cs` (model.json 스키마)

2. **Python 파일 읽기** (`C:\Users\user16g\Desktop\HSI_ML_Analyzer`):
   - `Python_Analysis/models/processing.py` (전체)
   - `Python_Analysis/services/processing_service.py` (`process_cube()` 순서)
   - `Python_Analysis/services/learning_service.py` (`export_model()` L280~L375)

3. 각 대응 쌍의 수식 대조
4. **LogGap 부호 이슈** 집중 분석:
   - Python `process_cube()`에서 Absorbance 모드 처리 순서 추적
   - C# `HsiPipeline.LoadModel()`의 `Mode.Contains("Absorbance")` 분기 확인
   - 실제 학습-추론 대칭성 판단
5. Pipeline 순서 불일치 감지
6. `apply_snv()` 실제 사용 여부 grep (`ProcessingService`, 워커 파일)

## 출력 형식

```
=== C# 패리티 검증 보고서 (FlashHSI ↔ Python HSI_ML_Analyzer) ===

[RawGapFeatureExtractor ↔ apply_simple_derivative]
- C# 공식: ...
- Python 공식: ...
- 방향 일치: ✅ / ❌
- Clamp 위험: ...
- 상태: ✅ 안전 / ⚠️ 주의 / ❌ 위험

[LogGapFeatureExtractor ↔ apply_absorbance + apply_simple_derivative]
- C# 공식: log10(B/A)
- Python 합산 공식: ...
- 부호 일치 여부: ...
- 학습-추론 대칭성 판단: ...
- 상태: ✅ 안전 / ⚠️ 주의 / ❌ 위험

[SnvProcessor ↔ apply_snv]
- ddof 차이: C# N-1 / Python N
- 프로덕션 실제 사용 여부: ...
- 상태: ✅ 안전 / ⚠️ 주의 / ❌ 위험

[MinMaxProcessor ↔ apply_minmax_norm]
- 상태: ✅ 안전 / ⚠️ 주의 / ❌ 위험

[L2NormalizeProcessor ↔ apply_l2_norm]
- 상태: ✅ 안전 / ⚠️ 주의 / ❌ 위험

[Pipeline 순서 패리티]
- C# 순서: ...
- Python 순서: ...
- 순서 일치: ✅ / ⚠️ / ❌

[model.json RequiredRawBands 정합성]
- Python export 로직: ...
- C# Clamp 발동 가능성: ...
- 상태: ✅ / ⚠️ / ❌

[종합 의견]
- 즉시 수정 필요: ...
- 감시 필요: ...
- 안전 확인됨: ...
```

## 주의사항

- **실제 파일 수정 금지** (읽기 전용 분석)
- Python 코드는 `C:\Users\user16g\Desktop\HSI_ML_Analyzer` 에 위치 — 필요 시 직접 읽기
- LogGap 부호 이슈는 학습-추론 대칭성 관점에서 신중하게 판단 (단순 부호 반전이면 LDA 가중치가 반전 학습되어 실질 패리티 문제 없을 수 있음)
- `apply_snv()` 가 Python `ProcessingService.process_cube()` 또는 워커에서 실제 호출되고 있다면 즉시 경고
