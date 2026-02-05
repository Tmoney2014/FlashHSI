# 🌌 HyperSort (FlashHSI) - AI Developer Context

> **Project Goal**: 700 FPS Real-time Hyperspectral Plastic Sorting System (Runtime)
> **Stack**: C# 12.0, .NET 8, WPF (MVVM), Unsafe Optimization

## 1. 🎯 프로젝트 정체성 (Project Identity)
이 프로젝트는 **Part 2. 런타임 (Runtime)** 시스템입니다. Python(Part 1)에서 학습된 모델(`model.json`)을 로드하여, 산업용 초분광 카메라로부터 들어오는 데이터를 **1.4ms 이내**에 처리하고 에어건을 트리거링하는 것이 목표입니다.

*   **속도 최우선**: 가독성보다 퍼포먼스가 중요한 구간(Hot Path)이 존재합니다.
*   **하드웨어 친화적**: 메모리 할당을 0에 가깝게 유지(Zero-Allocation)해야 합니다.

---

## 2. 🏗️ 아키텍처 및 디자인 패턴

### 2.1. Core Layer (`FlashHSI.Core`)
외부 의존성이 없는 순수 연산 라이브러리입니다.
*   **Unsafe Optimization**: `Monitor.Data`, `LinearClassifier` 등 핵심 연산은 `unsafe` 포인터로 처리합니다.
*   **Pipeline Pattern**: `HsiPipeline` 클래스가 전처리 → 분류 과정을 관장합니다.
*   **Interfaces**:
    *   `IClassifier`: `LinearClassifier` (LDA, SVM, PLS 공용)
    *   `IFeatureExtractor`: `LogGapFeatureExtractor` (핵심), `RawGapFeatureExtractor`
    *   `IProcessor`: `SnvProcessor`, `MinMaxProcessor` 등

### 2.2. UI Layer (`FlashHSI.UI`)
WPF 및 CommunityToolkit.Mvvm을 사용한 MVVM 패턴입니다.
*   **Threading Model**:
    *   **UI Thread**: 렌더링 및 사용자 입력 처리.
    *   **Simulation Thread**: `ThreadPriority.AboveNormal`로 동작하는 별도 스레드에서 무한 루프(`while`)로 프레임 처리.
*   **View Logic**: `MainViewModel.cs`가 사실상 컨트롤러 역할을 하며, `IsSimulating` 플래그로 루프를 제어합니다.

---

## 3. ⚡ 핵심 로직 (Core Logic)

### 3.1. 전처리 (Preprocessing) - "Log-Gap-Diff"
조명 변화(Scale)를 상쇄하기 위한 물리적 특성 기반 전처리입니다.
*   **수식**: $Feature = \log_{10}(\frac{Gap + \epsilon}{Target + \epsilon})$
    *   수학적으로 $Log(Gap) - Log(Target)$과 동일하며, 이는 $Abs_{Target} - Abs_{Gap}$과 같습니다.
*   **구현**: `LogGapFeatureExtractor.cs`
*   **Calibration**: White/Dark Reference가 로드된 경우, Raw 데이터에 대해 `(Raw - Dark) / (White - Dark)` 보정을 먼저 수행합니다.

### 3.2. 분류 (Classification)
단일 `LinearClassifier` 클래스가 JSON 설정에 따라 동작을 변경합니다.
*   **Linear Equation**: $Score[c] = (\mathbf{w}_c \cdot \mathbf{x}) + b_c$
*   **LDA Mode**: Softmax 확률 계산 후 `ConfidenceThreshold`보다 크면 해당 클래스, 아니면 Unknown.
*   **SVM/PLS Mode**: ArgMax (가장 높은 점수)를 무조건 선택 (Winner-Takes-All).

### 3.3. 성능 최적화 (Optimization Rules)
*   **Hot Path**: `RunSimulationLoop` 내부 및 `Predict` 메서드.
*   **GC 방지**: 루프 내부에서 `new` 키워드 사용 금지. 미리 할당된 버퍼나 `stackalloc`을 사용.
*   **Unsafe Access**: 배열 인덱싱(`[]`) 대신 포인터 연산(`*ptr++`) 권장.

---

## 4. 📝 규칙 및 컨벤션 (Project Rules)

### 4.1. 🇰🇷 언어
*   모든 주석, 커밋 메시지, 설명은 **한국어**로 작성합니다.
*   전문적이고 명확한 "해요"체를 사용합니다.

### 4.2. 🤖 AI 코드 식별
*   새로운 메서드 작성 시: `/// <ai>AI가 작성함</ai>` summary 태그 필수.
*   기존 코드 수정 시: `// AI가 수정함: [이유]` 주석 필수.

### 4.3. Git Convention
*   `feat`, `fix`, `docs`, `refactor`, `perf` (성능 개선) 등을 사용.
*   예: `feat: Log-Gap-Diff 전처리 로직 구현`

### 4.4. 안전 지침
*   `AI지침: 코드변경 금지` 주석이 있는 코드는 절대 건드리지 마십시오.
*   Hot Path 내부 로직 수정 시 벤치마크 혹은 성능 예측 근거를 제시해야 합니다.
