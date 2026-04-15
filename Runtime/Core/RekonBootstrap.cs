using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// Play Mode 진입 시 Rekon 시스템 전체를 자동 초기화하는 부트스트랩 클래스.
    ///
    /// 초기화 순서:
    ///   1. RekonSettings 로드 (없으면 경고 후 중단)
    ///   2. DontDestroyOnLoad GameObject 생성
    ///   3. 의존성 객체 생성 (LogRingBuffer, LogSerializer, ScreenshotCapturer 등)
    ///   4. 영상 녹화 활성화 시 FrameRingBuffer, VideoEncoder, FrameCapturer 초기화
    ///   5. ScreenshotQueue 생성
    ///   6. CaptureOrchestrator 생성 및 모든 의존성 주입
    ///   7. PerformanceTimelineCollector MonoBehaviour 생성 및 CaptureOrchestrator 바인딩
    ///   8. HotkeyManager MonoBehaviour 생성 및 설정 주입
    ///   9. HotkeyManager ↔ CaptureOrchestrator 바인딩
    ///  10. CaptureOverlay 초기화 및 오케스트레이터 바인딩
    ///  11. SilentSubmitManager 초기화
    ///  12. Application.quitting 리소스 정리
    ///  13. PendingUploadManager 초기화
    ///  14. SubmitToast 초기화 및 SilentSubmitManager 바인딩
    /// </summary>
    public static class RekonBootstrap
    {
        private static bool _initialized;
        private static ScreenshotQueue _screenshotQueue;
        private static StreamingVideoRecorder _streamingRecorder;

        /// <summary>
        /// Domain Reload OFF 대응: 정적 상태 리셋.
        /// Domain Reload가 비활성화된 환경(Enter Play Mode Options)에서
        /// Play Mode 재진입 시 정적 필드가 초기화되지 않는 문제를 방지합니다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Domain Reload OFF 대응: 정적 상태 리셋
            _initialized = false;
            Application.quitting -= OnApplicationQuitting;
            _screenshotQueue?.Clear();
            _screenshotQueue = null;
            // 스트리밍 녹화기 정리
            _streamingRecorder?.Dispose();
            _streamingRecorder = null;
            // RekonSettingsProvider 캐시 리셋 — Play Mode 재진입 시 최신 에셋을 다시 로드합니다
            RekonSettingsProvider.ResetCache();
            // BundleWriter 정적 경로 캐시 리셋
            BundleWriter.ResetStaticCache();
        }

        /// <summary>
        /// Application 종료 시 리소스 정리.
        /// 정적 핸들러를 사용하여 Domain Reload OFF 환경에서 람다 누적 구독을 방지합니다.
        /// </summary>
        private static void OnApplicationQuitting()
        {
            _screenshotQueue?.Clear();
            // FFmpeg 프로세스 정리 (Play Mode 종료 시 고아 프로세스 방지)
            _streamingRecorder?.Dispose();
            _streamingRecorder = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // ── 1. Settings 로드 ───────────────────────────────────────────────────
            RekonSettings settings = RekonSettingsProvider.Settings;

            if (settings == null)
            {
                Debug.LogWarning("[Rekon] RekonSettings를 찾을 수 없습니다. " +
                                 "Resources/RekonSettings.asset을 생성하세요. 시스템 초기화를 건너뜁니다.");
                return;
            }

            try
            {
                // ── 2. 루트 GameObject 생성 (씬 전환 후에도 유지) ─────────────────
                var root = new GameObject("[Rekon]");
                Object.DontDestroyOnLoad(root);

                Debug.Log("[Rekon] 부트스트랩 시작...");

                // ── 3. 공통 의존성 생성 ───────────────────────────────────────────

                // 로그 링버퍼: Application.logMessageReceivedThreaded 구독 시작
                var logRingBuffer = new LogRingBuffer(settings.logBufferSize);

                // 로그 직렬화기: 마스킹 활성화
                var logSerializer = new LogSerializer(enableMasking: true);

                // 코루틴 실행용 컴포넌트 (WaitForEndOfFrame 등)
                var coroutineRunner = root.AddComponent<RekonCoroutineRunner>();

                // 스크린샷 캡처기: settings + 코루틴 러너 주입
                var screenshotCapturer = new ScreenshotCapturer(settings, coroutineRunner);

                // 커스텀 컨텍스트 레지스트리: 생성자 파라미터 없음
                var contextRegistry = new ContextProviderRegistry();

                // 상태 스냅샷 수집기: contextRegistry 주입
                var stateCollector = new StateSnapshotCollector(contextRegistry);

                // ── 4. 영상 녹화 관련 컴포넌트 (videoEnabled 시에만) ───────────────
                FrameRingBuffer frameRingBuffer = null;
                IVideoEncoder videoEncoder = null;
                VideoEncoderConfig videoConfig = null;
                FrameCapturer frameCapturer = null;
                StreamingVideoRecorder streamingRecorder = null;

                if (settings.videoEnabled)
                {
#if UNITY_STANDALONE || UNITY_EDITOR
                    // 최초 호출 시 최대 3초 소요 (FFmpeg 프로세스 실행 및 응답 대기)
                    // 이후 호출은 캐시된 결과를 즉시 반환합니다.
                    bool ffmpegAvailable = FfmpegHelper.IsInstalled();
#else
                    bool ffmpegAvailable = false;
#endif

                    if (!ffmpegAvailable)
                    {
                        Debug.LogWarning("[Rekon] FFmpeg 미설치로 영상 녹화가 비활성화됩니다. " +
                                         "스크린샷과 로그 캡처는 정상 동작합니다.");
                        // frameRingBuffer, videoEncoder, videoConfig, frameCapturer, streamingRecorder 모두 null 유지
                    }
                    else
                    {
                        videoConfig = VideoEncoderConfig.FromSettings(settings);

                        // ── 스트리밍 녹화기 생성 (FFmpeg 실시간 인코딩) ──────────────
                        string gpuEncoder = FfmpegHelper.GetGpuEncoder();
                        streamingRecorder = new StreamingVideoRecorder(settings.videoFps, gpuEncoder);
                        _streamingRecorder = streamingRecorder;

                        // 레거시 링버퍼 경로는 유지 (streamingRecorder가 null일 때 폴백)
                        int frameCapacity = settings.videoFps * settings.videoBufferSeconds;
                        frameRingBuffer = new FrameRingBuffer(frameCapacity);
                        videoEncoder = new Mp4VideoEncoder();

                        // FrameCapturer는 MonoBehaviour이므로 root에 AddComponent
                        frameCapturer = root.AddComponent<FrameCapturer>();
                        frameCapturer.Initialize(frameRingBuffer, videoConfig, streamingRecorder);
                        frameCapturer.StartCapturing();

                        Debug.Log($"[Rekon] 스트리밍 영상 녹화 활성화: {videoConfig}, GPU인코더={gpuEncoder ?? "libx264(CPU)"}");
                    }
                }

                // ── 5. ScreenshotQueue 생성 ───────────────────────────────────────
                var screenshotQueue = new ScreenshotQueue();
                _screenshotQueue = screenshotQueue;

                // ── 6. CaptureOrchestrator 생성 ───────────────────────────────────
                var orchestrator = new CaptureOrchestrator(
                    screenshotCapturer: screenshotCapturer,
                    logCollector: logRingBuffer,
                    logSerializer: logSerializer,
                    stateCollector: stateCollector,
                    frameBuffer: frameRingBuffer,         // null 허용 (영상 비활성 시)
                    videoEncoder: videoEncoder,            // null 허용
                    videoConfig: videoConfig,              // null 허용
                    settings: settings,
                    screenshotQueue: screenshotQueue,
                    streamingRecorder: streamingRecorder); // null 허용 (FFmpeg 미설치 시)

                // ── 7. PerformanceTimelineCollector 생성 및 수집 즉시 시작 ─
                var timelineCollector = root.AddComponent<PerformanceTimelineCollector>();
                timelineCollector.StartCollecting(settings.currentPlan);
                orchestrator.BindTimelineCollector(timelineCollector);

                // ── 8. HotkeyManager 생성 및 설정 주입 ───────────────────────────
                var hotkeyManager = root.AddComponent<HotkeyManager>();
                hotkeyManager.SetSettings(settings);

                // ── 9. HotkeyManager ↔ CaptureOrchestrator 바인딩 ────────────────
                orchestrator.BindHotkeyManager(hotkeyManager);

                // ── 10. CaptureOverlay 초기화 ─────────────────────────────────────
                var overlay = CaptureOverlay.EnsureInstance();
                overlay.BindOrchestrator(orchestrator);

                // CaptureOverlay: Silent 모드 활성화 (SilentSubmitManager가 사용되므로)
                overlay.SetSilentMode(true);

                overlay.BindScreenshotQueue(screenshotQueue);
                overlay.BindSettings(settings);

                // 롱프레스 UX 피드백: HotkeyManager 이벤트 구독
                overlay.BindHotkeyManager(hotkeyManager);

                // ── 11. SilentSubmitManager 초기화 ───────────────────────────────
                var manifestGenerator = new ManifestGenerator();
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
                        Debug.Log("[Rekon] ReportSubmitService 초기화 완료 (Web 프록시 모드)");
                    }
                    catch (System.Exception submitEx)
                    {
                        Debug.LogWarning($"[Rekon] ReportSubmitService 초기화 실패 (로컬 저장만 가능): {submitEx.Message}");
                    }
                }

                var tokenStore = new SessionTokenStore();

                // ── 라이선스 캐시에서 currentPlan 복원 (플레이어 빌드 대응) ──────────
                // RekonSettingsWindow는 에디터 전용이므로 플레이어 빌드에서는
                // currentPlan이 기본값 "free"로 고정될 수 있습니다.
                // LicenseValidator 캐시(PlayerPrefs)에서 플랜 정보를 읽어 즉시 반영합니다.
                if (settings.isLinked)
                {
                    try
                    {
                        var licenseValidator = new LicenseValidator(
                            RekonSettings.WEB_DASHBOARD_URL, tokenStore);
                        var cached = licenseValidator.GetCachedLicense();
                        if (cached != null && cached.Valid && !string.IsNullOrEmpty(cached.Plan))
                        {
                            settings.currentPlan               = cached.Plan;
                            settings.maxAllowedBufferSeconds   = cached.MaxBufferSeconds;
                            settings.maxAllowedScreenshotCount = cached.MaxScreenshotCount;
                            Debug.Log($"[Rekon] 캐시에서 플랜 복원: plan={cached.Plan}, " +
                                      $"maxBuffer={cached.MaxBufferSeconds}초, " +
                                      $"maxScreenshot={cached.MaxScreenshotCount}개");
                        }
                    }
                    catch (System.Exception licEx)
                    {
                        Debug.LogWarning($"[Rekon] 라이선스 캐시 복원 실패 (free 플랜으로 유지): {licEx.Message}");
                    }
                }

                // tokenStore를 CaptureOrchestrator에도 주입 — 캡처 전 사용량 사전 체크에 사용
                orchestrator.BindTokenStore(tokenStore);

                var silentSubmitManager = new SilentSubmitManager(settings, bundleWriter, tokenStore, submitService);
                silentSubmitManager.BindOrchestrator(orchestrator);
                // 제출 중 캡처 차단을 위해 오케스트레이터에 SilentSubmitManager 역방향 바인딩
                orchestrator.BindSilentSubmitManager(silentSubmitManager);

                // ── 12. Application.quitting 시 리소스 정리 ──────────────────────
                // 정적 핸들러 사용으로 Domain Reload OFF 환경에서 람다 누적 구독 방지
                Application.quitting -= OnApplicationQuitting;  // 중복 방지
                Application.quitting += OnApplicationQuitting;

                // ── 13. PendingUploadManager 초기화 ──────────────────────────────────
                var pendingUploadManager = new PendingUploadManager();

                // SilentSubmitManager에 PendingUploadManager 바인딩
                silentSubmitManager.BindPendingUploadManager(pendingUploadManager);

                int pendingCount = pendingUploadManager.GetPendingCount();
                if (pendingCount > 0)
                {
                    Debug.Log($"[Rekon] 앱 시작 시 pending 번들 {pendingCount}개 감지. 향후 미전송 리포트 UI에서 재전송 가능합니다.");
                }

                // ── 14. SubmitToast 초기화 ──────────────────────────────────────────
                var submitToast = SubmitToast.EnsureInstance();
                submitToast.BindSilentSubmitManager(silentSubmitManager);

                Debug.Log("[Rekon] 부트스트랩 완료. 핫키 시스템, 캡처 파이프라인, Silent Submit, PendingUpload, 토스트 UI가 활성화되었습니다.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Rekon] 부트스트랩 초기화 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
