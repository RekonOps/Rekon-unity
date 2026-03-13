using UnityEngine;

namespace RekonOps.BugOneTouch
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
    ///   9. SilentSubmitManager 초기화
    ///  10. SubmitToast 초기화 및 SilentSubmitManager 바인딩
    /// </summary>
    public static class BugOneTouchBootstrap
    {
        private static bool _initialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

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

                // CaptureOverlay: Silent 모드 활성화 (SilentSubmitManager가 사용되므로)
                overlay.SetSilentMode(true);

                // ── 9. SilentSubmitManager 초기화 ────────────────────────────────
                var manifestGenerator = new ManifestGenerator(settings);
                var bundleWriter = new BundleWriter(manifestGenerator);

                // ReportSubmitService: 웹 대시보드 연동 시에만 생성
                // Web API 프록시(WEB_DASHBOARD_URL)를 통해 Supabase에 접근하므로
                // supabaseUrl/supabaseAnonKey 설정은 더 이상 필요하지 않음
                ReportSubmitService submitService = null;
                if (settings.isLinked)
                {
                    try
                    {
                        var r2UploadService = new R2UploadService();
                        submitService = new ReportSubmitService(r2UploadService);
                        Debug.Log("[BugOneTouch] ReportSubmitService 초기화 완료 (Web 프록시 모드)");
                    }
                    catch (System.Exception submitEx)
                    {
                        Debug.LogWarning($"[BugOneTouch] ReportSubmitService 초기화 실패 (로컬 저장만 가능): {submitEx.Message}");
                    }
                }

                var tokenStore = new SessionTokenStore();
                var silentSubmitManager = new SilentSubmitManager(settings, bundleWriter, tokenStore, submitService);
                silentSubmitManager.BindOrchestrator(orchestrator);

                // ── 10. PendingUploadManager 초기화 ──────────────────────────────────
                var pendingUploadManager = new PendingUploadManager();

                // SilentSubmitManager에 PendingUploadManager 바인딩
                silentSubmitManager.BindPendingUploadManager(pendingUploadManager);

                int pendingCount = pendingUploadManager.GetPendingCount();
                if (pendingCount > 0)
                {
                    Debug.Log($"[BugOneTouch] 앱 시작 시 pending 번들 {pendingCount}개 감지. 향후 미전송 리포트 UI에서 재전송 가능합니다.");
                }

                // ── 11. SubmitToast 초기화 ──────────────────────────────────────────
                var submitToast = SubmitToast.EnsureInstance();
                submitToast.BindSilentSubmitManager(silentSubmitManager);

                Debug.Log("[BugOneTouch] 부트스트랩 완료. 핫키 시스템, 캡처 파이프라인, Silent Submit, PendingUpload, 토스트 UI가 활성화되었습니다.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 부트스트랩 초기화 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
