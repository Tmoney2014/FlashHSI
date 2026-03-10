# FlashHSI.UI/ViewModels — MVVM ViewModel 레이어

**역할**: CommunityToolkit.Mvvm 기반 6개 ViewModel. UI 상태 소유, 커맨드 처리, Core 서비스 오케스트레이션.

---

## ViewModel 목록

| ViewModel | 책임 |
|-----------|------|
| `MainViewModel` | DI 허브, 종료 시퀀스, 상태바/Snackbar, 하드웨어 트리거 |
| `HomeViewModel` | 운영 대시보드 — 카메라·하드웨어·피더·분류 퀵 액세스, 모델 카드, 램프 온도 |
| `LiveViewModel` | 라이브 카메라 스트림, `FrameProcessed` 구독, `ArrayPool` vizRow 반환 |
| `SettingViewModel` | 마스킹·레퍼런스·SortClass·카메라 파라미터 설정, `model.json` C# 전용 필드 저장 |
| `StatisticViewModel` | 분류 결과 집계/표시 |
| `LogViewModel` | Serilog 메시지 수신 및 표시 |

---

## 패턴 (CommunityToolkit.Mvvm)

- `ObservableObject` 상속 → `[ObservableProperty]` 소스 젠 (backing field: `_camelCase`, 프로퍼티: `PascalCase`).
- `[RelayCommand]` → `XxxCommand` 자동 생성.
- `partial void OnXxxChanged(T value)` → 프로퍼티 변경 훅 (직접 수정 금지).
- `[ObservableProperty]` 초기값 설정: 생성자에서 `_backingField = value` (프로퍼티 setter가 아닌 필드 직접 할당).

---

## 핵심 흐름

### ArrayPool 반환 (LiveViewModel — 절대 규칙)
```
HsiEngine.FrameProcessed(int[] vizRow, ...) 
  → LiveViewModel.OnFrameProcessed()
    → Dispatcher.InvokeAsync( AddLine(vizRow) ) 
      → finally { ArrayPool<int>.Shared.Return(vizRow) }  ← UI 사용 완료 후 반환
```
**반환을 `finally` 블록에서만** — 예외 시에도 반드시 반환.

### 모델 로드 흐름
```
HomeViewModel.SelectModelCard(card)
  → Settings.RaiseModelLoaded(path)     ← CS0070 우회: 이벤트 직접 Invoke 금지
    → SettingViewModel.ModelLoaded 이벤트
      → MainViewModel.OnModelLoaded()   ← UI 상태 갱신
```

### 설정 변경 → 엔진 적용
```
SettingViewModel.[ObservableProperty] 변경
  → partial void OnXxxChanged()
    → _hsiEngine.SetXxx() / SettingsService.Save()
```

---

## WHERE TO LOOK

| Task | 위치 |
|------|------|
| 분류 시작/정지 | `HomeViewModel.TogglePrediction()` + `LiveViewModel.TogglePrediction()` |
| FrameProcessed 핸들러 | `LiveViewModel.OnFrameProcessed()` |
| 마스킹 설정 저장 | `SettingViewModel.SaveMaskSettingsToModelConfig()` |
| 모델 로드 후 상태 복원 | `SettingViewModel.LoadMaskRuleSettings()` |
| 하드웨어 상태 수신 | `HomeViewModel` → `HardwareStatusMessage` 구독 |
| 앱 종료 시퀀스 | `MainViewModel.WindowClosing()` |
| 램프 온도 모니터링 | `HomeViewModel.UpdateLampIndicator()` |
| 카메라 파라미터 live 적용 | `SettingViewModel.partial void OnCameraXxxChanged()` |

---

## ANTI-PATTERNS

| 금지 | 이유 |
|------|------|
| `vizRow` 반환 전 재사용 | `ArrayPool` 레이스 컨디션 |
| 이벤트 `+=` 중복 구독 | FrameProcessed 이중 처리 |
| View 코드비하인드에 로직 추가 | MVVM 위반 |
| `SettingViewModel`에서 `ModelLoaded` 이벤트 직접 `Invoke` | CS0070 오류 → `RaiseModelLoaded()` 메서드 사용 |
| `Dispatcher.Invoke()` (동기) | UI 데드락 → `InvokeAsync()` 사용 |
| `IsSuppressingCameraChanges` 무시 후 카메라 파라미터 설정 | 무한 루프 위험 |
