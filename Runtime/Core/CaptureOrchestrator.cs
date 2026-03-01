using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
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
        private readonly BugOneTouchSettings _settings;

        private HotkeyManager _hotkeyManager;
        private bool _disposed;
        private bool _isCapturing;

        /// <summary>캡처 진행 상황 이벤트</summary>
        public event Action<CaptureProgressEvent> OnProgress;

        /// <summary>캡처 완료 시 발행되는 이벤트 (CaptureResult를 인자로 받음)</summary>
        public event Action<CaptureResult> OnCaptureCompleted;

        /// <summary>
        /// CaptureOrchestrator를 초기화합니다.
        /// </summary>
        public CaptureOrchestrator(
            IScreenshotCapturer screenshotCapturer,
            ILogCollector logCollector,
            LogSerializer logSerializer,
            IStateSnapshotCollector stateCollector,
            FrameRingBuffer frameBuffer,
            IVideoEncoder videoEncoder,
            VideoEncoderConfig videoConfig,
            BugOneTouchSettings settings)
        {
            _screenshotCapturer = screenshotCapturer ?? throw new ArgumentNullException(nameof(screenshotCapturer));
            _logCollector = logCollector ?? throw new ArgumentNullException(nameof(logCollector));
            _logSerializer = logSerializer ?? throw new ArgumentNullException(nameof(logSerializer));
            _stateCollector = stateCollector ?? throw new ArgumentNullException(nameof(stateCollector));
            _frameBuffer = frameBuffer; // null 허용 (영상 비활성 시)
            _videoEncoder = videoEncoder; // null 허용
            _videoConfig = videoConfig; // null 허용
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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
            if (_isCapturing)
            {
                Debug.LogWarning("[BugOneTouch] 이미 캡처가 진행 중입니다.");
                return null;
            }

            _isCapturing = true;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
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
                Debug.LogWarning("[BugOneTouch] 캡처 타임아웃 (5초 초과). 수집된 아티팩트만 반환합니다.");
                ReportProgress("complete", 1.0f, "타임아웃");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 캡처 중 예기치 않은 오류: {ex.Message}");
            }
            finally
            {
                _isCapturing = false;
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
                Debug.LogError($"[BugOneTouch] 스크린샷 캡처 실패: {ex.Message}");
                ReportProgress("screenshot", 0.25f, ex.Message);
            }
        }

        private async Task CaptureLogsAsync(string dir, CaptureResult result, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                LogEntry[] entries = _logCollector.GetEntries();
                string path = Path.Combine(dir, "logs.zip");
                await _logSerializer.SaveAsync(entries, path);
                result.LogsPath = path;

                ReportProgress("logs", 0.50f);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 로그 수집 실패: {ex.Message}");
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

                ReportProgress("state", 0.75f);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 상태 수집 실패: {ex.Message}");
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
                    string videoDir = Path.Combine(dir, "video");
                    await _videoEncoder.EncodeAsync(frames, videoDir, _videoConfig);
                    result.VideoPath = videoDir;
                }

                ReportProgress("video", 1.0f);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 영상 수집 실패: {ex.Message}");
                ReportProgress("video", 1.0f, ex.Message);
            }
        }

        private static string CreateCaptureDirectory(DateTime timestamp)
        {
            string tempBase = Path.Combine(
                Application.temporaryCachePath,
                "BugOneTouch",
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
                Debug.LogWarning($"[BugOneTouch] OnProgress 핸들러 오류: {ex.Message}");
            }
        }
    }
}
