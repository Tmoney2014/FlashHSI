# 🧠 C# Inference Runtime Specification for HSI

> **문서 버전**: v1.0  
> **대상 독자**: C# 런타임 개발자  
> **목적**: Python에서 학습된 모델(`model_config.json`)을 C#에서 로드하고, 실시간(Real-time)으로 추론하기 위한 구현 명세 정의

---

## 1. 개요 (Overview)

본 런타임은 High-Speed Sorter에 탑재되며, Python의 `scikit-learn` 기반 로직을 C# 환경에서 **수동으로 재구현**하는 것을 목표로 합니다. Python 런타임에 대한 의존성 없이 독립적으로 동작해야 하며, **Raw Data + Aluminum Background (Inverse Masking)** 전략을 기반으로 한 초고속 추론을 지향합니다.

---

## 2. 모델 파일 (`model_config.json`) 구조

학습된 모델의 설정 파일은 JSON 형식을 따르며, 추론에 필요한 가중치와 전처리 파이프라인 정보를 포함합니다.

### 2.1. JSON Schema 예시

```json
{
  "ModelType": "LinearModel",     // (string) 모델 타입 (Linear SVM 또는 LDA)
  "SelectedBands": [10, 25, ...], // (int[]) 학습에 사용된 Feature 밴드 인덱스 (0-based)
  "RequiredRawBands": [5, 10...], // (int[]) 전처리를 위해 로드해야 할 실제 원본 밴드 목록 (C# 최적화용)
  "Performance": { ... },         // (object) 학습 당시 정확도 메타데이터
  
  // 핵심: 선형 모델 계수 (y = Wx + b)
  // Weights: [Class][Feature] 형태의 2차원 배열
  "Weights": [
    [-0.12, 0.45, ... ], // Class 0의 가중치
    [0.05, -0.91, ... ]  // Class 1의 가중치
  ],
  "Bias": [-1.2, 0.5, ...], // (double[]) 각 클래스별 Bias (Intercept)

  // 핵심: 전처리 파이프라인 설정
  "Preprocessing": {
    "Mode": "Raw",          // "Raw", "Reflectance", "Absorbance"
    "ApplyDeriv": true,     // 미분(Gap Difference) 적용 여부
    "Gap": 5,               // 미분 간격 (중요)
    "DerivOrder": 1,        // 미분 차수 (통상 1)
    "MaskRules": "b80 < 50000", // 배경 제거 규칙 (C# 파싱 필요)
    "Threshold": "0.0"      // (Legacy) MaskRule이 우선함
  },

  "Labels": { "0": "PET", "1": "PP", ... }, // (dict) 클래스 ID -> 이름 매핑
  "Colors": { "0": "#FF0000", ... }         // (dict) 시각화용 색상 코드
}
```

---

## 3. 추론 파이프라인 (Inference Pipeline)

입력된 **HSI Line Data** (Width x Bands)는 아래의 순서대로 처리되어 최종 **Classification Map** (Width)을 생성합니다.

### Step 1: Background Masking (Dynamic Rule)

모델 설정의 `MaskRules` 문자열을 파싱하여 배경과 객체를 분리합니다. 런타임은 `MaskRules`의 연산자를 분석하여 동적으로 로직을 적용해야 합니다.

*   **Rule Format**: `b{BandIndex} {Operator} {Threshold}` (예: `b80 < 50000`)
*   **Parsing Logic**:
    1. `b` 접두어 제거 후 Band Index 파싱
    2. 연산자 (`>`, `<`, `>=`, `<=`) 파싱
    3. Threshold 값 파싱

*   **Logic (Pseudo-code)**:
    ```csharp
    // 예: "b80 < 50000" (알루미늄 배경: 값이 작으면 물체)
    // 예: "b80 > 3000" (검은 배경: 값이 크면 물체)
    
    double val = pixel[bandIdx];
    bool isObject = false;
    
    if (op == ">") isObject = val > threshold;
    else if (op == "<") isObject = val < threshold;
    
    if (isObject) {
        // 객체로 판단 -> Step 2 진행
    } else {
        // 배경으로 판단 -> Class -1 (None) 할당
    }
    ```

### Step 2: Preprocessing (Dynamic Chain)

`Preprocessing` 섹션의 플래그에 따라 전처리를 수행합니다. **연산 순서는 JSON 키 순서가 아닌, 아래 정의된 논리적 순서를 따라야 합니다.**

#### 1. Data Mode Conversion
입력 데이터(Raw DN)를 모드에 맞게 변환합니다.

*   **Mode: "Raw"**
    *   변환 없이 원본 데이터 사용.
*   **Mode: "Reflectance"**
    *   $\text{Reflectance} = \frac{\text{Raw} - \text{Dark}}{\text{White} - \text{Dark}}$
    *   결과값은 0.0 ~ 1.0 범위로 Clipping.
*   **Mode: "Absorbance"**
    *   Reflectance 계산 후 로그 변환 수행.
    *   $\text{Abs} = -\log_{10}(\max(R, 10^{-6}))$

#### 2. Filtering & Normalization
각 플래그(`true`)에 해당하는 연산을 수행합니다.

*   **Min Subtraction (Baseline Correction)** (`ApplyMinSub`)
    *   $x' = x - \min(x)$ (Pixel-wise)
*   **Standard Normal Variate (SNV)** (`ApplySNV`)
    *   $x' = \frac{x - \mu}{\sigma}$ (평균 $\mu$, 표준편차 $\sigma$)
*   **Savitzky-Golay Filter (SG)** (`ApplySG`)
    *   `SGWin` (Window Size), `SGPoly` (Order) 파라미터 사용.
    *   Convolution 연산으로 구현.
*   **Min-Max Normalization** (`ApplyMinMax`)
    *   $x' = \frac{x - \min(x)}{\max(x) - \min(x)}$
*   **L2 Normalization** (`ApplyL2`)
    *   $x' = \frac{x}{\sqrt{\sum x_i^2}}$
*   **Mean Centering** (`ApplyCenter`)
    *   학습 시 데이터 평균을 빼는 연산.
    *   **Runtime 주의**: 단일 픽셀/라인에 대해 수행 시 왜곡 위험. 학습된 Mean Vector가 없다면 **사용하지 않음(False)**을 권장. 필요 시 Line 전체 평균 사용.

#### 3. Feature Extraction (Dimensionality Reduction)

*   **Simple Derivative (Gap Difference)** (`ApplyDeriv`)
    *   핵심 기능.
    *   $D[i] = \text{Band}[i + \text{Gap}] - \text{Band}[i]$
    *   `ApplyDeriv`가 true 이면 반드시 수행.
*   **3-Point Band Depth**
    *   중심점($C$)과 좌우($L, R$)를 이용한 깊이 계산.
    *   $L = \text{Band}[i - \text{Gap}], \quad R = \text{Band}[i + \text{Gap}], \quad C = \text{Band}[i]$
    *   $\text{Baseline} = \frac{L + R}{2}, \quad \text{Depth} = 1 - \frac{C}{\text{Baseline}}$

---

### Step 3: Post-Processing (Real-time Blob Analysis)

Line Scan 카메라 특성에 맞춰, **Line-by-Line 연결성 추적** 알고리즘을 사용합니다.

#### 1. 자료구조 (Active Blob Table)
```csharp
class ActiveBlob {
    public int StartX;      // 객체 시작 X
    public int EndX;        // 객체 끝 X
    public int[] Votes;     // 클래스별 투표 수
    public int TotalPixels; // 전체 픽셀 수
    public int LastSeenLine;// 트래킹용 타임스탬프
}
// List<ActiveBlob> activeBlobs;
```

#### 2. 라인 연결 알고리즘
1.  **Run-Length Encoding (RLE)**: 현재 라인을 `Segment` 단위(클래스 연속 구간)로 변환.
2.  **Overlap Check**: 이전 라인의 `ActiveBlob`과 현재 `Segment`의 X좌표 겹침 여부 확인.
    *   **겹침**: Blob 정보 업데이트 (Votes 누적, LastSeenLine 갱신).
    *   **안 겹침 (New)**: 새로운 `ActiveBlob` 생성.
3.  **Blob Closing**: 이번 라인에서 연결되지 않은(지나간) Blob을 종료 처리.
    *   **Majority Voting**: `Votes` 최다 득표 클래스로 최종 판정.
    *   **Eject**: Ejector 시스템으로 정보 전송.
    *   **Remove**: 리스트에서 제거.

#### 3. 동시 처리 (Concurrency)
*   **독립적 추적**: 여러 물체가 동시에 지나가도 `List<ActiveBlob>`에서 개별 관리.
*   **비동기 판정**: 물체가 끝나는 시점에 즉시 신호 전송 (뒷줄 물체 대기 없음).

---

### Step 4: Ejection Control (Physical Mapping)

판정된 객체를 물리적 에어건 신호로 변환합니다.

#### 1. Channel Mapping
*   `CenterX = (StartX + EndX) / 2`
*   `ChannelID = CenterX / Pixels_Per_Valve`

#### 2. Dynamic Delay Strategy (Hybrid)
*   **Case A: 일반 물체 (Normal)** -> **Center Hit**
    *   물체 꼬리(Tail) 통과 후, 중앙이 에어건에 도달할 때까지 지연.
    *   `Delay = Distance - (Length / 2)`
*   **Case B: 긴 물체 (Long Object)** -> **Head Hit (Early Trigger)**
    *   `Length > Max_Threshold` 인 경우.
    *   꼬리를 기다리지 않고 즉시 발사, 선두 타격.

---

## 4. 구현 시 주의사항 (Critical)

1.  **Zero Allocation**: 픽셀 처리 루프 내에서 `new` 할당 금지. 버퍼 재사용 필수.
2.  **Thread Safety**: 병렬 처리 시 `bestClass` 등 공유 변수 사용 주의. 지역 변수 활용.
3.  **Boundary Check**: `targetBand + Gap` 인덱스 접근 시 범위 초과 여부 검증.
4.  **Inverse Masking**: C# 런타임은 알루미늄 배경(`< Threshold`) 지원이 필수.

## 5. 성능 목표

*   **Target**: 1 Line (640px) 처리 시간 < **1.0 ms**
*   **Optimization Recommendation**:
    *   **Pure Raw Mode**: 전처리 없이 Raw 데이터로 내적 연산 시 최고 속도.
    *   **SIMD**: 내적(Dot Product) 연산 시 `Vector<double>` 등 SIMD 명령어 적극 활용 권장.
