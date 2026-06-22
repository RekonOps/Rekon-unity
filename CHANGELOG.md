# 변경 이력

이 문서의 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.0.0/)를 따르며,
[유의적 버전](https://semver.org/lang/ko/) 규칙을 준수합니다.

---

## [Unreleased]

### 변경
- **라이선스 변경**: Business Source License 1.1 → **Apache License 2.0** (OSI 오픈소스). Unity SDK 클라이언트를 OpenUPM 등 공개 레지스트리로 배포 가능하게 함. 백엔드/제품은 별도 라이선스 유지.

---

## [0.5.0] - 2026-06-21

### 추가

- **`Rekon.Capture(title)` 코드 캡처 API**: 코드에서 버그 리포트 캡처(영상+로그+스크린샷)를 발동하는 public 진입점. 캡처 핫키(Ctrl/Cmd+Shift+B)와 동일 경로(`CaptureOrchestrator.StartAsync`)이며 완료 시 Silent Submit 자동 제출. 제목 지정 가능(미지정 시 자동 생성).

### 수정

- **영상-로그 시점 싱크 보정**: 스트리밍 녹화 모드에서 `video_start_t_abs`/`video_duration_s`가 항상 빈 레거시 링버퍼를 읽어 `0`으로 기록돼, 웹 리플레이에서 로그가 영상과 전혀 정렬되지 않던 문제. 인코딩 길이(`FramesWritten/fps`)로 `video_start = capture_trigger − duration` 역산 + `clock_offset = 0`(로그·영상 realtime 단일 축 통일). 트리거 시점 프레임수 스냅샷으로 `Restart()` 리셋 레이스 회피.
- **로그 전량 수집(플레이 진입부터)**: team_pro `ReplayLogCollector`를 부트스트랩 초반에 조기 생성·구독(window 180s 유지, 비-team_pro는 dispose) + `SubsystemRegistration` 이른 tap으로 컬렉터 생성 전(부트스트랩 이전 포함) 구간까지 버퍼링 후 seed. 기존엔 늦은 구독으로 초기 로그가 리포트에서 누락되던 문제 해결. 멱등 구독으로 Domain Reload OFF 누적 방지.
- **웹 연동 해제가 저장되지 않던 IMGUI 버그**: 연동 해제 클릭 시 같은 `OnGUI` 패스 중 `isLinked` 변경이 하위 섹션 컨트롤 수를 Layout↔Repaint 간 불일치시켜 예외가 발생, 저장(`ApplyModifiedProperties`) 직전에 중단되던 문제. 즉시 영속화 + `GUIUtility.ExitGUI()`로 수정.

---

## [0.4.1] - 2026-06-01

### 수정

- **빌드 에러 fix (CS1739)**: 보안 회귀 테스트(`LogMasker`/`TokenEncryptor` property test)의 `new System.Random(seed: N)` → positional 인자로 변경. .NET 생성자 파라미터명은 `Seed`(대문자)라 소문자 named argument 불일치로 컴파일 실패하던 것 복구 (7곳).

### 패키징

- **git URL 설치 시 `.meta` 누락 경고 제거**: `.upmignore` 는 npm registry publish 시에만 적용되고 git URL 설치엔 무시되어, repo 의 작업용 `.md` 가 PackageCache(immutable)로 따라가 "has no meta file" 경고 다수 발생하던 문제 해결.
  - 작업/AI 메타 5종(`CLAUDE`/`CONTEXT`/`findings`/`progress`/`task_plan`) git 추적 제외 (dev 로컬만 유지, prod·UPM 미포함).
  - `SECURITY.md.meta` 를 `.gitignore` whitelist 에 추가 (패키지 배포 문서).
  - 기준 확립: 패키지 사용자가 볼 문서(README/CHANGELOG/LICENSE/SECURITY)만 커밋.

---

## [0.4.0] - 2026-05-23

### 추가

- **team_pro 리플레이 로그 수집** (#230): JSONL + `ReplayMetadata` + `ReplayLogCollector` — 영상↔로그 시간축(`realtimeSinceStartup`) 싱크 재생 기반.
- **스크린샷 `captured_t_abs`** (`realtimeSinceStartupAsDouble`) + `.jsonl` 로그 수집 — team_pro 스크린샷 리포트의 로그 타임라인 시점마커 싱크.
- **웹 연동 배지에 연동된 계정 플랜 표시** (Free / Team / Team Pro).

### 수정

- 스크린샷 `captured_t_abs` 캡처 시각순 정렬 (파일명-시각 매핑 일치).
- 스크린샷 `ReplayLogCollector` 부트스트랩 바인딩 누락 보강 → 2-pane 시점마커 활성.
- `#230` 리플레이 `.cs` 의 `.meta` 누락 보강 (GUID 일관성).

---

## [0.3.0] - 2026-05-20

### 변경

#### 네트워크 계층 추상화 (HTTP seam)

- `ReportSubmitService` 의 create-report + confirm-upload 호출이 `IRekonHttpClient` 위임 방식으로 전환됨.
  - 직접 `UnityWebRequest` 사용 → `_httpClient.PostAsync(...)` 호출, `using UnityEngine.Networking` 제거
  - 비즈니스 로직 (UsageLimitExceededException, AuthBrokerException, 재시도 정책) 100% 보존
- `IRekonHttpClient` / `UnityHttpClient` 신규 — 생성자 `IRekonHttpClient httpClient = null` 옵셔널 주입 (backward compat, 외부 caller 0 변경)
  - 기본값 `new UnityHttpClient()` — 실 환경 동작 동일, 테스트 환경 mock 주입 가능

#### FFmpeg 실행 추상화

- `IFfmpegProcessRunner` / `FfmpegProcessRunner` 신규 — `Mp4VideoEncoder` 의 FFmpeg 프로세스 실행을 seam 으로 분리 (테스트 가능성 확보)

#### 라이선스 검증 단순화 (#145)

- `LicenseValidator` 가 `max_seats` nullable 처리 — team / team_pro 무제한 시트 정책 반영
- `licenseKey` / `userId` 인자 제거 → JWT 기반 인증으로 통일

#### 성능 타임라인 team_pro 전용 (#229)

- `PerformanceTimelineCollector` 가 team_pro 플랜에서만 FPS / 힙·GPU·텍스처 메모리 / 프레임 타이밍 / 씬·TimeScale 이벤트 수집
- free / team 은 timescale / network / scene 만 수집 (실제 적재는 backend create-report 가 team_pro 만 저장)

#### 단축키 (#202)

- 스크린샷 리포트 롱프레스 임계값 2초 → 1초 단축

### 제거

- Obsolete dead code 제거: `ReportSubmitter.cs` (513L), `VideoEncoder.cs` (119L)

### 수정

- 개발 환경 URL 정정 (`rekon.vercel.app` → `rekonops.vercel.app`)

### 외부 영향

- 외부 caller (`RekonBootstrap.cs`) 변경 없음 — 게임 빌드 정상 동작
- UPM 패키지 사용자 영향: 내부 리팩토링 위주. 동작 변경은 (1) 롱프레스 1초, (2) 성능 타임라인 team_pro 전용 2건뿐

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
