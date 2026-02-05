# 배경 처리 설계 원칙 (Background Masking Design)

## 🎯 핵심 원칙
> **배경 마스킹은 반드시 Raw 데이터 단계에서 수행한다.**
> 반사율(Reflectance) 또는 흡광도(Absorbance) 변환 **이전**에 처리해야 한다.

---

## 📐 왜 Raw 단계에서 처리해야 하는가?

### 1. 성능 (Performance)
```
[현재 방식 - 최적]
Raw → 배경 체크 → [배경이면 SKIP] → Log 변환 → 분류
       ↑ 빠른 비교 연산       ↑ 유효 픽셀만 변환

[비효율적 방식]
Raw → Log 변환 → 배경 체크 → 분류
       ↑ 모든 픽셀에 Log 계산 (느림)
```

**수치 예시:**
- 640px × 700 FPS = **448,000 픽셀/초**
- Log 연산: ~20 CPU cycles vs 비교 연산: ~1 cycle
- 배경 비율 50% 가정 시, Log 연산 **50% 절약**

### 2. 학습-추론 일관성 (Consistency)
- **오프라인 학습 (Python)**: Raw 기준으로 배경 필터링
- **온라인 추론 (C#)**: 동일하게 Raw 기준으로 배경 필터링
- 동일한 픽셀 집합으로 학습/추론 → **정확도 보장**

---

## 📋 구현 가이드

### model.json 설정
```json
{
  "Preprocessing": {
    "Mode": "Absorbance",           // 또는 "Raw", "Reflectance"
    "MaskRules": "b80 > 35000",     // Raw 값 기준 규칙
    "Threshold": "35000.0"
  }
}
```

| 필드 | 설명 |
|------|------|
| `Mode` | Feature 추출 방식 (Absorbance = Log 변환) |
| `MaskRules` | **Raw 값 기준** 배경 마스킹 규칙 |

### 처리 순서
```
1. Raw 데이터 수신
2. MaskRules 평가 (Raw 값 기준)
   - 조건 불충족 → 배경으로 분류, SKIP
   - 조건 충족 → 유효 픽셀
3. Mode에 따라 Feature 추출
   - "Raw": Gap Difference
   - "Absorbance": Log(Gap/Target)
4. 분류기 호출
```

---

## ⚠️ 주의사항

1. **MaskRules는 항상 Raw 값 기준**
   - 잘못된 예: `b80 > 0.5` (반사율 기준) ❌
   - 올바른 예: `b80 > 35000` (Raw DN 값) ✅

2. **Calibration(White/Dark) 적용 시점**
   - 배경 마스킹: Raw 값 (Calibration 전)
   - Feature 추출: Calibration 적용 후

---

*이 문서는 Python 오프라인 학습과 C# 런타임 추론 간의 일관성을 보장하기 위한 설계 원칙입니다.*
