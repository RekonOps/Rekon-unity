using UnityEngine;

#pragma warning disable CS0618 // Obsolete 경고 억제 (JiraAttachmentUploader 하위 호환성)

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// Play Mode 진입 시 BugOneTouch 시스템 전체를 자동 초기화하는 부트스트랩 클래스.
    ///
    /// 초기화 순서:
    ///   1. BugOneTouchSettings 로드 (없으면 경고 후 중단)
    ///   2. DontDestroyOnLoad GameObject 생성
    ///   3. 의존성 객체 생성 (LogRingBuffer, LogSerializer, ScreenshotCapturer 등)
    ///   4. 영상 녹화 활성화 시 FrameRingBuffer, VideoEncoder, FrameCapturer 초기화
    ///   5. CaptureOrchestrator 생성 및 모든 의존성 주입
    ///   6. HotkeyManager MonoBehaviour 생성 및 설정 주입
    ///   7. HotkeyManager ↔ CaptureOrchestrator 바인딩
    ///   8. CaptureOverlay 초기화 및 오케스트레이터 바인딩
    /// </summary>
    public static class BugOneTouchBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            // ── 1. Settings 로드 ───────────────────────────────────────────────────
            BugOneTouchSettings settings = BugOneTouchSettingsProvider.Settings;

            if (settings == null)
            {
                Debug.LogWarning("[BugOneTouch] BugOneTouchSettings를 찾을 수 없습니다. " +
                                 "Resources/BugOneTouchSettings.asset을 생성하세요. 시스템 초기화를 건너뜁니다.");
                return;
            }

            try
            {
                // ── 2. 루트 GameObject 생성 (씬 전환 후에도 유지) ─────────────────
                var root = new GameObject("[BugOneTouch]");
                Object.DontDestroyOnLoad(root);

                Debug.Log("[BugOneTouch] 부트스트랩 시작...");

                // ── 3. 공통 의존성 생성 ───────────────────────────────────────────

                // 로그 링버퍼: Application.logMessageReceivedThreaded 구독 시작
                var logRingBuffer = new LogRingBuffer(settings.logBufferSize);

                // 로그 직렬화기: 마스킹 활성화
                var logSerializer = new LogSerializer(enableMasking: true);

                // 스크린샷 캡처기: settings 주입
                var screenshotCapturer = new ScreenshotCapturer(settings);

                // 커스텀 컨텍스트 레지스트리: 생성자 파라미터 없음
                var contextRegistry = new ContextProviderRegistry();

                // 상태 스냅샷 수집기: contextRegistry 주입
                var stateCollector = new StateSnapshotCollector(contextRegistry);

                // ── 4. 영상 녹화 관련 컴포넌트 (videoEnabled 시에만) ───────────────
                FrameRingBuffer frameRingBuffer = null;
                IVideoEncoder videoEncoder = null;
                VideoEncoderConfig videoConfig = null;
                FrameCapturer frameCapturer = null;

                if (settings.videoEnabled)
                {
                    int frameCapacity = settings.videoFps * settings.videoBufferSeconds;
                    frameRingBuffer = new FrameRingBuffer(frameCapacity);

                    // FFmpeg 설치 여부에 따라 인코더 선택
#if UNITY_STANDALONE || UNITY_EDITOR
                    // 최초 호출 시 최대 3초 소요 (FFmpeg 프로세스 실행 및 응답 대기)
                    // 이후 호출은 캐시된 결과를 즉시 반환합니다.
                    if (FfmpegHelper.IsInstalled())
                    {
                        videoEncoder = new Mp4VideoEncoder();
                        Debug.Log("[BugOneTouch] MP4 인코더 활성화 (FFmpeg 감지됨)");
                    }
                    else
#endif
                    {
                        videoEncoder = new VideoEncoder();
                        Debug.Log("[BugOneTouch] raw 프레임 인코더 사용");
                    }
                    videoConfig = VideoEncoderConfig.FromSettings(settings);

                    // FrameCapturer는 MonoBehaviour이므로 root에 AddComponent
                    frameCapturer = root.AddComponent<FrameCapturer>();
                    frameCapturer.Initialize(frameRingBuffer, videoConfig);
                    frameCapturer.StartCapturing();

                    Debug.Log($"[BugOneTouch] 영상 녹화 활성화: {videoConfig}");
                }

                // ── 5. CaptureOrchestrator 생성 ───────────────────────────────────
                var orchestrator = new CaptureOrchestrator(
                    screenshotCapturer: screenshotCapturer,
                    logCollector: logRingBuffer,
                    logSerializer: logSerializer,
                    stateCollector: stateCollector,
                    frameBuffer: frameRingBuffer,    // null 허용 (영상 비활성 시)
                    videoEncoder: videoEncoder,       // null 허용
                    videoConfig: videoConfig,         // null 허용
                    settings: settings);

                // ── 6. HotkeyManager 생성 및 설정 주입 ───────────────────────────
                var hotkeyManager = root.AddComponent<HotkeyManager>();
                hotkeyManager.SetSettings(settings);

                // ── 7. HotkeyManager ↔ CaptureOrchestrator 바인딩 ────────────────
                orchestrator.BindHotkeyManager(hotkeyManager);

                // ── 8. CaptureOverlay 초기화 ──────────────────────────────────────
                var overlay = CaptureOverlay.EnsureInstance();
                overlay.BindOrchestrator(orchestrator);

                // ── 9. BugReportForm 생성 및 의존성 주입 ──────────────────────────
                // Auth 관련 의존성
                var tokenStore      = new SessionTokenStore();
                var reAuthHandler   = new ReAuthHandler(tokenStore);
                var brokerClient    = new AuthBrokerClient(settings.authBrokerUrl, tokenStore);
                var tokenManager    = new TokenRefreshManager(brokerClient, tokenStore, reAuthHandler);

                // Jira API 클라이언트
                var jiraApiClient   = new JiraApiClient(tokenManager);

                // Jira 서비스
                var issueCreator        = new JiraIssueCreator(jiraApiClient, settings);
                var attachmentUploader  = new JiraAttachmentUploader(jiraApiClient);
                var jiraService         = new JiraSubmissionService(issueCreator, attachmentUploader);

                // 번들 관련
                var manifestGenerator   = new ManifestGenerator(settings);
                var bundleWriter        = new BundleWriter(manifestGenerator);
                var bundleRepository    = new BundleRepository();

                // ── 10. Supabase / 웹 저장 / 라이선스 의존성 (설정된 경우만) ──────────
                SupabaseAuthClient supabaseAuthClient = null;
                ReportSubmitter reportSubmitter = null;
                LicenseValidator licenseValidator = null;

                if (!string.IsNullOrEmpty(settings.supabaseUrl) && !string.IsNullOrEmpty(settings.supabaseAnonKey))
                {
                    supabaseAuthClient = new SupabaseAuthClient(settings.supabaseUrl, settings.supabaseAnonKey, tokenStore);

                    var r2Uploader = new R2Uploader();
                    reportSubmitter = new ReportSubmitter(settings.supabaseUrl, settings.supabaseAnonKey, tokenStore, r2Uploader);

                    if (!string.IsNullOrEmpty(settings.licenseKey))
                    {
                        licenseValidator = new LicenseValidator(settings.supabaseUrl, settings.supabaseAnonKey, tokenStore);
                        Debug.Log("[BugOneTouch] 라이선스 검증기 초기화 완료");
                    }

                    Debug.Log("[BugOneTouch] Supabase 연동 초기화 완료");
                }

                // BugReportForm 생성 및 의존성 주입
                var bugReportForm = BugReportForm.EnsureInstance();
                bugReportForm.SetDependencies(
                    jiraService, bundleWriter, bundleRepository, settings,
                    reportSubmitter, licenseValidator, supabaseAuthClient);

                // ── 11. 캡처 완료 이벤트 → BugReportForm 바인딩 ─────────────────
                orchestrator.OnCaptureCompleted += bugReportForm.ShowForm;

                Debug.Log("[BugOneTouch] 부트스트랩 완료. 핫키 시스템과 캡처 파이프라인이 활성화되었습니다.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 부트스트랩 초기화 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
