# 변경 이력

이 문서의 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.0.0/)를 따르며,
[유의적 버전](https://semver.org/lang/ko/) 규칙을 준수합니다.

---

## [Unreleased]

### 추가

#### Supabase Auth 웹 로그인
- 브라우저 기반 Supabase Auth 인증 플로우 (auth-unity-start/status 폴링)
- `SupabaseAuthClient` -- Supabase Auth HTTP 클라이언트
- Unity 에디터 Settings 패널에서 [웹 로그인] 버튼으로 원클릭 인증

#### Cloudflare R2 파일 업로드
- 영상(MP4), 스크린샷(PNG), 로그 파일 R2 업로드
- Signed URL 기반 직접 업로드
- 재시도 로직 (최대 3회, 지수 백오프)
- 업로드 실패 시 로컬 저장 (네트워크 복구 시 백그라운드 재시도)

#### 라이선스 검증 시스템
- `LicenseValidator` -- 서버 기반 라이선스 키 검증 (1시간 캐시)
- 오프라인 Grace Period 72시간 (서버 타임스탬프 기준)
- 라이선스 무효 시 Jira 제출만 차단 (로컬 캡처는 허용)
- AES 암호화 로컬 캐시 (EditorPrefs)

#### [웹 저장] / [Jira 등록] 이중 경로
- [웹 저장]: 웹 로그인만 되어있으면 항상 사용 가능 (무료/유료)
- [Jira 등록]: Jira OAuth 연동 완료 시에만 활성화, 클라이언트 직접 Jira API 호출
- 두 경로 모두 R2 파일 업로드 + Supabase 메타데이터 저장

#### AES-256-CBC 세션 암호화
- `SessionTokenStore` -- OAuth 토큰 및 세션 데이터 암호화 영속 저장
- `TokenEncryptor` -- AES-256 암호화/복호화 유틸리티

---

## [0.1.0] - 2026-03-01

### 추가

#### M0: 프로젝트 기반 구축
- 초기 프로젝트 구조 및 저장소 설정
- Unity 표준 `.gitignore` 적용
- MIT 라이선스
- README, CONTRIBUTING 가이드 및 PR/Issue 템플릿
- GitHub 브랜치 전략: `main` (안정 버전) + `develop` (통합)

#### M1: UPM 패키지 구조
- `package.json` (UPM 표준 메타데이터)
- `Runtime/`, `Editor/`, `Tests/` 디렉토리 구조
- Assembly Definition 설정 (`BugOneTouch.Runtime.asmdef` / `BugOneTouch.Editor.asmdef`)
- 네임스페이스 `GaoZombie.BugOneTouch` 일관 적용

#### M2: 로컬 번들 저장소
- `BundleWriter` -- 버그 리포트 아티팩트를 ZIP 번들로 패키징
- `BundleRepository` -- 로컬 번들 저장, 조회, 삭제 관리
- `BundleManifest` / `ManifestGenerator` -- SHA-256 기반 무결성 검증
- `BundleRetentionPolicy` -- 보관 한도(개수/용량) 초과 시 자동 정리
- `SubmissionQueue` -- 제출 대기열 관리 및 재시도 로직

#### M3: 캡처 파이프라인
- `CaptureOrchestrator` -- 전체 캡처 파이프라인 조율 (병렬 수집, 5초 타임아웃)
- `ScreenshotCapturer` -- 스크린샷 PNG 캡처
- `LogRingBuffer` -- 최근 N개 로그 유지 링 버퍼
- `StateSnapshotCollector` -- 게임 상태 JSON 수집
- `FrameCapturer` / `FrameRingBuffer` / `FramePool` -- 영상 프레임 링 버퍼
- `VideoEncoder` / `VideoSegmentWriter` -- 프레임을 MP4/세그먼트로 인코딩
- `HotkeyManager` -- F12 핫키 이벤트 처리

#### M4: Jira Cloud 연동
- `JiraApiClient` -- Jira REST API v3 HTTP 클라이언트
- `JiraIssueCreator` -- 이슈 생성 요청 빌더
- `JiraAttachmentUploader` -- 첨부 파일 업로드
- `JiraSubmissionService` -- 번들에서 Jira 이슈 전체 제출 플로우 조율

#### M5: 에디터 UI
- `BugOneTouchSettingsProvider` -- Project Settings 패널 통합
- `BugReportForm` -- 버그 리포트 작성 폼
- `CaptureOverlay` -- 캡처 진행 상황 오버레이 UI
- Bundle Manager 에디터 윈도우

#### M6: 크래시 복구
- `AbnormalExitDetector` -- Memory-Mapped File 기반 비정상 종료 감지
- `PeriodicFlushManager` -- 주기적 데이터 플러시 관리
- `CrashBundleWriter` -- 크래시 데이터 번들 생성
- `ManagedExceptionHandler` -- .NET 처리되지 않은 예외 수집

#### M7: 보안
- `LogMasker` -- 이메일, IPv4, 토큰/시크릿 자동 마스킹
- `TokenEncryptor` -- Jira OAuth 토큰 AES-256 암호화 저장
- `ClientRateLimiter` -- 클라이언트 측 API 호출 속도 제한

#### M8: OAuth 인증 브로커
- `AuthBrokerClient` -- Supabase Edge Functions 기반 Auth Broker HTTP 클라이언트
- `OAuthFlowManager` -- Jira OAuth 2.0 PKCE 플로우 관리
- `SessionTokenStore` -- 암호화된 토큰 영속 저장소
- `TokenRefreshManager` -- 액세스 토큰 자동 갱신

#### M9: 릴리즈 준비
- 사용자 가이드, API 레퍼런스 문서화
- 샘플 프로젝트 (`Samples~/BasicDemo/`)
- UPM 패키지 검증 스크립트

---

[Unreleased]: https://github.com/Project-Bug-OneTouch/Bug-OneTouch-unity/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Project-Bug-OneTouch/Bug-OneTouch-unity/releases/tag/v0.1.0
