# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.1.0] - 2026-03-01

### Added

#### M0: 프로젝트 기반 구축
- 초기 프로젝트 구조 및 저장소 설정
- Unity 표준 `.gitignore` 적용
- MIT 라이선스
- README (프로젝트 개요, 기능 목록, 기술 스택, UPM 설치 가이드)
- CONTRIBUTING 가이드 및 PR/Issue 템플릿
- GitHub 브랜치 전략: `main` (안정 버전) + `develop` (통합)

#### M1: UPM 패키지 구조
- `package.json` (UPM 표준 메타데이터)
- `Runtime/`, `Editor/`, `Tests/` 디렉토리 구조
- `BugOneTouch.Runtime.asmdef` / `BugOneTouch.Editor.asmdef` Assembly Definition 설정
- 네임스페이스 `GaoZombie.BugOneTouch` 일관 적용

#### M2: 로컬 번들 저장소
- `BundleWriter` - 버그 리포트 아티팩트를 ZIP 번들로 패키징
- `BundleRepository` - 로컬 번들 저장, 조회, 삭제 관리
- `BundleManifest` / `ManifestGenerator` - SHA-256 기반 무결성 검증
- `BundleRetentionPolicy` - 보관 한도(개수/용량) 초과 시 자동 정리
- `SubmissionQueue` - 제출 대기열 관리 및 재시도 로직
- `SHA256HashUtility` - 해시 유틸리티

#### M3: 캡처 파이프라인
- `CaptureOrchestrator` - 전체 캡처 파이프라인 조율 (병렬 수집, 5초 타임아웃)
- `IScreenshotCapturer` / `ScreenshotCapturer` - 스크린샷 PNG 캡처
- `ILogCollector` - 로그 링 버퍼 수집 인터페이스
- `LogRingBuffer` - 최근 N개 로그 유지 링 버퍼
- `LogSerializer` - 로그를 ZIP으로 직렬화
- `IStateSnapshotCollector` / `StateSnapshotCollector` - 게임 상태 JSON 수집
- `BugOneTouchContext` - 커스텀 K/V 컨텍스트 정적 API
- `IContextProvider` / `ContextProviderRegistry` - 동적 컨텍스트 프로바이더 등록/수집
- `FrameCapturer` / `FrameRingBuffer` / `FramePool` - 영상 프레임 링 버퍼
- `VideoEncoder` / `VideoSegmentWriter` - 프레임을 MP4/세그먼트로 인코딩
- `HotkeyManager` - F12 핫키 이벤트 처리

#### M4: Jira Cloud 연동
- `JiraApiClient` - Jira REST API v3 HTTP 클라이언트
- `JiraIssueCreator` - 이슈 생성 요청 빌더
- `JiraAttachmentUploader` - 스크린샷/로그/영상 첨부 파일 업로드
- `JiraSubmissionService` - 번들 → Jira 이슈 전체 제출 플로우 조율

#### M5: 에디터 UI
- `BugOneTouchSettingsProvider` - Project Settings 패널 통합
- `BugReportForm` - 버그 리포트 작성 폼 (제목, 설명, 심각도, 프로젝트 키)
- `CaptureOverlay` - 캡처 진행 상황 오버레이 UI
- Bundle Manager 에디터 윈도우 - 번들 목록, 상태 확인, 수동 재시도/삭제

#### M6: 크래시 복구
- `AbnormalExitDetector` - 비정상 종료(크래시) 감지 (Memory-Mapped File 기반)
- `MappedFileWriter` - 크래시 안전 데이터 직렬화 (Memory-Mapped File)
- `PeriodicFlushManager` - 주기적 데이터 플러시 관리 (로그/상태/영상)
- `CrashBundleWriter` - 크래시 데이터 번들 생성
- `CrashBundleRetentionPolicy` - 크래시 번들 보관 정책
- `ManagedExceptionHandler` - .NET 처리되지 않은 예외 수집
- 크래시 복구 에디터 윈도우 - 세션 시작 시 자동 감지 및 리포트 안내

#### M7: 보안
- `LogMasker` - 이메일, IPv4, 토큰/시크릿 자동 마스킹 (컴파일된 Regex, 스레드 안전)
- `MaskingRuleLoader` - JSON 파일에서 커스텀 마스킹 규칙 로드
- `TokenEncryptor` - Jira OAuth 토큰 AES-256 암호화 저장
- `ClientRateLimiter` - 클라이언트 측 API 호출 속도 제한

#### M8: OAuth 인증 브로커
- `AuthBrokerClient` - Supabase Edge Functions 기반 Auth Broker HTTP 클라이언트
- `OAuthFlowManager` - Jira OAuth 2.0 PKCE 플로우 관리 (브라우저 오픈 → 콜백 수신)
- `SessionTokenStore` - 암호화된 토큰 영속 저장소
- `TokenRefreshManager` - 액세스 토큰 자동 갱신
- `ReAuthHandler` - 토큰 갱신 실패 시 재인증 안내

#### M9: 릴리즈 준비
- 사용자 가이드 (`docs/user-guide.md`) - Quick Start, 설치, 설정, Jira 연결, 사용법, 트러블슈팅
- API 레퍼런스 (`docs/api-reference.md`) - 전체 공개 API 문서화
- 샘플 프로젝트 (`Samples~/BasicDemo/`) - 커스텀 컨텍스트 및 IContextProvider 구현 예제
- UPM 패키지 검증 스크립트 (`scripts/validate-package.sh`)
- Asset Store 제출 가이드 (`docs/asset-store-submission.md`)
- Auth Broker 프로덕션 배포 가이드 (`docs/deployment/auth-broker-production.md`)

---

## [Unreleased]

_(다음 릴리즈 예정 변경사항)_

---

[0.1.0]: https://github.com/GaoZombie/Bug-OneTouch-unity/releases/tag/v0.1.0
[Unreleased]: https://github.com/GaoZombie/Bug-OneTouch-unity/compare/v0.1.0...HEAD
