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

### 커밋/PR 시 체크
- 현재 브랜치가 main이 아닌지 반드시 확인
- main 대상 작업은 반드시 develop에서 PR 생성

---

# Rekon Unity 플러그인

## 프로젝트 컨텍스트
이 프로젝트의 전체 컨텍스트는 `../.context/` 에 있습니다.
작업 시작 전 반드시 `../.context/CLAUDE.md`를 읽으세요.

## 이 repo 전용 규칙
- 언어: C# (Unity 2021.3+)
- 네임스페이스: `RekonOps.Rekon`
- 로그 접두사: `[Rekon]`
- 설정: ScriptableObject (`RekonSettings`)
- 비동기: `async/await` + `Task`
- 영상 인코딩: FFmpeg (PC/Mac 전용)
- 기본 캡처: 15fps, 1280x720
- 주석/로그: 한글

## 세션 종료 규칙
작업 완료 시 아래 해당 사항이 있으면 `../.context/` 업데이트:
- 아키텍처 변경 → architecture.md
- 새로운 의사결정 → decisions.md
- DB 스키마 변경 → schemas.md
- API 변경 → api-spec.md
