# FlashHSI.Tests — 테스트 프로젝트

**역할**: xUnit 유닛 테스트 + 성능 게이트. UI 없음, 순수 Core 로직 검증.

---

## STRUCTURE

```
FlashHSI.Tests/
├── ClassifierTests.cs    # LinearClassifier.Predict() 정확도 검증
├── PipelineTests.cs      # HsiPipeline 전처리 파이프라인 검증
├── BenchmarkTests.cs     # 성능 게이트 — avgTime < 1.4ms 단언
├── MaskRuleTests.cs      # MaskRuleParser + unsafe ushort* 평가 검증
└── FlashHSI.Tests.csproj
```

---

## WHERE TO LOOK

| Task | 위치 |
|------|------|
| 분류 로직 정확도 테스트 추가 | `ClassifierTests.cs` |
| 전처리 파이프라인 테스트 추가 | `PipelineTests.cs` |
| 마스킹 규칙 파서 테스트 추가 | `MaskRuleTests.cs` |
| 성능 게이트 임계값 변경 | `BenchmarkTests.cs` → `Assert.True(avgTimeMs < 1.4, ...)` |

---

## 테스트 규칙

- **`[Fact]` 전용** — `[Theory]`/`[InlineData]` 사용 금지.
- **`unsafe` 허용** — `AllowUnsafeBlocks=true`. `fixed(double* p = ...)` 패턴 자유롭게 사용.
- **비관리 메모리**: `Marshal.AllocHGlobal` + `fixed` 사용 시 반드시 `try/finally { FreeHGlobal() }`.
- **네이밍**: `MethodName_Condition_ExpectedBehavior` (예: `Predict_Identifies_Class_Correctly`).
- **커버리지**: `coverlet.collector` 포함. `dotnet test --collect:"XPlat Code Coverage"` 로 수집.

---

## 성능 게이트 (`BenchmarkTests.cs`)

- xUnit `[Fact]` 내부에서 `Stopwatch`로 100,000회 반복 측정.
- 워밍업 100회 선행 필수 (JIT 컴파일 효과 제거).
- **`Assert.True(avgTimeMs < 1.4)`** — 이 단언이 깨지면 최적화 필요.
- **반드시 `-c Release`로 실행**: `dotnet test FlashHSI.Tests/FlashHSI.Tests.csproj -c Release`
  - Debug 빌드는 JIT 최적화 비활성화로 허위 실패 발생.

---

## ANTI-PATTERNS

| 금지 | 이유 |
|------|------|
| Debug 빌드로 성능 테스트 실행 | JIT 최적화 미적용 → 허위 실패 |
| `Marshal.AllocHGlobal` 후 `FreeHGlobal` 누락 | 비관리 메모리 누수 |
| 성능 게이트 `Assert` 삭제/완화 | 퇴행 감지 불가 |
| Hot Path 수정 후 `BenchmarkTests` 미실행 | 성능 퇴행 미검출 |
