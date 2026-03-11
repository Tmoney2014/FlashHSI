---
description: 변경 파일을 논리적 단위로 묶어 FlashHSI 컨벤션에 맞게 커밋
---

FlashHSI Git 커밋 컨벤션에 따라 변경사항을 스마트하게 커밋한다.

## 핵심 규칙 (절대 위반 금지)
- **이 커맨드 자체가 명시적 요청이므로 커밋 진행**
- **커밋 이후 Push는 절대 하지 않는다**
- **수정 개연성이 있는 파일들끼리만 묶어서 따로따로 커밋한다** (논리적 단위 분리)
- `--no-verify`, `--force` 등 훅 우회 옵션 절대 사용 금지

---

## Workflow

### Step 1: 현재 상태 파악 (병렬 실행)

```bash
git status
git diff --stat
git diff --cached --stat
git log --oneline -5
```

### Step 2: 변경 파일 논리적 그룹핑

파일들을 아래 기준으로 그룹화:

| 그룹 기준 | 예시 |
|-----------|------|
| 같은 기능/모듈 변경 | `HsiEngine.cs` + `HsiPipeline.cs` → 하나의 커밋 |
| 독립적인 버그 수정 | 각각 별도 커밋 |
| 문서/설정만 변경 | `docs/` 변경 → 별도 `docs:` 커밋 |
| 테스트만 변경 | `FlashHSI.Tests/` → 별도 `test:` 커밋 |
| UI와 Core 혼재 | 원칙적으로 분리 (논리적 연관성 없으면 반드시 분리) |

**판단 기준**: "이 변경들이 같은 이유로 수정되었는가?" → YES면 같이, NO면 분리

### Step 3: 커밋 메시지 작성

**형식**: `<type>: <subject>` (한글, 50자 이내, 명령형)

**Type 목록**:
| Type | 사용 시점 |
|------|-----------|
| `feat` | 새 기능 추가 |
| `fix` | 버그 수정 |
| `docs` | 문서 변경 (코드 변경 없음) |
| `style` | 포맷팅, 세미콜론 등 (로직 변경 없음) |
| `refactor` | 리팩토링 (기능 변경 없음) |
| `perf` | 성능 개선 |
| `test` | 테스트 코드 추가/수정 |
| `chore` | 빌드, 설정, 기타 작업 |

**예시**:
```
feat: 멀티 밴드 분석 MROI UI 추가
fix: 레이스 컨디션으로 인한 vizRow 데이터 손상 수정
perf: LinearClassifier 내적 연산 SIMD 적용
refactor: HsiPipeline 전처리 단계 분리
docs: 배경 마스킹 설계 문서 업데이트
test: LinearClassifier LDA 예측 정확도 테스트 추가
chore: .gitignore Params.txt 제외 추가
```

### Step 4: 그룹별 순차 커밋

각 그룹에 대해:
```bash
git add <관련 파일들>
git commit -m "<type>: <subject>"
```

본문이 필요한 경우 (변경이 복잡할 때):
```bash
git commit -m "<type>: <subject>" -m "<한국어 본문 설명>"
```

### Step 5: 결과 보고

커밋 완료 후 요약 출력:

```
✅ 커밋 완료

[커밋 1] abc1234 feat: ...
[커밋 2] def5678 fix: ...
...

총 N개 커밋 | Push는 하지 않았습니다.
```

---

## $ARGUMENTS 처리

**$ARGUMENTS가 있으면**: 해당 내용을 커밋 메시지로 사용하되, 타입이 없으면 자동 추론. 모든 변경을 단일 커밋으로.  
**$ARGUMENTS가 없으면**: Step 1~5 전체 플로우 실행.

---

## Anti-Patterns (절대 금지)

| 금지 | 이유 |
|------|------|
| `git add .` 후 단일 커밋 | 논리적 단위 분리 원칙 위반 |
| `git push` 실행 | 이 커맨드는 커밋만 한다 |
| `--no-verify` | 훅 우회 금지 |
| 영어 커밋 메시지 | 이 프로젝트는 한국어 메시지 사용 |
| `chore:` 남용 | 정확한 type 분류 필수 |
