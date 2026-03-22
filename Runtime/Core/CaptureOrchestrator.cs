using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

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
        // 생성자 주입 토큰 스토어 (null 허용)
        private readonly SessionTokenStore _tokenStore;
        // 런타임 바인딩 토큰 스토어 — BindTokenStore() 호출 시 설정되며, _tokenStore보다 우선합니다.
        private SessionTokenStore _runtimeTokenStore;

        // 실제 사용할 토큰 스토어: 런타임 바인딩 우선, 없으면 생성자 주입 사용
        private SessionTokenStore ActiveTokenStore => _runtimeTokenStore ?? _tokenStore;

        private HotkeyManager _hotkeyManager;
        private SilentSubmitManager _silentSubmitManager;
        private bool _disposed;
        // 원자적 플래그: 0 = 대기, 1 = 캡처 중
        private int _isCapturingFlag;

        /// <summary>캡처 진행 상황 이벤트</summary>
        public event Action<CaptureProgressEvent> OnProgress;

        /// <summary>캡처 완료 시 발행되는 이벤트 (CaptureResult를 인자로 받음)</summary>
        public event Action<CaptureResult> OnCaptureCompleted;

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
            SessionTokenStore tokenStore = null)
        {
            _screenshotCapturer = screenshotCapturer ?? throw new ArgumentNullException(nameof(screenshotCapturer));
            _logCollector = logCollector ?? throw new ArgumentNullException(nameof(logCollector));
            _logSerializer = logSerializer ?? throw new ArgumentNullException(nameof(logSerializer));
            _stateCollector = stateCollector ?? throw new ArgumentNullException(nameof(stateCollector));
            _frameBuffer = frameBuffer; // null 허용 (영상 비활성 시)
            _videoEncoder = videoEncoder; // null 허용
            _videoConfig = videoConfig; // null 허용
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tokenStore = tokenStore; // null 허용 (미연동 시 사전 체크 건너뜀)
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
        /// SilentSubmitManager를 바인딩합니다.
        /// 캡처 시작 전 제출 진행 여부를 확인하는 데 사용됩니다.
        /// </summary>
        public void BindSilentSubmitManager(SilentSubmitManager manager)
        {
            _silentSubmitManager = manager;
            Debug.Log("[Rekon] CaptureOrchestrator: SilentSubmitManager 바인딩 완료");
        }

        /// <summary>
        /// HotkeyManager를 등록하고 OnCaptureTrigger 이벤트를 구독합니다.
        /// </summary>
        public void BindHotkeyManager(HotkeyManager hotkeyManager)
        {
            if (_hotkeyManager != null)
                _hotkeyManager.OnCaptureTrigger -= OnTrigger;

            _hotkeyManager = hotkeyManager;

            if (_hotkeyManager != null)
                _hotkeyManager.OnCaptureTrigger += OnTrigger;
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

            // 임시 디렉토리 생성
            string captureDir = CreateCaptureDirectory(result.Timestamp);

            try
            {
                // 병렬 수집
                var screenshotTask = CaptureScreenshotAsync(captureDir, result, cts.Token);
                var logsTask = CaptureLogsAsync(captureDir, result, cts.Token);
                var stateTask = CaptureStateAsync(captureDir, result, cts.Token);
                var videoTask = CaptureVideoAsync(captureDir, result, cts.Token);

                await Task.WhenAll(screenshotTask, logsTask, stateTask, videoTask);

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
                _hotkeyManager.OnCaptureTrigger -= OnTrigger;
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

        private async Task CaptureLogsAsync(string dir, CaptureResult result, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                LogEntry[] entries = _logCollector.GetEntries();
                string path = Path.Combine(dir, "logs.txt");
                await _logSerializer.SaveAsync(entries, path);
                result.LogsPath = path;

                ReportProgress("logs", 0.50f);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 로그 수집 실패: {ex.Message}");
                ReportProgress("logs", 0.50f, ex.Message);
            }
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
                if (!_settings.videoEnabled || _frameBuffer == null || _videoEncoder == null)
                {
                    ReportProgress("video", 1.0f);
                    return;
                }

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
                string accessToken = _tokenStore.LoadSupabase();
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

                var tcs = new TaskCompletionSource<string>();
                var syncContext = System.Threading.SynchronizationContext.Current;

                void RunOnMainThread(Action action)
                {
                    if (syncContext != null)
                        syncContext.Post(_ => action(), null);
                    else
                        action();
                }

                RunOnMainThread(async () =>
                {
                    UnityWebRequest request = null;
                    bool isDisposed = false;

                    try
                    {
                        request = UnityWebRequest.Get(url);
                        request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                        // 3초 타임아웃 (빠른 실패 — 네트워크 단절 대응)
                        request.timeout = 3;

                        var operation = request.SendWebRequest();
                        while (!operation.isDone)
                            await Task.Yield();

                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            tcs.TrySetResult(request.downloadHandler.text);
                        }
                        else
                        {
                            // fail-open: 네트워크 오류, 4xx, 5xx 모두 null로 처리
                            Debug.Log($"[Rekon] 사용량 사전 체크 응답 오류 " +
                                      $"(fail-open, HTTP {request.responseCode}): {request.error}");
                            tcs.TrySetResult(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"[Rekon] 사용량 사전 체크 예외 (fail-open): {ex.Message}");
                        tcs.TrySetResult(null);
                    }
                    finally
                    {
                        isDisposed = true;
                        request?.Dispose();
                    }
                });

                // 3초 + 여유 1초 대기 (UnityWebRequest.timeout이 이미 3초이므로 4초 초과 시 강제 fail-open)
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(4));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask || !tcs.Task.IsCompleted)
                {
                    Debug.Log("[Rekon] 사용량 사전 체크 타임아웃 (fail-open). 캡처를 계속 진행합니다.");
                    return null;
                }

                string responseJson = tcs.Task.Result;
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
