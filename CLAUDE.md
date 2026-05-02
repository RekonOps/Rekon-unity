# Rekon Unity 플러그인

## 🔴 브랜치 전략 (절대 규칙)

**main 브랜치에 직접 push/commit 절대 금지!**

| 브랜치 | 역할 | 규칙 |
|--------|------|------|
| `main` | 릴리스 전용 | PR only, 직접 push 금지 |
| `develop` | 개발 통합 (default) | feature/* 머지 대상 |
| `feature/*` | 기능 개발 | develop에서 분기 → develop에 머지 |

### 작업 흐름
```
feature/* → develop (자유 머지) → main (PR only, 리뷰 후) → v태그 (배포)
```

### 금지 사항
- `git push origin main` ❌
- `git checkout main && git commit` ❌
- main 브랜치에서 직접 파일 수정 ❌

---

## 프로젝트 컨텍스트
이 프로젝트의 전체 컨텍스트는 `../.context/` 에 있습니다.
작업 시작 전 반드시 `../.context/CLAUDE.md`를 읽으세요.

## 이 repo 전용 규칙
- Unity 2021.3+ (최소 지원)
- 네임스페이스: `RekonOps.Rekon`
- 로그 접두사: `[Rekon]`
- 비동기: `async/await` + `Task` (UniTask 사용 X)
- ScriptableObject 기반 설정 (RekonSettings)
- UPM 패키지 형식 (`package.json` + Samples~)
- 주석/로그: 한글
- IL2CPP Dev Build 호환성 보장

## 세션 종료 규칙
작업 완료 시 아래 해당 사항이 있으면 `../.context/` 업데이트:
- 아키텍처 변경 → architecture.md
- 새로운 의사결정 → decisions.md
- 패키지 버전 변경 → CHANGELOG (자체)

---

## Agent skills

도메인 글로서리, 이슈 트래커, triage 라벨 컨벤션은 `Rekon-Context` repo 의 마스터 설정을 참조합니다.

- **Issue tracker**: `gh issue create -R RekonOps/Rekon-unity ...` — 자세한 내용 `../.context/docs/agents/issue-tracker.md`
- **Triage labels**: 5 canonical role + P0~P3 + Area 라벨 — 자세한 내용 `../.context/docs/agents/triage-labels.md`
- **Domain docs**: 본 repo 도메인 정보는 [`CONTEXT.md`](./CONTEXT.md) 시작점, 마스터는 `../.context/CONTEXT-MAP.md`
- **ADR**: `../.context/decisions.md` 단일 누적 (본 repo 에 `docs/adr/` 사용 안 함)
