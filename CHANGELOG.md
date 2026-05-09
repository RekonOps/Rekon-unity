# 변경 이력

이 문서의 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.0.0/)를 따르며,
[유의적 버전](https://semver.org/lang/ko/) 규칙을 준수합니다.

---

## [Unreleased]

---

## [0.3.0] - 2026-05-09

### 변경

#### `ReportSubmitService` HTTP seam 도입

- `ReportSubmitService` 의 create-report + confirm-upload 호출이 `IRekonHttpClient` 위임 방식으로 전환됨.
- `Runtime/Services/ReportSubmitService.cs` 의 `SendRequestAsync` 145L → 22L (단순 위임)
  - 직접 `UnityWebRequest` 사용 → `_httpClient.PostAsync(...)` 호출
  - `using UnityEngine.Networking` 제거 (코드 의존 0)
  - 비즈니스 로직 (UsageLimitExceededException, AuthBrokerException, 재시도 정책) 100% 보존
- 생성자에 `IRekonHttpClient httpClient = null` 옵셔널 매개변수 추가 — backward compat (외부 caller 0 변경)
  - 기본값: `new UnityHttpClient()` — 실 환경 동작 동일
  - 테스트 환경: 향후 mock 주입 가능 (Test Framework 환경 문제 해결 후 단위 테스트 추가 예정)

### 외부 영향

- 외부 caller (`RekonBootstrap.cs`) 변경 없음 — 게임 빌드 정상 동작
- UPM 패키지 사용자 영향: 0 (내부 구현 리팩토링 + 테스트 가능성 확보)

### 알려진 이슈

- Unity Test Framework 환경에서 `[Test] public async Task` 광역 인식 실패 — 별도 task 로 추적 (Rekon-Context backlog).
  관련 단위 테스트는 환경 문제 해결 후 추가 예정.

---

## [1.0.0] - 2026-03-XX

### 추가

#### Supabase Auth 웹 로그인 (auth-unity-start 폴링 방식)
- 브라우저 기반 Supabase Auth 인증 플로우 (auth-unity-start/status 폴링)
- `SupabaseAuthClient` -- Supabase Auth HTTP 클라이언트
- Unity 에디터 Settings 패널에서 [웹 로그인] 버튼으로 원클릭 인증
- 로그인 완료 시 `workspace_id`를 `Settings.tenantId`에 자동 저장

#### [웹 저장] 원클릭 제출 (JAM.dev 패턴)
- [웹 저장]: 웹 로그인만 되어있으면 항상 사용 가능 (무료/유료)
- Unity 플러그인에서 Jira에 직접 연결하지 않음 -- Jira 이슈 등록은 웹 대시보드에서만 수행
- Web Proxy 방식: 모든 API 호출이 웹 백엔드(Next.js API Routes)를 경유

#### Cloudflare R2 파일 업로드
- 영상(MP4), 스크린샷(PNG), 로그 파일 R2 업로드
- Presigned URL 기반 직접 업로드 (PUT)
- 재시도 로직 (최대 3회, 지수 백오프)
- 업로드 완료 후 `confirm-upload` API 호출로 서버에 완료 알림
- 업로드 실패 시 `pending/` 로컬 저장 (네트워크 복구 시 백그라운드 재시도)

#### 라이선스 검증 시스템
- `LicenseValidator` -- 서버 기반 라이선스 키 검증 (1시간 캐시)
- 오프라인 Grace Period 72시간 (서버 타임스탬프 기준)
- 라이선스 무효 시 로컬 캡처는 허용 (웹 저장만 차단)
- AES 암호화 로컬 캐시 (EditorPrefs)

#### AES-256-CBC 세션 암호화
- `SessionTokenStore` -- OAuth 토큰 및 세션 데이터 암호화 영속 저장
- `TokenEncryptor` -- AES-256 암호화/복호화 유틸리티

### 변경

- **ADR-047 반영**: Unity Jira 직접 연동 제거 -- Web Proxy 방식으로 전환
  - 기존: Unity → Supabase Edge Functions 직접 호출
  - 변경: Unity → 웹 백엔드(Next.js API Routes) → Supabase Edge Functions
- Jira 이슈 등록 위치 변경: Unity 플러그인 → 웹 대시보드 (`/workspace/[id]`)
- `JiraIssueCreator`: Unity 플러그인에서 직접 호출하지 않음. 웹 백엔드의 `push-to-jira`에서 유사 로직 사용

### 폐기 예정

- `ReportSubmitter` -- 레거시 리포트 제출 클래스. `ReportSubmitService` (Services/)로 대체됨
- `JiraAttachmentUploader` -- Jira API 직접 업로드 방식. R2 URL 링크 방식(`JiraIssueCreator` + R2 URL)으로 대체됨

### 제거

- Unity에서 Supabase Edge Functions 직접 호출 코드 제거 (Web Proxy 경유로 전환)

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
- Assembly Definition 설정 (`Rekon.Runtime.asmdef` / `Rekon.Editor.asmdef`)
- 네임스페이스 `RekonOps.Rekon` 일관 적용

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
- `JiraIssueCreator` -- 이슈 생성 요청 빌더 (R2 URL 링크 방식)
- `JiraAttachmentUploader` -- 첨부 파일 업로드 (v1.0.0에서 폐기 예정)
- `JiraSubmissionService` -- 번들에서 Jira 이슈 전체 제출 플로우 조율

#### M5: 에디터 UI
- `RekonSettingsProvider` -- Project Settings 패널 통합
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

[Unreleased]: https://github.com/RekonOps/Rekon-unity/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/RekonOps/Rekon-unity/compare/v0.1.0...v1.0.0
[0.1.0]: https://github.com/RekonOps/Rekon-unity/releases/tag/v0.1.0
