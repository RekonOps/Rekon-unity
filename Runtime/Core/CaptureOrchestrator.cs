using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 캡처 파이프라인 전체를 조율하는 오케스트레이터.
    ///
    /// 책임:
    ///   1. HotkeyManager.OnCaptureTrigger 이벤트 구독
    ///   2. 트리거 시 스크린샷 + 로그 + 상태 + 영상을 병렬 수집 (Task.WhenAll)
    ///   3. 임시 폴더에 아티팩트 저장
    ///   4. CaptureResult 반환
    ///   5. CaptureProgressEvent 발행
    ///   6. 5초 타임아웃 처리
    ///
    /// 의존성은 생성자 주입(DI)으로 제공됩니다.
    /// </summary>
    public class CaptureOrchestrator : ICaptureOrchestrator, IDisposable
    {
        private const float TimeoutSeconds = 5f;

        private readonly IScreenshotCapturer _screenshotCapturer;
        private readonly ILogCollector _logCollector;
        private readonly LogSerializer _logSerializer;
        private readonly IStateSnapshotCollector _stateCollector;
        private readonly FrameRingBuffer _frameBuffer;
        private readonly IVideoEncoder _videoEncoder;
        private readonly VideoEncoderConfig _videoConfig;
        private readonly RekonSettings _settings;
        private readonly StreamingVideoRecorder _streamingRecorder;
        // 생성자 주입 토큰 스토어 (null 허용)
        private readonly SessionTokenStore _tokenStore;
        // 런타임 바인딩 토큰 스토어 — BindTokenStore() 호출 시 설정되며, _tokenStore보다 우선합니다.
        private SessionTokenStore _runtimeTokenStore;

        // 실제 사용할 토큰 스토어: 런타임 바인딩 우선, 없으면 생성자 주입 사용
        private SessionTokenStore ActiveTokenStore => _runtimeTokenStore ?? _tokenStore;

        // HTTP 클라이언트 — BindHttpClient() 또는 기본값 UnityHttpClient 사용
        private IRekonHttpClient _httpClient;

        // 성능 타임라인 수집기 (null 허용 — 미연동 시 수집 건너뜀)
        private PerformanceTimelineCollector _timelineCollector;

        // team_pro 전용 시간 윈도우 로그 수집기 — 영상 캡처 경로 (null 허용 — free/team 플랜 시 null)
        private ReplayLogCollector _replayLogCollector;

        // team_pro 전용 스크린샷 경로 로그 수집기 (null 허용 — free/team 또는 미바인딩 시 null)
        // 영상 없는 스크린샷 리포트에서 .jsonl 로그를 수집·전송하기 위해 별도 관리합니다.
        private ReplayLogCollector _screenshotReplayLogCollector;

        private readonly ScreenshotQueue _screenshotQueue;

        private HotkeyManager _hotkeyManager;
        private SilentSubmitManager _silentSubmitManager;
        private bool _disposed;
        // 원자적 플래그: 0 = 대기, 1 = 캡처 중
        private int _isCapturingFlag;

        /// <summary>캡처 진행 상황 이벤트</summary>
        public event Action<CaptureProgressEvent> OnProgress;

        /// <summary>캡처 완료 시 발행되는 이벤트 (CaptureResult를 인자로 받음)</summary>
        public event Action<CaptureResult> OnCaptureCompleted;

        /// <summary>스크린샷 큐에 새 항목이 추가됐을 때 발행 (인자: 현재 큐 크기, eviction 발생 여부)</summary>
        public event Action<int, bool> OnScreenshotQueued;

        /// <summary>
        /// 스크린샷 전용 발송 완료 시 발행 (인자: 성공 여부, 발송된 장수).
        /// 성공 시 true + 장수, 큐 비었거나 실패 시 false + 0.
        /// </summary>
        public event Action<bool, int> OnScreenshotSubmitCompleted;

        /// <summary>
        /// CaptureOrchestrator를 초기화합니다.
        /// </summary>
        /// <param name="tokenStore">세션 토큰 저장소. null 허용 — null이면 사용량 사전 체크를 건너뜁니다.</param>
        public CaptureOrchestrator(
            IScreenshotCapturer screenshotCapturer,
            ILogCollector logCollector,
            LogSerializer logSerializer,
            IStateSnapshotCollector stateCollector,
            FrameRingBuffer frameBuffer,
            IVideoEncoder videoEncoder,
            VideoEncoderConfig videoConfig,
            RekonSettings settings,
            ScreenshotQueue screenshotQueue = null,
            SessionTokenStore tokenStore = null,
            StreamingVideoRecorder streamingRecorder = null)
        {
            _screenshotCapturer = screenshotCapturer ?? throw new ArgumentNullException(nameof(screenshotCapturer));
            _logCollector = logCollector ?? throw new ArgumentNullException(nameof(logCollector));
            _logSerializer = logSerializer ?? throw new ArgumentNullException(nameof(logSerializer));
            _stateCollector = stateCollector ?? throw new ArgumentNullException(nameof(stateCollector));
            _frameBuffer = frameBuffer; // null 허용 (영상 비활성 시)
            _videoEncoder = videoEncoder; // null 허용
            _videoConfig = videoConfig; // null 허용
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _screenshotQueue = screenshotQueue; // null 허용 (스크린샷 큐 비활성 시)
            _tokenStore = tokenStore; // null 허용 (미연동 시 사전 체크 건너뜀)
            _streamingRecorder = streamingRecorder; // null 허용 (스트리밍 모드 비활성 시)
        }

        /// <summary>
        /// 플랜별 영상 버퍼 상한을 적용한 실효(effective) 버퍼 시간을 계산합니다.
        /// 사용자가 설정한 <paramref name="videoBufferSeconds"/> 가 플랜이 허용하는
        /// <paramref name="maxAllowedBufferSeconds"/> 를 넘으면 플랜값으로 clamp 합니다.
        ///   free 60 / team·team_pro 90 (백엔드 validate-license 가 maxAllowedBufferSeconds 로 내려줌).
        /// maxAllowedBufferSeconds 가 0 이하(=플랜값 미수신)면 clamp 하지 않고 설정값을 그대로 사용합니다.
        /// 순수 함수 — 테스트 가능하도록 public static 으로 노출합니다.
        /// </summary>
        public static int ResolveEffectiveBufferSeconds(int videoBufferSeconds, int maxAllowedBufferSeconds)
        {
            return maxAllowedBufferSeconds > 0
                ? Mathf.Min(videoBufferSeconds, maxAllowedBufferSeconds)
                : videoBufferSeconds;
        }

        /// <summary>
        /// SessionTokenStore를 런타임에 바인딩합니다.
        /// 부트스트랩에서 tokenStore 생성 후 오케스트레이터에 주입할 때 사용합니다.
        /// 이미 생성자에서 주입된 경우에도 교체할 수 있습니다.
        /// </summary>
        public void BindTokenStore(SessionTokenStore tokenStore)
        {
            // _tokenStore는 readonly가 아니므로 필드를 직접 교체하는 대신
            // 별도의 런타임 오버라이드 필드를 사용합니다.
            _runtimeTokenStore = tokenStore;
            Debug.Log("[Rekon] CaptureOrchestrator: SessionTokenStore 바인딩 완료");
        }

        /// <summary>
        /// IRekonHttpClient를 런타임에 바인딩합니다.
        /// 테스트에서 MockHttpClient를 주입할 때 사용합니다.
        /// 호출하지 않으면 CheckUsageLimitAsync에서 UnityHttpClient를 사용합니다.
        /// </summary>
        public void BindHttpClient(IRekonHttpClient httpClient)
        {
            _httpClient = httpClient;
            Debug.Log("[Rekon] CaptureOrchestrator: IRekonHttpClient 바인딩 완료");
        }

        /// <summary>
        /// PerformanceTimelineCollector를 바인딩합니다.
        /// 영상 녹화 시작/종료 시 성능 데이터 수집에 사용됩니다.
        /// </summary>
        public void BindTimelineCollector(PerformanceTimelineCollector collector)
        {
            _timelineCollector = collector;
            Debug.Log("[Rekon] CaptureOrchestrator: PerformanceTimelineCollector 바인딩 완료");
        }

        /// <summary>
        /// team_pro 전용 ReplayLogCollector를 바인딩합니다 (BindTimelineCollector 패턴).
        /// team_pro 플랜 시에만 RekonBootstrap에서 호출됩니다.
        /// null 전달 시 기존 _logCollector(LogRingBuffer) 경로로 fallback됩니다.
        /// ⚠️ 이 바인딩은 영상 캡처 경로 전용입니다. 스크린샷 경로는 BindScreenshotReplayLogCollector 사용.
        /// </summary>
        public void BindReplayLogCollector(ReplayLogCollector collector)
        {
            _replayLogCollector = collector;
            Debug.Log("[Rekon] CaptureOrchestrator: ReplayLogCollector 바인딩 완료 (team_pro 영상 리플레이 활성)");
        }

        /// <summary>
        /// team_pro 전용 스크린샷 경로 ReplayLogCollector를 바인딩합니다.
        /// 영상 없는 스크린샷 전용 리포트(SubmitScreenshotOnlyAsync)에서 .jsonl 로그를 수집·전송합니다.
        /// team_pro 플랜 시에만 RekonBootstrap에서 호출됩니다.
        /// null 전달 시 스크린샷 리포트에서 .jsonl 수집을 건너뜁니다.
        /// </summary>
        public void BindScreenshotReplayLogCollector(ReplayLogCollector collector)
        {
            _screenshotReplayLogCollector = collector;
            if (collector != null)
                Debug.Log("[Rekon] CaptureOrchestrator: 스크린샷 경로 ReplayLogCollector 바인딩 완료 (team_pro 싱크 활성)");
            else
                Debug.Log("[Rekon] CaptureOrchestrator: 스크린샷 경로 ReplayLogCollector 해제됨");
        }

        /// <summary>
        /// SilentSubmitManager를 바인딩합니다.
        /// 캡처 시작 전 제출 진행 여부를 확인하는 데 사용됩니다.
        /// </summary>
        public void BindSilentSubmitManager(SilentSubmitManager manager)
        {
            _silentSubmitManager = manager;
            Debug.Log("[Rekon] CaptureOrchestrator: SilentSubmitManager 바인딩 완료");
        }

        /// <summary>
        /// HotkeyManager를 등록하고 OnCaptureTrigger / OnScreenshotTrigger / OnScreenshotLongPress 이벤트를 구독합니다.
        /// </summary>
        public void BindHotkeyManager(HotkeyManager hotkeyManager)
        {
            if (_hotkeyManager != null)
            {
                _hotkeyManager.OnCaptureTrigger -= OnTrigger;
                _hotkeyManager.OnScreenshotTrigger -= OnScreenshotTrigger;
                _hotkeyManager.OnScreenshotLongPress -= OnScreenshotLongPress;
            }

            _hotkeyManager = hotkeyManager;

            if (_hotkeyManager != null)
            {
                _hotkeyManager.OnCaptureTrigger += OnTrigger;
                _hotkeyManager.OnScreenshotTrigger += OnScreenshotTrigger;
                _hotkeyManager.OnScreenshotLongPress += OnScreenshotLongPress;
            }
        }

        /// <summary>
        /// 모든 서브시스템에서 병렬로 데이터를 수집하고 CaptureResult를 반환합니다.
        /// 5초 타임아웃이 초과되면 수집 가능한 아티팩트만 포함하여 반환합니다.
        /// </summary>
        public async Task<CaptureResult> StartAsync()
        {
            // 원자적 CAS: 0(대기) → 1(캡처 중) 으로 교체. 이미 1이면 중복 진입 차단
            if (Interlocked.CompareExchange(ref _isCapturingFlag, 1, 0) != 0)
            {
                Debug.LogWarning("[Rekon] 이미 캡처가 진행 중입니다.");
                return null;
            }

            // 제출 진행 중이면 새 캡처 차단
            if (_silentSubmitManager != null && _silentSubmitManager.IsSubmitting)
            {
                Debug.LogWarning("[Rekon] 제출이 진행 중입니다. 캡처를 시작할 수 없습니다.");
                // 획득한 캡처 플래그 반환
                Interlocked.Exchange(ref _isCapturingFlag, 0);
                return null;
            }

            // 사용량 사전 체크: 웹 대시보드 연동 상태이고 유효한 토큰이 있을 때만 수행
            bool usagePreCheckEnabled = _settings.isLinked
                && ActiveTokenStore != null
                && ActiveTokenStore.HasValidSupabaseToken();

            if (usagePreCheckEnabled)
            {
                var usageCheck = await CheckUsageLimitAsync();
                if (usageCheck != null && !usageCheck.Allowed)
                {
                    string limitLabel = "월간 한도 도달";
                    Debug.LogWarning($"[Rekon] 사용량 한도 초과: {usageCheck.Reason}");
                    ReportProgress("usage_limit", 0f, limitLabel);
                    // 획득한 캡처 플래그 반환
                    Interlocked.Exchange(ref _isCapturingFlag, 0);
                    return null;
                }
            }

            // 인코더별 권장 타임아웃 적용 (인코딩 방식에 따라 소요 시간이 다름)
            float effectiveTimeout = (_videoEncoder != null)
                ? _videoEncoder.RecommendedTimeoutSeconds
                : TimeoutSeconds;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));
            var result = new CaptureResult { Timestamp = DateTime.UtcNow };

            // 영상-로그 싱크용 스냅샷(realtime 단일 축):
            //   캡처 트리거 시각과, 그 시점까지 인코딩된 프레임 수를 여기서 고정한다.
            //   videoTask 의 Restart() 가 FramesWritten 을 0으로 리셋하므로, 병렬 logsTask 가
            //   읽기 전에 트리거 시점 값을 스냅샷해야 race-free 하다.
            double captureTriggerT = Time.realtimeSinceStartupAsDouble;
            long videoFramesAtTrigger = _streamingRecorder?.FramesWritten ?? 0L;

            // 임시 디렉토리 생성
            string captureDir = CreateCaptureDirectory(result.Timestamp);

            try
            {
                // 성능 타임라인은 부트스트랩에서 이미 수집 중 (StartCollecting은 Bootstrap에서 호출)

                // 병렬 수집
                // 영상 캡처 트리거 시에는 스크린샷을 캡처하지 않음 (별도 스크린샷 트리거 사용)
                var screenshotTask = _settings.videoEnabled
                    ? Task.CompletedTask
                    : CaptureScreenshotAsync(captureDir, result, cts.Token);
                var logsTask = CaptureLogsAsync(captureDir, result, captureTriggerT, videoFramesAtTrigger, cts.Token);
                var stateTask = CaptureStateAsync(captureDir, result, cts.Token);
                var videoTask = CaptureVideoAsync(captureDir, result, cts.Token);

                await Task.WhenAll(screenshotTask, logsTask, stateTask, videoTask);

                // 성능 타임라인 수집 종료 및 결과에 포함
                if (_timelineCollector != null)
                {
                    try
                    {
                        result.PerformanceTimeline = _timelineCollector.StopCollecting();
                        // 다음 리포트를 위해 수집 재시작
                        _timelineCollector.StartCollecting(_settings.currentPlan);
                    }
                    catch (Exception tlEx)
                    {
                        Debug.LogWarning($"[Rekon] 성능 타임라인 수집 종료 실패 (무시): {tlEx.Message}");
                    }
                }

                // 스크린샷 큐 드레인 — 영상 번들에 포함
                if (_screenshotQueue != null)
                {
                    var entries = _screenshotQueue.DrainAll();
                    if (entries.Length > 0)
                    {
                        // CaptureRealtime 오름차순 정렬: 비동기 캡처 완료 순서가 달라도
                        // screenshot_0 이 항상 가장 먼저 캡처된 항목이 되도록 보장합니다.
                        SortEntriesByCaptureRealtime(entries);
                        result.ScreenshotEntries = entries;
                        Debug.Log($"[Rekon] 스크린샷 큐 드레인: {entries.Length}장 영상 번들에 포함 (캡처 시각 순 정렬)");
                    }
                }

                ReportProgress("complete", 1.0f);
                OnCaptureCompleted?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning($"[Rekon] 캡처 타임아웃 ({effectiveTimeout}초 초과). 수집된 아티팩트만 반환합니다.");
                ReportProgress("complete", 1.0f, "타임아웃");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 캡처 중 예기치 않은 오류: {ex.Message}");
            }
            finally
            {
                // 실패/타임아웃 경로에서도 StopCollecting 호출 → sceneLoaded 이벤트 누수 방지
                // StopCollecting 내부에서 _isCollecting = false 처리하므로 이중 호출 안전
                if (_timelineCollector != null && result.PerformanceTimeline == null)
                {
                    try { _timelineCollector.StopCollecting(); }
                    catch (Exception tlEx)
                    {
                        Debug.LogWarning($"[Rekon] finally: 성능 타임라인 정리 실패 (무시): {tlEx.Message}");
                    }
                }

                // 원자적으로 플래그 해제: 1(캡처 중) → 0(대기)
                Interlocked.Exchange(ref _isCapturingFlag, 0);
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_hotkeyManager != null)
            {
                _hotkeyManager.OnCaptureTrigger -= OnTrigger;
                _hotkeyManager.OnScreenshotTrigger -= OnScreenshotTrigger;
                _hotkeyManager.OnScreenshotLongPress -= OnScreenshotLongPress;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        private void OnTrigger()
        {
            if (_disposed)
                return;

            // 핫키 이벤트 → 비동기 캡처 시작 (결과는 OnCaptureCompleted 이벤트로 전달)
            _ = StartAsync();
        }

        private void OnScreenshotTrigger()
        {
            if (_disposed) return;
            _ = StartScreenshotAsync();
        }

        private void OnScreenshotLongPress()
        {
            if (_disposed) return;
            // 결과는 OnScreenshotSubmitCompleted 이벤트로 전달됨
            _ = SubmitScreenshotOnlyAsync();
        }

        /// <summary>
        /// 스크린샷 전용 캡처를 수행하여 ScreenshotQueue에 저장합니다.
        /// 영상 파이프라인을 실행하지 않습니다.
        /// </summary>
        public async Task StartScreenshotAsync()
        {
            if (_screenshotQueue == null)
            {
                Debug.LogWarning("[Rekon] ScreenshotQueue가 바인딩되지 않았습니다.");
                return;
            }

            // 영상 캡처 진행 중이면 스크린샷 캡처 스킵
            if (Interlocked.CompareExchange(ref _isCapturingFlag, 0, 0) != 0)
            {
                Debug.LogWarning("[Rekon] 영상 캡처 진행 중 — 스크린샷 캡처를 건너뜁니다.");
                return;
            }

            try
            {
                // team_pro 싱크: 캡처 직전 realtimeSinceStartupAsDouble 을 기록 (로그 t_abs 와 동일한 시간축)
                double captureRealtime = Time.realtimeSinceStartupAsDouble;

                byte[] pngBytes = await _screenshotCapturer.CaptureAsync();
                if (pngBytes == null || pngBytes.Length == 0)
                {
                    Debug.LogWarning("[Rekon] 스크린샷 캡처 실패 (빈 데이터)");
                    return;
                }

                bool evicted = _screenshotQueue.Enqueue(pngBytes, DateTime.UtcNow, captureRealtime);
                int newCount = _screenshotQueue.Count;

                Debug.Log($"[Rekon] 스크린샷 큐 추가 완료 ({newCount}/{_screenshotQueue.Capacity}){(evicted ? " — 오래된 항목 교체됨" : "")}");
                OnScreenshotQueued?.Invoke(newCount, evicted);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 스크린샷 전용 캡처 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 스크린샷 큐를 드레인하여 스크린샷 전용 리포트를 발송합니다.
        /// 영상/로그/상태 수집 없이 큐에 쌓인 스크린샷만 포함합니다.
        /// 롱프레스 완료 시 호출됩니다.
        /// </summary>
        public async Task<bool> SubmitScreenshotOnlyAsync()
        {
            if (_disposed)
                return false;

            if (_screenshotQueue == null || _screenshotQueue.Count == 0)
            {
                Debug.LogWarning("[Rekon] 발송할 스크린샷이 없습니다.");
                OnScreenshotSubmitCompleted?.Invoke(false, 0);
                return false;
            }

            // 영상 캡처 중이면 발송 대기 없이 즉시 반환 (중복 방지)
            if (Interlocked.CompareExchange(ref _isCapturingFlag, 0, 0) != 0)
            {
                Debug.LogWarning("[Rekon] 영상 캡처 진행 중 — 스크린샷 전용 발송을 건너뜁니다.");
                OnScreenshotSubmitCompleted?.Invoke(false, 0);
                return false;
            }

            // 제출 진행 중이면 차단
            if (_silentSubmitManager != null && _silentSubmitManager.IsSubmitting)
            {
                Debug.LogWarning("[Rekon] 제출이 진행 중입니다. 스크린샷 전용 발송을 건너뜁니다.");
                OnScreenshotSubmitCompleted?.Invoke(false, 0);
                return false;
            }

            try
            {
                // 큐 드레인
                var entries = _screenshotQueue.DrainAll();
                if (entries.Length == 0)
                {
                    Debug.LogWarning("[Rekon] 발송할 스크린샷이 없습니다.");
                    OnScreenshotSubmitCompleted?.Invoke(false, 0);
                    return false;
                }

                // CaptureRealtime 오름차순 정렬: 비동기 캡처 완료 순서가 달라도
                // screenshot_0 이 항상 가장 먼저 캡처된 항목이 되도록 보장합니다.
                // ManifestGenerator 가 ScreenshotEntries[i] → screenshot_i.png 로 부여하고,
                // SilentSubmitManager 가 screenshot_N → ScreenshotEntries[N].CaptureRealtime 역참조하므로
                // 이 정렬이 captured_t_abs 와 파일명 순서를 일치시키는 핵심 단계입니다.
                SortEntriesByCaptureRealtime(entries);

                // 스크린샷 전용 CaptureResult 생성 (로그 + 상태 포함)
                var result = new CaptureResult
                {
                    Timestamp = DateTime.UtcNow,
                    ScreenshotEntries = entries,
                };

                // 로그 + 상태 수집 (임시 디렉토리에 저장)
                try
                {
                    string tempDir = Path.Combine(
                        Application.temporaryCachePath, "Rekon",
                        DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
                    Directory.CreateDirectory(tempDir);

                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));

                    // team_pro: _screenshotReplayLogCollector 가 바인딩된 경우 .jsonl 로그 수집
                    // 영상 경로의 _replayLogCollector 와 독립적으로 동작 — 영상 경로 불변 보장
                    await Task.WhenAll(
                        CaptureScreenshotLogsAsync(tempDir, result, cts.Token),
                        CaptureStateAsync(tempDir, result, cts.Token)
                    );
                }
                catch (Exception logEx)
                {
                    Debug.LogWarning($"[Rekon] 스크린샷 리포트 로그/상태 수집 실패 (무시): {logEx.Message}");
                }

                Debug.Log($"[Rekon] 스크린샷 전용 리포트 발송: {entries.Length}장 (로그: {(result.LogsPath != null ? "포함" : "없음")})");
                OnCaptureCompleted?.Invoke(result);
                OnScreenshotSubmitCompleted?.Invoke(true, entries.Length);

                await Task.CompletedTask; // async 시그니처 유지
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 스크린샷 전용 발송 실패: {ex.Message}");
                OnScreenshotSubmitCompleted?.Invoke(false, 0);
                return false;
            }
        }

        private async Task CaptureScreenshotAsync(string dir, CaptureResult result, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                byte[] pngBytes = await _screenshotCapturer.CaptureAsync();
                if (pngBytes != null && pngBytes.Length > 0)
                {
                    string path = Path.Combine(dir, "screenshot.png");
                    await _screenshotCapturer.SaveAsync(pngBytes, path);
                    result.ScreenshotPath = path;
                }

                ReportProgress("screenshot", 0.25f);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 스크린샷 캡처 실패: {ex.Message}");
                ReportProgress("screenshot", 0.25f, ex.Message);
            }
        }

        private async Task CaptureLogsAsync(string dir, CaptureResult result, double captureTriggerT, long videoFramesAtTrigger, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                bool isTeamPro = _settings.currentPlan == "team_pro";

                if (isTeamPro && _replayLogCollector != null)
                {
                    // ── team_pro: JSONL 직렬화 + ReplayMetadata 적재 ──────────────
                    LogEntry[] entries = _replayLogCollector.GetEntries();
                    string jsonlPath = Path.Combine(dir, "logs.jsonl");
                    await _logSerializer.SaveAsJsonlAsync(entries, jsonlPath);
                    result.LogsPath = jsonlPath;

                    // 영상 시작/길이 산출 (realtime 단일 축, clock_offset=0).
                    //   스트리밍 모드(프로덕션 기본)에선 레거시 링버퍼(_frameBuffer)가 항상 비므로,
                    //   인코딩 길이(FramesWritten/fps)로 역산한다. -sseof 로 마지막 N초만 추출하므로
                    //   min(buffer, encoded) 사용. 인코딩 길이 기준이라 클립 "끝"(=캡처 트리거 시점)이
                    //   정확히 맞는다(프레임 드랍 시 시작쪽만 어긋남 — 감수).
                    //   비스트리밍(FFmpeg 미설치) 경로만 기존 링버퍼 실측(unscaled)으로 fallback.
                    double videoStartT;
                    double videoDurationS;
                    if (_streamingRecorder != null && _settings != null && _settings.videoEnabled)
                    {
                        int fps = Mathf.Max(1, _settings.videoFps);
                        // 플랜 상한 적용(free 60 / team·pro 90) — StopAndExtractAsync 에 넘기는 값과 동일하게 clamp.
                        int bufferSeconds = ResolveEffectiveBufferSeconds(
                            _settings.videoBufferSeconds, _settings.maxAllowedBufferSeconds);
                        double encodedSeconds = videoFramesAtTrigger / (double)fps;
                        videoDurationS = System.Math.Min(bufferSeconds, encodedSeconds);
                        videoStartT    = captureTriggerT - videoDurationS; // realtime 축
                    }
                    else
                    {
                        FrameData[] frames = _frameBuffer?.GetFrames();
                        videoStartT    = (frames != null && frames.Length > 0) ? frames[0].Timestamp : 0.0;
                        videoDurationS = (frames != null && frames.Length > 0)
                            ? frames[frames.Length - 1].Timestamp - frames[0].Timestamp : 0.0;
                    }

                    // realtime 단일 축이라 시계 보정 불필요
                    double clockOffset = 0.0;

                    // 로그 수 분류 (로그·영상 모두 realtime 축, clock_offset=0)
                    int countInVideo     = CountLogsInRange(entries, videoStartT,             videoStartT + videoDurationS, clockOffset);
                    int countBeforeVideo = CountLogsInRange(entries, double.NegativeInfinity, videoStartT,                  clockOffset);

                    result.ReplayMetadata = new ReplayMetadata
                    {
                        video_start_t_abs      = videoStartT,
                        video_duration_s       = videoDurationS,
                        capture_trigger_t_abs  = captureTriggerT,
                        play_mode_start_t_abs  = 0.0,   // Play Mode 시작 = 0 기준
                        clock_offset           = clockOffset, // realtime 단일 축이라 0
                        log_count_total        = entries.Length,
                        log_count_in_video     = countInVideo,
                        log_count_before_video = countBeforeVideo,
                        schema_version         = 2,
                    };

                    Debug.Log($"[Rekon] team_pro JSONL 로그 + ReplayMetadata 완료: " +
                              $"총 {entries.Length}건, 영상 내 {countInVideo}건, 이전 {countBeforeVideo}건");
                }
                else
                {
                    // ── free/team: 기존 텍스트 직렬화 (100% 보존) ────────────────
                    LogEntry[] entries = _logCollector.GetEntries();
                    string path = Path.Combine(dir, "logs.txt");
                    await _logSerializer.SaveAsync(entries, path);
                    result.LogsPath = path;
                    result.ReplayMetadata = null;
                }

                ReportProgress("logs", 0.50f);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 로그 수집 실패: {ex.Message}");
                ReportProgress("logs", 0.50f, ex.Message);
            }
        }

        /// <summary>
        /// 스크린샷 전용 리포트의 로그를 수집합니다.
        ///
        /// team_pro + _screenshotReplayLogCollector 바인딩 시:
        ///   → .jsonl 로그 수집 + 전송 (captured_t_abs 싱크용)
        ///   → replay_metadata 는 생성하지 않음 (스크린샷 경로 — 영상 없음)
        ///
        /// free/team 또는 _screenshotReplayLogCollector 미바인딩 시:
        ///   → 기존 LogRingBuffer(txt) 경로로 fallback (100% 불변)
        ///
        /// ⚠️ 영상 경로(_replayLogCollector)에는 절대 영향 없음.
        /// </summary>
        private async Task CaptureScreenshotLogsAsync(string dir, CaptureResult result, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                bool isTeamPro = _settings.currentPlan == "team_pro";

                if (isTeamPro && _screenshotReplayLogCollector != null)
                {
                    // ── team_pro: JSONL 직렬화 (captured_t_abs 싱크 마커용) ────────
                    // replay_metadata 는 영상 전용 — 스크린샷 경로에서는 생성하지 않음
                    LogEntry[] entries = _screenshotReplayLogCollector.GetEntries();
                    string jsonlPath = Path.Combine(dir, "logs.jsonl");
                    await _logSerializer.SaveAsJsonlAsync(entries, jsonlPath);
                    result.LogsPath = jsonlPath;
                    // replay_metadata 는 null 유지 (스크린샷 경로 — 영상+로그 싱크 메타 불필요)
                    result.ReplayMetadata = null;

                    Debug.Log($"[Rekon] 스크린샷 리포트 team_pro JSONL 로그 수집 완료: {entries.Length}건 " +
                              $"(replay_metadata 없음 — captured_t_abs 직접 비교)");
                }
                else
                {
                    // ── free/team 또는 미바인딩: 기존 텍스트 직렬화 (100% 보존) ────
                    LogEntry[] entries = _logCollector.GetEntries();
                    string path = Path.Combine(dir, "logs.txt");
                    await _logSerializer.SaveAsync(entries, path);
                    result.LogsPath = path;
                    result.ReplayMetadata = null;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 스크린샷 리포트 로그 수집 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// ScreenshotEntry 배열을 CaptureRealtime 오름차순으로 정렬합니다.
        /// CaptureRealtime = 0.0 인 항목(기존 2-파라미터 Enqueue)은 안정적으로 끝으로 밀리지 않고
        /// 0 값끼리는 원래 순서를 유지합니다 (Array.Sort 은 불안정 정렬이지만 0 vs 0 교환은 무해).
        ///
        /// 호출 위치:
        ///   - StartAsync() 내 영상 번들 경로 DrainAll 직후
        ///   - SubmitScreenshotOnlyAsync() 내 DrainAll 직후
        ///
        /// 보장: screenshot_0 = 가장 먼저 캡처된 항목, screenshot_N-1 = 마지막 캡처.
        /// </summary>
        private static void SortEntriesByCaptureRealtime(ScreenshotEntry[] entries)
        {
            if (entries == null || entries.Length <= 1)
                return;

            Array.Sort(entries, (a, b) => a.CaptureRealtime.CompareTo(b.CaptureRealtime));
        }

        /// <summary>
        /// 지정 realtime 구간 내 로그 수를 반환합니다.
        /// 로그는 realtime 축, 영상 구간은 unscaled 축이므로 clockOffset으로 변환하여 비교합니다.
        /// logT(realtime) → unscaled 변환: logT - clockOffset
        /// </summary>
        private static int CountLogsInRange(
            LogEntry[] entries,
            double rangeStartUnscaled,
            double rangeEndUnscaled,
            double clockOffset)
        {
            int count = 0;
            foreach (var e in entries)
            {
                // 로그(realtime 축) → unscaled 축 환산
                double logUnscaled = e.Timestamp - clockOffset;
                if (logUnscaled >= rangeStartUnscaled && logUnscaled < rangeEndUnscaled)
                    count++;
            }
            return count;
        }

        private async Task CaptureStateAsync(string dir, CaptureResult result, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                StateSnapshot snapshot = await _stateCollector.CollectAsync();
                string json = JsonUtility.ToJson(snapshot, prettyPrint: true);
                string path = Path.Combine(dir, "state.json");

                await Task.Run(() => File.WriteAllText(path, json, System.Text.Encoding.UTF8), token);
                result.StatePath = path;

                // 수집된 스냅샷 객체를 결과에 세팅 (ManifestGenerator가 환경 정보 추출에 사용)
                result.StateSnapshot = snapshot;

                ReportProgress("state", 0.75f);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 상태 수집 실패: {ex.Message}");
                ReportProgress("state", 0.75f, ex.Message);
            }
        }

        private async Task CaptureVideoAsync(string dir, CaptureResult result, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                // 영상 캡처가 비활성화된 경우 건너뜀
                if (!_settings.videoEnabled)
                {
                    ReportProgress("video", 1.0f);
                    return;
                }

                // ── 스트리밍 모드 ──────────────────────────────────────────────────
                if (_streamingRecorder != null && _streamingRecorder.IsRecording)
                {
                    // 플랜 상한 적용(free 60 / team·pro 90) — 추출 길이도 동일하게 clamp.
                    int bufferSeconds = _settings != null
                        ? ResolveEffectiveBufferSeconds(_settings.videoBufferSeconds, _settings.maxAllowedBufferSeconds)
                        : 60;
                    string videoPath = await _streamingRecorder.StopAndExtractAsync(bufferSeconds);

                    if (!string.IsNullOrEmpty(videoPath) && File.Exists(videoPath))
                    {
                        result.VideoPath = videoPath;
                        Debug.Log($"[Rekon] 스트리밍 영상 추출 완료: {videoPath}");
                    }
                    else
                    {
                        Debug.LogWarning("[Rekon] 스트리밍 영상 추출 실패 또는 파일 없음");
                    }

                    // 다음 캡처를 위해 녹화 재시작
                    _streamingRecorder.Restart();

                    ReportProgress("video", 1.0f);
                    return;
                }

                // ── 레거시 링버퍼 모드 ───────────────────────────────────────────
                if (_frameBuffer == null || _videoEncoder == null)
                {
                    ReportProgress("video", 1.0f);
                    return;
                }

                // GetFrames(): 사전 할당 방식 — 소유권 이전 없이 현재 버퍼 스냅샷을 읽어옵니다.
                FrameData[] frames = _frameBuffer.GetFrames();
                if (frames.Length > 0)
                {
                    // 인코더의 OutputExtension을 사용하여 출력 경로 결정 (OCP 적용)
                    string ext = _videoEncoder.OutputExtension;
                    string videoPath = string.IsNullOrEmpty(ext)
                        ? Path.Combine(dir, "video")
                        : Path.Combine(dir, $"video{ext}");

                    // 첨부파일 크기 제한은 웹 대시보드에서 관리됨 (ADR-047)
                    var activeConfig = _videoConfig;

                    await _videoEncoder.EncodeAsync(frames, videoPath, activeConfig, token);

                    // 파일 크기 초과 시 CRF를 올려 최대 2회 재인코딩 (무한 루프 방지)
                    if (activeConfig.TargetMaxSizeBytes > 0 && File.Exists(videoPath))
                    {
                        // CRF 단계: 23 → 28 → 33
                        int[] crfSteps = { 28, 33 };
                        long currentSize = new FileInfo(videoPath).Length;
                        for (int attempt = 0; attempt < crfSteps.Length; attempt++)
                        {
                            if (currentSize <= activeConfig.TargetMaxSizeBytes)
                                break; // 크기 제한 이내 → 재인코딩 불필요

                            int nextCrf = crfSteps[attempt];
                            Debug.LogWarning(
                                $"[Rekon] 영상 파일 크기({currentSize / 1024.0 / 1024.0:F1} MB)가 " +
                                $"첨부파일 제한({activeConfig.TargetMaxSizeBytes / 1024.0 / 1024.0:F0} MB)을 초과합니다. " +
                                $"CRF {nextCrf}으로 재인코딩합니다. (시도 {attempt + 1}/2)");

                            var reencodeConfig = new VideoEncoderConfig
                            {
                                Width             = activeConfig.Width,
                                Height            = activeConfig.Height,
                                Fps               = activeConfig.Fps,
                                BitrateMbps       = activeConfig.BitrateMbps,
                                Crf               = nextCrf,
                                TargetMaxSizeBytes = activeConfig.TargetMaxSizeBytes,
                            };

                            // 원본 파일 보호를 위해 임시 경로에 인코딩 후 성공 시 교체
                            string tempPath = videoPath + ".tmp";
                            try
                            {
                                await _videoEncoder.EncodeAsync(frames, tempPath, reencodeConfig, token);
                            }
                            catch (OperationCanceledException)
                            {
                                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                                throw;
                            }
                            catch (Exception)
                            {
                                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                                throw;
                            }
                            if (File.Exists(tempPath))
                            {
                                long newSize = new FileInfo(tempPath).Length;
                                if (newSize > 0 && newSize < currentSize)
                                {
                                    File.Delete(videoPath);
                                    File.Move(tempPath, videoPath);
                                    currentSize = newSize; // 다음 반복 비교용으로 업데이트
                                }
                                else
                                {
                                    File.Delete(tempPath); // 더 커지거나 0이면 원본 유지
                                    break; // 더 이상 재인코딩 무의미
                                }
                            }
                        }

                        // 최종 크기 확인 로그
                        if (File.Exists(videoPath))
                        {
                            long finalSize = new FileInfo(videoPath).Length;
                            if (finalSize > activeConfig.TargetMaxSizeBytes)
                            {
                                Debug.LogWarning(
                                    $"[Rekon] 재인코딩 후에도 영상 파일 크기({finalSize / 1024.0 / 1024.0:F1} MB)가 " +
                                    $"첨부파일 제한({activeConfig.TargetMaxSizeBytes / 1024.0 / 1024.0:F0} MB)을 초과합니다. " +
                                    "Jira 업로드 시 거부될 수 있습니다.");
                            }
                        }
                    }

                    // 사전 할당 방식: 인코딩 후 ArrayPool.Return 불필요
                    // 링버퍼가 슬롯을 계속 소유하며 재사용합니다.
                    result.VideoPath = videoPath;
                }

                ReportProgress("video", 1.0f);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 영상 수집 실패: {ex.Message}");
                ReportProgress("video", 1.0f, ex.Message);
            }
        }

        private static string CreateCaptureDirectory(DateTime timestamp)
        {
            string tempBase = Path.Combine(
                Application.temporaryCachePath,
                "Rekon",
                timestamp.ToString("yyyyMMdd_HHmmss_fff"));

            Directory.CreateDirectory(tempBase);
            return tempBase;
        }

        private void ReportProgress(string stage, float progress, string errorMessage = null)
        {
            var evt = new CaptureProgressEvent(stage, progress, errorMessage);
            try
            {
                OnProgress?.Invoke(evt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] OnProgress 핸들러 오류: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 사용량 사전 체크
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 캡처 전 사용량 한도를 사전 체크합니다.
        /// 웹 대시보드 /api/usage 엔드포인트를 3초 타임아웃으로 호출합니다.
        ///
        /// 반환값:
        ///   - null: API 실패 / 타임아웃 → fail-open (캡처 허용)
        ///   - { Allowed = true }: 여유 있음 → 캡처 허용
        ///   - { Allowed = false, Reason = "daily"|"monthly" }: 한도 초과 → 캡처 차단
        /// </summary>
        private async Task<UsageCheckResult> CheckUsageLimitAsync()
        {
            try
            {
                var tokenStore = ActiveTokenStore;
                if (tokenStore == null)
                {
                    // 토큰 스토어 자체가 없음 → fail-open
                    Debug.Log("[Rekon] 사용량 사전 체크: 토큰 스토어 없음. 캡처를 계속 진행합니다.");
                    return null;
                }
                string accessToken = tokenStore.LoadSupabase();
                if (string.IsNullOrEmpty(accessToken))
                {
                    // 토큰 없음 → fail-open
                    Debug.Log("[Rekon] 사용량 사전 체크: 토큰 없음. 캡처를 계속 진행합니다.");
                    return null;
                }

                // URL: {WEB_DASHBOARD_URL}/api/usage?workspace_id={workspaceId}
                string workspaceId = _settings.tenantId;
                string baseUrl = RekonSettings.WEB_DASHBOARD_URL.TrimEnd('/');
                string url = $"{baseUrl}/api/usage?workspace_id={Uri.EscapeDataString(workspaceId)}";

                // IRekonHttpClient로 GET 요청 (null이면 UnityHttpClient 사용)
                var client = _httpClient ?? new UnityHttpClient();
                var headers = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "Authorization", $"Bearer {accessToken}" }
                };

                // 3초 + 여유 1초 대기 (빠른 실패 — 네트워크 단절 대응)
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(4));
                string responseJson;
                try
                {
                    var response = await client.GetAsync(url, headers, cts.Token);
                    if (!response.IsSuccess)
                    {
                        // fail-open: 4xx, 5xx 모두 null로 처리
                        Debug.Log($"[Rekon] 사용량 사전 체크 응답 오류 " +
                                  $"(fail-open, HTTP {response.StatusCode})");
                        return null;
                    }
                    responseJson = response.Body;
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    Debug.Log("[Rekon] 사용량 사전 체크 타임아웃 (fail-open). 캡처를 계속 진행합니다.");
                    return null;
                }
                catch (Exception ex)
                {
                    Debug.Log($"[Rekon] 사용량 사전 체크 예외 (fail-open): {ex.Message}");
                    return null;
                }

                if (string.IsNullOrEmpty(responseJson))
                {
                    // fail-open
                    return null;
                }

                // JSON 파싱
                UsageInfoResponse usage;
                try
                {
                    usage = JsonUtility.FromJson<UsageInfoResponse>(responseJson);
                }
                catch (Exception ex)
                {
                    Debug.Log($"[Rekon] 사용량 사전 체크 JSON 파싱 실패 (fail-open): {ex.Message}");
                    return null;
                }

                if (usage == null)
                    return null;

                if (usage.monthly_exceeded)
                {
                    return new UsageCheckResult
                    {
                        Allowed = false,
                        Reason = "monthly"
                    };
                }

                return new UsageCheckResult { Allowed = true };
            }
            catch (Exception ex)
            {
                // fail-open: 예외 시 캡처 허용
                Debug.Log($"[Rekon] 사용량 사전 체크 예외 (fail-open): {ex.Message}");
                return null;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 사용량 체크 전용 내부 데이터 클래스
        // ──────────────────────────────────────────────────────────────

        /// <summary>사용량 사전 체크 결과</summary>
        private class UsageCheckResult
        {
            /// <summary>캡처 허용 여부. false이면 Reason을 참조하세요.</summary>
            public bool Allowed;

            /// <summary>한도 초과 유형: "monthly"</summary>
            public string Reason;
        }

        /// <summary>/api/usage 응답 모델</summary>
        [Serializable]
        private class UsageInfoResponse
        {
            public string plan;
            public int monthly_count;
            public int monthly_limit;
            /// <summary>월간 한도 초과 여부</summary>
            public bool monthly_exceeded;
        }
    }
}
