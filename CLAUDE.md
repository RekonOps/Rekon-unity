# Bug-OneTouch Unity 플러그인

## 프로젝트 컨텍스트
이 프로젝트의 전체 컨텍스트는 `../.context/` 에 있습니다.
작업 시작 전 반드시 `../.context/CLAUDE.md`를 읽으세요.

## 이 repo 전용 규칙
- 언어: C# (Unity 2021.3+)
- 네임스페이스: `GaoZombie.BugOneTouch`
- 로그 접두사: `[BugOneTouch]`
- 설정: ScriptableObject (`BugOneTouchSettings`)
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
