# FlashHSI - High Speed Hyperspectral Imaging Sorter ⚡

**FlashHSI**는 산업용 초분광 선별기(Hyperspectral Sorter)를 위한 고성능 실시간 분류 소프트웨어입니다.
C# .NET 8 및 WPF 기반으로 제작되었으며, Python 등으로 학습된 다양한 선형 모델을 로드하여 산업 현장에서 요구하는 빠른 속도(>200 FPS)로 픽셀 단위 분류를 수행합니다.

## 🚀 Key Features

### 1. Multi-Model Support (다중 모델 지원)
단일 처리 엔진(`LinearClassifier`)으로 다양한 선형 분류 모델을 통합 지원합니다. JSON 설정 파일만 교체하면 즉시 모델이 변경됩니다.
*   **LDA (Linear Discriminant Analysis)**: 통계적 확률 기반 분류.
*   **Linear SVM (Support Vector Machine)**: 마진 최적화 기반 분류.
*   **PLS-DA (Partial Least Squares Discriminant Analysis)**: 차원 축소 및 회귀 기반 분류.

### 2. Intelligent Decision Logic (지능형 판단)
모델의 특성에 따라 최적의 판단 로직이 자동으로 적용됩니다.
*   **LDA**: `Softmax` 확률 계산 → **Confidence Threshold(신뢰도 임계값)** 적용 가능.
*   **SVM / PLS-DA**: `ArgMax` (Winner-Takes-All) 방식 적용 → 점수 스케일에 상관없이 가장 유력한 클래스를 **100%** 선택 (Unknown 없음).

### 3. Advanced Preprocessing Pipeline (고급 전처리)
학습 단계와 동일한 수준의 정밀한 전처리 파이프라인을 내장하고 있습니다.
*   **SNV (Standard Normal Variate)**: 표본 표준편차($N-1$) 기준의 정석적 구현.
*   **Min-Max Normalization**: 데이터 스케일 정규화.
*   **L2 Normalization**: 벡터 크기 정규화.
*   **Feature Extraction**:
    *   **Log Ratio**: 흡광도(Absorbance) 모드 (`Log(Target - Gap)`)
    *   **Raw Gap**: 반사율(Reflectance) 모드 (`Target - Gap`)

### 4. Industrial Reliability (산업급 신뢰성)
*   **High Priority Threading**: UI와 분리된 고(High) 우선순위 연산 스레드로 OS 스케줄링 지연 최소화.
*   **Unsafe Optimization**: 포인터 연산(`unsafe`)을 통한 메모리 복사 최소화 및 초고속 연산.
*   **MVVM Architecture**: `CommunityToolkit.Mvvm` 기반의 유지보수 용이한 설계.

## 🛠️ Tech Stack
*   **Framework**: .NET 8 (Windows)
*   **UI**: WPF (Windows Presentation Foundation)
*   **Language**: C# 12.0
*   **Test**: xUnit, BenchmarkDotNet

## 📦 Usage
1.  **Load Model JSON**: Python에서 학습된 모델 설정 파일(`.json`)을 로드합니다.
2.  **Select Data**: 시뮬레이션을 위한 초분광 데이터 헤더(`.hdr`)를 선택합니다.
3.  **Simulation Start**: 분류 시뮬레이션을 시작합니다.
4.  **Control**:
    *   **Confidence Threshold**: LDA 모델 사용 시, 불확실한 픽셀을 걸러내는 강도를 조절합니다.
    *   **Background Threshold**: 배경(Background)으로 처리할 빛의 세기 임계값을 설정합니다.

## 📂 Project Structure
*   **FlashHSI.Core**: 핵심 연산 로직(모델, 전처리, 파일 IO). (외부 의존성 제로)
*   **FlashHSI.UI**: WPF 기반 사용자 인터페이스 및 ViewModel.
*   **FlashHSI.Tests**: 유닛 테스트 및 벤치마크.

---
*Developed for High-Speed Industrial Sorting Applications.*
