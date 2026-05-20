using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 핫키 한 번으로 캡처 → 자동 제목 생성 → 번들 저장 → 웹 API 전송까지
    /// 자동 처리하는 Silent Submit 매니저.
    ///
    /// 플로우:
    ///   1. CaptureOrchestrator.OnCaptureCompleted 이벤트 수신
    ///   2. 자동 제목 생성: [접두어] [씬이름] [타임스탬프]
    ///   3. 메타데이터 4종 수집 (Unity버전, 씬이름, 플랫폼, 해상도)
    ///   4. BundleWriter로 번들 저장
    ///   5. ReportSubmitService로 웹 API 전송
    ///   6. 성공/실패 결과 처리
    /// </summary>
    public class SilentSubmitManager : IDisposable
    {
        // ─── 타임스탬프 포맷 ──────────────────────────────────────────────────

        private static readonly string[] TimestampFormats =
        {
            "yyMMdd_HHmm",       // 0: 기본
            "yyyy-MM-dd HH:mm",  // 1: ISO 스타일
            "MMdd_HHmmss",       // 2: 짧은 형식
        };

        // ─── 의존성 ──────────────────────────────────────────────────────────

        private readonly RekonSettings _settings;
        private readonly BundleWriter _bundleWriter;
        private readonly ReportSubmitService _submitService;
        private readonly SessionTokenStore _tokenStore;
        private PendingUploadManager _pendingUploadManager;
        private ICaptureOrchestrator _orchestrator;
        private bool _disposed;
        // 원자적 플래그: 0 = 대기, 1 = 제출 중
        private int _isSubmittingFlag;

        // ─── 프로퍼티 ─────────────────────────────────────────────────────────

        /// <summary>현재 제출이 진행 중인지 여부 (원자적 읽기)</summary>
        public bool IsSubmitting => Interlocked.CompareExchange(ref _isSubmittingFlag, 0, 0) == 1;

        // ─── 이벤트 ──────────────────────────────────────────────────────────

        /// <summary>Silent Submit 완료 시 발행되는 이벤트. (성공 여부, 리포트 ID 또는 에러 메시지)</summary>
        public event Action<bool, string> OnSubmitCompleted;

        // ─── 생성자 ──────────────────────────────────────────────────────────

        /// <summary>
        /// SilentSubmitManager를 초기화합니다.
        /// </summary>
        /// <param name="settings">Rekon 설정</param>
        /// <param name="bundleWriter">번들 기록기</param>
        /// <param name="tokenStore">세션 토큰 저장소 (DI)</param>
        /// <param name="submitService">리포트 제출 서비스 (null 허용: 미로그인 시)</param>
        public SilentSubmitManager(
            RekonSettings settings,
            BundleWriter bundleWriter,
            SessionTokenStore tokenStore,
            ReportSubmitService submitService = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _bundleWriter = bundleWriter ?? throw new ArgumentNullException(nameof(bundleWriter));
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _submitService = submitService; // null 허용 (미로그인/오프라인 시 로컬 저장만)
        }

        // ─── 공개 메서드 ──────────────────────────────────────────────────────

        /// <summary>
        /// PendingUploadManager를 바인딩합니다.
        /// 전송 실패/미로그인 시 pending 큐에 자동 등록됩니다.
        /// </summary>
        public void BindPendingUploadManager(PendingUploadManager pendingUploadManager)
        {
            _pendingUploadManager = pendingUploadManager;
            Debug.Log("[Rekon] SilentSubmitManager: PendingUploadManager 바인딩 완료");
        }

        /// <summary>
        /// CaptureOrchestrator의 OnCaptureCompleted 이벤트를 구독합니다.
        /// </summary>
        public void BindOrchestrator(ICaptureOrchestrator orchestrator)
        {
            // 기존 바인딩 해제
            if (_orchestrator != null)
                _orchestrator.OnCaptureCompleted -= HandleCaptureCompleted;

            _orchestrator = orchestrator;

            if (_orchestrator != null)
                _orchestrator.OnCaptureCompleted += HandleCaptureCompleted;

            Debug.Log("[Rekon] SilentSubmitManager: 오케스트레이터 바인딩 완료");
        }

        /// <summary>
        /// CaptureResult를 받아 Silent Submit을 수행합니다.
        /// 외부에서 직접 호출할 수도 있습니다.
        /// </summary>
        public async Task SubmitAsync(CaptureResult captureResult)
        {
            if (captureResult == null)
            {
                Debug.LogWarning("[Rekon] SilentSubmit: CaptureResult가 null입니다.");
                return;
            }

            if (!captureResult.IsPartialSuccess)
            {
                Debug.LogWarning("[Rekon] SilentSubmit: 유효한 아티팩트가 없습니다. 건너뜁니다.");
                return;
            }

            // 원자적 CAS: 0(대기) → 1(제출 중) 으로 교체. 이미 1이면 중복 진입 차단
            if (Interlocked.CompareExchange(ref _isSubmittingFlag, 1, 0) != 0)
            {
                Debug.LogWarning("[Rekon] SilentSubmit: 이미 제출이 진행 중입니다.");
                return;
            }

            try
            {
                // 1. 자동 제목 생성
                string title = GenerateTitle(captureResult.Timestamp);
                Debug.Log($"[Rekon] SilentSubmit: 자동 생성 제목 = \"{title}\"");

                // 2. 메타데이터 수집
                var metadata = CollectMetadata();
                Debug.Log($"[Rekon] SilentSubmit: 메타데이터 {metadata.Count}개 수집 완료");

                // 3. BundleWriter로 번들 저장
                BundleManifest manifest = await _bundleWriter.WriteAsync(captureResult);

                // 매니페스트에 제목, 메타데이터 세팅
                manifest.title = title;
                manifest.metadata.Clear();
                foreach (var kvp in metadata)
                {
                    manifest.metadata.Add(new MetadataEntry(kvp.Key, kvp.Value));
                }

                // manifest.json을 디스크에 다시 저장 (WriteAsync 시점에는 title/metadata가 비어 있었으므로)
                await _bundleWriter.RewriteManifestAsync(manifest);

                Debug.Log($"[Rekon] SilentSubmit: 번들 저장 완료 (id={manifest.id})");

                // 4. 웹 API 전송
                if (_submitService != null)
                {
                    await SubmitToWebAsync(manifest, captureResult);
                }
                else
                {
                    Debug.Log("[Rekon] SilentSubmit: ReportSubmitService 미설정. 로컬 저장만 완료합니다.");
                    OnSubmitCompleted?.Invoke(true, $"로컬 저장 완료: {manifest.id}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] SilentSubmit 실패: {ex.Message}\n{ex.StackTrace}");
                OnSubmitCompleted?.Invoke(false, ex.Message);
            }
            finally
            {
                // 원자적으로 플래그 해제: 1(제출 중) → 0(대기)
                Interlocked.Exchange(ref _isSubmittingFlag, 0);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_orchestrator != null)
                _orchestrator.OnCaptureCompleted -= HandleCaptureCompleted;
        }

        // ─── 내부 구현 ───────────────────────────────────────────────────────

        /// <summary>
        /// CaptureOrchestrator.OnCaptureCompleted 이벤트 핸들러.
        /// </summary>
        private void HandleCaptureCompleted(CaptureResult result)
        {
            if (_disposed) return;

            // 비동기 제출 시작 (fire-and-forget, 에러는 내부에서 처리)
            _ = SubmitAsync(result);
        }

        /// <summary>
        /// 설정 기반 자동 제목을 생성합니다.
        /// 형식: [접두어] 씬이름 타임스탬프
        /// </summary>
        internal string GenerateTitle(DateTime timestamp)
        {
            string prefix = string.IsNullOrEmpty(_settings.reportTitlePrefix)
                ? "Bug"
                : _settings.reportTitlePrefix;

            string sceneName = GetActiveSceneName();
            string formattedTimestamp = FormatTimestamp(timestamp, _settings.timestampFormat);

            return $"[{prefix}] {sceneName} {formattedTimestamp}";
        }

        /// <summary>
        /// Settings에서 지정된 형식으로 타임스탬프를 포맷합니다.
        /// </summary>
        internal static string FormatTimestamp(DateTime timestamp, int formatIndex)
        {
            if (formatIndex < 0 || formatIndex >= TimestampFormats.Length)
                formatIndex = 0;

            return timestamp.ToString(TimestampFormats[formatIndex]);
        }

        /// <summary>
        /// Settings 토글에 따라 메타데이터를 수집합니다.
        /// </summary>
        internal Dictionary<string, string> CollectMetadata()
        {
            var metadata = new Dictionary<string, string>();

            if (_settings.collectUnityVersion)
                metadata["unity_version"] = Application.unityVersion;

            if (_settings.collectSceneName)
                metadata["scene_name"] = GetActiveSceneName();

            if (_settings.collectPlatform)
                metadata["platform"] = Application.platform.ToString();

            if (_settings.collectResolution)
                metadata["resolution"] = $"{Screen.width}x{Screen.height}";

            return metadata;
        }

        /// <summary>
        /// 웹 API로 리포트를 전송합니다.
        /// </summary>
        private async Task SubmitToWebAsync(BundleManifest manifest, CaptureResult captureResult)
        {
            try
            {
                // 파일 첨부 목록 구성
                var files = new List<FileAttachment>();

                // 단수 스크린샷 (레거시 경로 기반)
#pragma warning disable CS0618 // Obsolete 멤버 사용 (하위 호환 유지)
                if (!string.IsNullOrEmpty(captureResult.ScreenshotPath) && File.Exists(captureResult.ScreenshotPath))
                {
                    files.Add(new FileAttachment
                    {
                        FileName = Path.GetFileName(captureResult.ScreenshotPath),
                        Data = await ReadFileAsync(captureResult.ScreenshotPath),
                        FileType = "screenshot"
                    });
                }
#pragma warning restore CS0618

                // 복수 스크린샷 (스크린샷 핫키 큐 드레인)
                // manifest.artifacts 기반으로 순회하여 captureResult 인덱스 불일치 문제를 방지합니다.
                // ManifestGenerator가 빈 엔트리를 건너뛰더라도 manifest에 실제 저장된 항목만 업로드합니다.
                {
                    string bundleDir = BundleWriter.GetBundleDirectory(manifest.id);
                    foreach (var artifact in manifest.artifacts)
                    {
                        if (artifact.type == BundleArtifactType.Screenshot
                            && artifact.file_name.StartsWith("screenshot_"))
                        {
                            string screenshotPath = Path.Combine(bundleDir, artifact.file_name);
                            if (File.Exists(screenshotPath))
                            {
                                files.Add(new FileAttachment
                                {
                                    FileName = artifact.file_name,
                                    Data = await ReadFileAsync(screenshotPath),
                                    FileType = "screenshot"
                                });
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(captureResult.LogsPath) && File.Exists(captureResult.LogsPath))
                {
                    files.Add(new FileAttachment
                    {
                        FileName = Path.GetFileName(captureResult.LogsPath),
                        Data = await ReadFileAsync(captureResult.LogsPath),
                        FileType = "log"
                    });
                }

                if (!string.IsNullOrEmpty(captureResult.StatePath) && File.Exists(captureResult.StatePath))
                {
                    files.Add(new FileAttachment
                    {
                        FileName = Path.GetFileName(captureResult.StatePath),
                        Data = await ReadFileAsync(captureResult.StatePath),
                        FileType = "state"
                    });
                }

                if (!string.IsNullOrEmpty(captureResult.VideoPath) && File.Exists(captureResult.VideoPath))
                {
                    files.Add(new FileAttachment
                    {
                        FileName = Path.GetFileName(captureResult.VideoPath),
                        Data = await ReadFileAsync(captureResult.VideoPath),
                        FileType = "video"
                    });
                }

                if (files.Count == 0)
                {
                    Debug.LogWarning("[Rekon] SilentSubmit: 전송할 파일이 없습니다. 로컬 저장만 완료합니다.");
                    OnSubmitCompleted?.Invoke(true, $"로컬 저장 완료 (파일 없음): {manifest.id}");
                    return;
                }

                // AccessToken 확인
                // SessionTokenStore는 생성자에서 DI로 주입받은 인스턴스 재사용
                string accessToken = null;
                try
                {
                    accessToken = _tokenStore.LoadSupabase();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Rekon] SilentSubmit: 토큰 로드 실패 (pending 큐로 폴백): {ex.Message}");

                    // 토큰 로드 실패 시 pending 큐에 등록
                    if (_pendingUploadManager != null)
                    {
                        await _pendingUploadManager.EnqueueAsync(manifest);
                        OnSubmitCompleted?.Invoke(true, $"로컬 저장 완료 (토큰 오류): {manifest.id}");
                    }
                    else
                    {
                        OnSubmitCompleted?.Invoke(true, $"로컬 저장 완료 (토큰 오류): {manifest.id}");
                    }
                    return;
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    Debug.Log("[Rekon] SilentSubmit: 로그인되지 않았습니다. pending 큐에 등록합니다.");

                    // pending 큐에 등록 (로그인 시 자동 업로드)
                    if (_pendingUploadManager != null)
                    {
                        await _pendingUploadManager.EnqueueAsync(manifest);
                        OnSubmitCompleted?.Invoke(true, $"로컬 저장 완료 (미로그인): {manifest.id}");
                    }
                    else
                    {
                        OnSubmitCompleted?.Invoke(true, $"로컬 저장 완료 (미로그인): {manifest.id}");
                    }
                    return;
                }

                // WorkspaceId(tenantId) 검증 — 비어있으면 pending 큐에 등록 후 중단
                // 원인: 에셋 미저장 상태로 Play Mode 진입 시 tenantId가 빈 문자열일 수 있음
                string workspaceId = _settings.tenantId;
                if (string.IsNullOrEmpty(workspaceId))
                {
                    Debug.LogWarning("[Rekon] SilentSubmit: WorkspaceId(tenantId)가 비어있습니다. " +
                                     "Settings에 워크스페이스가 연동되어 있는지 확인하세요. pending 큐에 등록합니다.");

                    if (_pendingUploadManager != null)
                    {
                        await _pendingUploadManager.EnqueueAsync(manifest);
                        OnSubmitCompleted?.Invoke(true, $"로컬 저장 완료 (WorkspaceId 없음): {manifest.id}");
                    }
                    else
                    {
                        OnSubmitCompleted?.Invoke(true, $"로컬 저장 완료 (WorkspaceId 없음): {manifest.id}");
                    }
                    return;
                }

                var request = new ReportSubmitRequest
                {
                    AccessToken = accessToken,
                    WorkspaceId = workspaceId,
                    Title = manifest.title,
                    Description = BuildDescription(manifest),
                    Files = files,
                    PerformanceTimeline = captureResult.PerformanceTimeline,
                    ReplayMetadata = captureResult.ReplayMetadata
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var result = await _submitService.SubmitReportAsync(request, cancellationToken: cts.Token);

                if (result.Success)
                {
                    manifest.state = BundleState.Submitted;
                    manifest.registered_at = DateTime.UtcNow.ToString("O");

                    Debug.Log($"[Rekon] SilentSubmit: 웹 전송 성공! ReportId={result.ReportId}");
                    OnSubmitCompleted?.Invoke(true, result.ReportId);
                }
                else if (result.IsUsageLimitExceeded)
                {
                    // 429 사용량 초과: pending 큐 등록 없이 사용자 안내만 수행
                    manifest.state = BundleState.Failed;
                    Debug.LogWarning($"[Rekon] SilentSubmit: 사용량 한도 초과 " +
                                     $"(reason={result.UsageLimitReason}, upgradeUrl={result.UpgradeUrl})");

                    // 이벤트 페이로드: "USAGE_LIMIT:<reason>:<monthly_limit>:<upgradeUrl>"
                    string payload = $"USAGE_LIMIT:{result.UsageLimitReason}:{result.MonthlyLimit}:{result.UpgradeUrl ?? ""}";
                    OnSubmitCompleted?.Invoke(false, payload);
                }
                else
                {
                    manifest.state = BundleState.Failed;
                    Debug.LogWarning($"[Rekon] SilentSubmit: 웹 전송 실패: {result.ErrorMessage}");

                    // pending 큐에 등록 (재시도 스케줄)
                    if (_pendingUploadManager != null)
                    {
                        await _pendingUploadManager.EnqueueAsync(manifest);
                        Debug.Log($"[Rekon] SilentSubmit: pending 큐에 등록 완료 (bundleId={manifest.id})");
                    }

                    OnSubmitCompleted?.Invoke(false, result.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                manifest.state = BundleState.Failed;
                Debug.LogWarning("[Rekon] SilentSubmit: 웹 전송 타임아웃 (60초 초과)");

                // pending 큐에 등록 (재시도 스케줄)
                if (_pendingUploadManager != null)
                {
                    try { await _pendingUploadManager.EnqueueAsync(manifest); }
                    catch { /* 무시 */ }
                }

                OnSubmitCompleted?.Invoke(false, "전송 타임아웃");
            }
            catch (Exception ex)
            {
                manifest.state = BundleState.Failed;
                Debug.LogError($"[Rekon] SilentSubmit: 웹 전송 중 오류: {ex.Message}");

                // pending 큐에 등록 (재시도 스케줄)
                if (_pendingUploadManager != null)
                {
                    try { await _pendingUploadManager.EnqueueAsync(manifest); }
                    catch { /* 무시 */ }
                }

                OnSubmitCompleted?.Invoke(false, ex.Message);
            }
        }

        /// <summary>
        /// 메타데이터를 기반으로 리포트 설명을 구성합니다.
        /// </summary>
        private static string BuildDescription(BundleManifest manifest)
        {
            if (manifest.metadata == null || manifest.metadata.Count == 0)
                return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## 환경 정보");
            foreach (var entry in manifest.metadata)
            {
                sb.AppendLine($"- **{entry.key}**: {entry.value}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 현재 활성 씬 이름을 반환합니다.
        /// </summary>
        private static string GetActiveSceneName()
        {
            try
            {
                var scene = SceneManager.GetActiveScene();
                return scene.IsValid() ? scene.name : "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// 파일을 비동기로 읽어 바이트 배열로 반환합니다.
        /// </summary>
        private static async Task<byte[]> ReadFileAsync(string path)
        {
            return await Task.Run(() => File.ReadAllBytes(path));
        }
    }
}
