using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 오프라인/미로그인 시 전송 실패한 번들을 pending 큐에 등록하고,
    /// 로그인/네트워크 복구 시 자동 업로드하는 매니저.
    ///
    /// pending 큐 관리:
    ///   - {persistentDataPath}/BugOneTouch/pending/ 폴더 사용
    ///   - 각 항목은 {bundleId}.pending.json 파일
    ///   - 번들 자체는 BundleWriter가 이미 로컬에 저장
    ///
    /// 자동 업로드 트리거:
    ///   1. 앱 시작 시: Bootstrap에서 pending 스캔 → 로그인 상태면 순차 업로드
    ///   2. 로그인 성공 시: SessionTokenStore.OnTokenChanged 이벤트 구독
    ///
    /// 재시도 정책:
    ///   - 최대 5회, 지수 백오프: 10초, 30초, 1분, 5분, 10분
    ///   - 5회 초과 시 포기 (로컬 유지, 사용자 수동 업로드)
    /// </summary>
    public class PendingUploadManager : IDisposable
    {
        // ─── 상수 ──────────────────────────────────────────────────────────

        /// <summary>최대 재시도 횟수</summary>
        public const int MaxRetryCount = 5;

        /// <summary>지수 백오프 간격 (초)</summary>
        private static readonly int[] RetryDelaysSeconds = { 10, 30, 60, 300, 600 };

        private const string PendingFolderName = "pending";
        private const string PendingFileSuffix = ".pending.json";

        // ─── 의존성 ──────────────────────────────────────────────────────────

        private readonly BundleRepository _bundleRepository;
        private readonly ReportSubmitService _submitService;
        private readonly SessionTokenStore _tokenStore;
        private readonly BugOneTouchSettings _settings;

        private int _isProcessing; // 0=false, 1=true (Interlocked용)
        private bool _disposed;
        private CancellationTokenSource _cts;

        // ─── 이벤트 ──────────────────────────────────────────────────────────

        /// <summary>
        /// pending 번들 업로드 완료 시 발행. (성공 여부, bundleId, 메시지)
        /// </summary>
        public event Action<bool, string, string> OnPendingUploadCompleted;

        // ─── pending 큐 항목 모델 ──────────────────────────────────────────

        /// <summary>
        /// pending 큐 파일에 직렬화되는 데이터 모델.
        /// </summary>
        [Serializable]
        public class PendingEntry
        {
            public string bundleId;
            public string title;
            public string createdAt;
            public int retryCount;
        }

        // ─── 생성자 ──────────────────────────────────────────────────────────

        /// <summary>
        /// PendingUploadManager를 초기화합니다.
        /// </summary>
        /// <param name="bundleRepository">번들 저장소</param>
        /// <param name="submitService">리포트 제출 서비스 (null 허용: Supabase 미설정 시)</param>
        /// <param name="tokenStore">세션 토큰 저장소</param>
        /// <param name="settings">BugOneTouch 설정</param>
        public PendingUploadManager(
            BundleRepository bundleRepository,
            ReportSubmitService submitService,
            SessionTokenStore tokenStore,
            BugOneTouchSettings settings)
        {
            _bundleRepository = bundleRepository ?? throw new ArgumentNullException(nameof(bundleRepository));
            _submitService = submitService; // null 허용
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // pending 폴더 생성
            EnsurePendingDirectory();

            // 토큰 변경 이벤트 구독 (로그인 성공 시 자동 업로드)
            _tokenStore.OnTokenChanged += HandleTokenChanged;

            Debug.Log("[BugOneTouch] PendingUploadManager 초기화 완료");
        }

        // ─── 공개 메서드 ──────────────────────────────────────────────────────

        /// <summary>
        /// 번들을 pending 큐에 등록합니다.
        /// 번들 상태를 Pending으로 변경하고, pending JSON 파일을 생성합니다.
        /// </summary>
        /// <param name="manifest">등록할 번들 매니페스트</param>
        public async Task EnqueueAsync(BundleManifest manifest)
        {
            if (manifest == null)
            {
                Debug.LogWarning("[BugOneTouch] PendingUpload: null 매니페스트를 enqueue 시도");
                return;
            }

            try
            {
                // 번들 상태를 Pending으로 변경
                await _bundleRepository.UpdateStateAsync(manifest.id, BundleState.Pending);

                // pending JSON 파일 생성
                var entry = new PendingEntry
                {
                    bundleId = manifest.id,
                    title = manifest.title ?? "",
                    createdAt = DateTime.UtcNow.ToString("O"),
                    retryCount = 0
                };

                string pendingPath = GetPendingFilePath(manifest.id);
                string json = JsonUtility.ToJson(entry, prettyPrint: true);
                await Task.Run(() =>
                {
                    string tempPath = pendingPath + ".tmp";
                    File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                    if (File.Exists(pendingPath))
                        File.Delete(pendingPath);
                    File.Move(tempPath, pendingPath);
                });

                Debug.Log($"[BugOneTouch] PendingUpload: 큐에 등록 완료 (bundleId={manifest.id})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] PendingUpload: 큐 등록 실패 ({manifest.id}): {ex.Message}");
            }
        }

        /// <summary>
        /// pending 폴더를 스캔하여 모든 pending 항목 목록을 반환합니다.
        /// </summary>
        /// <returns>PendingEntry 목록. 비어있으면 빈 리스트.</returns>
        public async Task<List<PendingEntry>> GetPendingEntriesAsync()
        {
            return await Task.Run(() =>
            {
                var entries = new List<PendingEntry>();
                string pendingDir = GetPendingDirectory();

                if (!Directory.Exists(pendingDir))
                    return entries;

                foreach (string filePath in Directory.GetFiles(pendingDir, $"*{PendingFileSuffix}"))
                {
                    try
                    {
                        string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                        var entry = JsonUtility.FromJson<PendingEntry>(json);
                        if (entry != null && !string.IsNullOrEmpty(entry.bundleId))
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[BugOneTouch] PendingUpload: pending 파일 파싱 실패 ({filePath}): {ex.Message}");
                    }
                }

                // 생성 시각 오름차순 정렬 (오래된 것 먼저)
                entries.Sort((a, b) => string.Compare(a.createdAt, b.createdAt, StringComparison.Ordinal));

                return entries;
            });
        }

        /// <summary>
        /// 모든 pending 항목을 순차적으로 업로드합니다.
        /// 로그인 상태가 아니면 즉시 반환합니다.
        /// </summary>
        /// <returns>성공한 업로드 수</returns>
        public async Task<int> ProcessAllPendingAsync()
        {
            if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
            {
                Debug.Log("[BugOneTouch] PendingUpload: 이미 처리 중입니다.");
                return 0;
            }

            try
            {
                if (_submitService == null)
                {
                    Debug.Log("[BugOneTouch] PendingUpload: ReportSubmitService가 없어 처리를 건너뜁니다.");
                    return 0;
                }

                // 로그인 상태 확인
                string accessToken;
                try
                {
                    accessToken = _tokenStore.LoadSupabase();
                }
                catch
                {
                    accessToken = null;
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    Debug.Log("[BugOneTouch] PendingUpload: 미로그인 상태. pending 처리를 건너뜁니다.");
                    return 0;
                }

                _cts = new CancellationTokenSource();
                int successCount = 0;

                try
                {
                    var entries = await GetPendingEntriesAsync();

                    if (entries.Count == 0)
                    {
                        Debug.Log("[BugOneTouch] PendingUpload: pending 항목이 없습니다.");
                        return 0;
                    }

                    Debug.Log($"[BugOneTouch] PendingUpload: {entries.Count}개 pending 항목 처리 시작");

                    foreach (var entry in entries)
                    {
                        if (_cts.IsCancellationRequested)
                            break;

                        bool success = await ProcessSingleEntryAsync(entry, accessToken, _cts.Token);
                        if (success)
                            successCount++;
                    }

                    Debug.Log($"[BugOneTouch] PendingUpload: 처리 완료 ({successCount}/{entries.Count} 성공)");
                }
                catch (OperationCanceledException)
                {
                    Debug.Log("[BugOneTouch] PendingUpload: 처리가 취소되었습니다.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BugOneTouch] PendingUpload: 처리 중 오류: {ex.Message}");
                }
                finally
                {
                    _cts?.Dispose();
                    _cts = null;
                }

                return successCount;
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        }

        /// <summary>
        /// pending 항목 수를 반환합니다.
        /// </summary>
        public int GetPendingCount()
        {
            string pendingDir = GetPendingDirectory();
            if (!Directory.Exists(pendingDir))
                return 0;

            return Directory.GetFiles(pendingDir, $"*{PendingFileSuffix}").Length;
        }

        /// <summary>
        /// 진행 중인 처리를 취소합니다.
        /// </summary>
        public void CancelProcessing()
        {
            _cts?.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cts?.Cancel();
            _cts?.Dispose();

            if (_tokenStore != null)
                _tokenStore.OnTokenChanged -= HandleTokenChanged;
        }

        // ─── 내부 구현 ──────────────────────────────────────────────────────

        /// <summary>
        /// 단일 pending 항목을 업로드합니다.
        /// </summary>
        private async Task<bool> ProcessSingleEntryAsync(
            PendingEntry entry,
            string accessToken,
            CancellationToken ct)
        {
            string bundleId = entry.bundleId;

            try
            {
                // 최대 재시도 횟수 초과 확인
                if (entry.retryCount >= MaxRetryCount)
                {
                    Debug.LogWarning($"[BugOneTouch] PendingUpload: 최대 재시도 초과, 건너뜀 (bundleId={bundleId}, retryCount={entry.retryCount})");
                    return false;
                }

                // 재시도 시 지수 백오프 딜레이 적용
                if (entry.retryCount > 0)
                {
                    int delayIndex = Math.Min(entry.retryCount - 1, RetryDelaysSeconds.Length - 1);
                    int delayMs = RetryDelaysSeconds[delayIndex] * 1000;
                    Debug.Log($"[BugOneTouch] PendingUpload: 재시도 대기 {RetryDelaysSeconds[delayIndex]}초 (bundleId={bundleId}, retryCount={entry.retryCount})");
                    await Task.Delay(delayMs, ct);
                }

                // 번들 매니페스트 로드
                BundleManifest manifest = await _bundleRepository.GetByIdAsync(bundleId);
                if (manifest == null)
                {
                    Debug.LogWarning($"[BugOneTouch] PendingUpload: 번들을 찾을 수 없습니다. pending 제거 (bundleId={bundleId})");
                    RemovePendingFile(bundleId);
                    return false;
                }

                // 이미 제출된 경우 스킵
                if (manifest.state == BundleState.Submitted)
                {
                    Debug.Log($"[BugOneTouch] PendingUpload: 이미 제출된 번들. pending 제거 (bundleId={bundleId})");
                    RemovePendingFile(bundleId);
                    return true;
                }

                // 번들 상태를 Submitting으로 변경
                await _bundleRepository.UpdateStateAsync(bundleId, BundleState.Submitting);

                // 번들 파일 읽기 및 전송
                var files = await BuildFileAttachmentsAsync(manifest);
                if (files.Count == 0)
                {
                    Debug.LogWarning($"[BugOneTouch] PendingUpload: 전송할 파일이 없습니다 (bundleId={bundleId})");
                    await _bundleRepository.UpdateStateAsync(bundleId, BundleState.Failed);
                    UpdatePendingRetryCount(entry);
                    return false;
                }

                var request = new ReportSubmitRequest
                {
                    AccessToken = accessToken,
                    WorkspaceId = _settings.tenantId,
                    Title = manifest.title ?? $"Bug Report {(bundleId.Length > 8 ? bundleId[..8] : bundleId)}",
                    Description = BuildDescription(manifest),
                    Files = files
                };

                using var uploadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                uploadCts.CancelAfter(TimeSpan.FromSeconds(60));

                var result = await _submitService.SubmitReportAsync(request, cancellationToken: uploadCts.Token);

                if (result.Success)
                {
                    // 성공: 번들 상태 변경 + pending 파일 제거
                    manifest.state = BundleState.Submitted;
                    manifest.registered_at = DateTime.UtcNow.ToString("O");
                    await _bundleRepository.UpdateStateAsync(bundleId, BundleState.Submitted);
                    RemovePendingFile(bundleId);

                    Debug.Log($"[BugOneTouch] PendingUpload: 업로드 성공! (bundleId={bundleId}, reportId={result.ReportId})");
                    OnPendingUploadCompleted?.Invoke(true, bundleId, result.ReportId);
                    return true;
                }
                else
                {
                    // 실패: 재시도 카운트 증가
                    await _bundleRepository.UpdateStateAsync(bundleId, BundleState.Pending);
                    UpdatePendingRetryCount(entry);

                    Debug.LogWarning($"[BugOneTouch] PendingUpload: 업로드 실패 (bundleId={bundleId}): {result.ErrorMessage}");
                    OnPendingUploadCompleted?.Invoke(false, bundleId, result.ErrorMessage);
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                // 취소 시 Pending 상태로 복원
                try { await _bundleRepository.UpdateStateAsync(bundleId, BundleState.Pending); }
                catch { /* 무시 */ }

                Debug.Log($"[BugOneTouch] PendingUpload: 업로드 취소됨 (bundleId={bundleId})");
                return false;
            }
            catch (Exception ex)
            {
                // 예외 시 재시도 카운트 증가
                try { await _bundleRepository.UpdateStateAsync(bundleId, BundleState.Pending); }
                catch { /* 무시 */ }
                UpdatePendingRetryCount(entry);

                Debug.LogError($"[BugOneTouch] PendingUpload: 업로드 중 오류 (bundleId={bundleId}): {ex.Message}");
                OnPendingUploadCompleted?.Invoke(false, bundleId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 번들의 아티팩트 파일들을 FileAttachment 목록으로 구성합니다.
        /// </summary>
        private async Task<List<FileAttachment>> BuildFileAttachmentsAsync(BundleManifest manifest)
        {
            var files = new List<FileAttachment>();
            string bundleDir = BundleWriter.GetBundleDirectory(manifest.id);

            if (manifest.artifacts == null)
                return files;

            foreach (var artifact in manifest.artifacts)
            {
                string filePath = Path.Combine(bundleDir, artifact.file_name);

                if (artifact.type == BundleArtifactType.Video && Directory.Exists(filePath))
                {
                    // 비디오 디렉토리의 경우 첫 번째 파일 사용 (MP4 우선)
                    string[] videoFiles = Directory.GetFiles(filePath);
                    if (videoFiles.Length > 0)
                    {
                        // MP4 파일 우선 탐색
                        string videoFile = null;
                        foreach (string vf in videoFiles)
                        {
                            if (vf.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                            {
                                videoFile = vf;
                                break;
                            }
                        }
                        videoFile ??= videoFiles[0];

                        byte[] data = await Task.Run(() => File.ReadAllBytes(videoFile));
                        files.Add(new FileAttachment
                        {
                            FileName = Path.GetFileName(videoFile),
                            Data = data,
                            FileType = "video"
                        });
                    }
                }
                else if (File.Exists(filePath))
                {
                    string fileType = artifact.type switch
                    {
                        BundleArtifactType.Screenshot => "screenshot",
                        BundleArtifactType.Log => "log",
                        BundleArtifactType.State => "state",
                        BundleArtifactType.Video => "video",
                        _ => "unknown"
                    };

                    byte[] data = await Task.Run(() => File.ReadAllBytes(filePath));
                    files.Add(new FileAttachment
                    {
                        FileName = artifact.file_name,
                        Data = data,
                        FileType = fileType
                    });
                }
                else
                {
                    Debug.LogWarning($"[BugOneTouch] PendingUpload: 아티팩트 파일 없음: {filePath}");
                }
            }

            return files;
        }

        /// <summary>
        /// 토큰 변경 이벤트 핸들러. 로그인 성공 시 pending 자동 업로드를 시작합니다.
        /// </summary>
        private void HandleTokenChanged()
        {
            if (_disposed) return;

            int pendingCount = GetPendingCount();
            if (pendingCount == 0)
                return;

            Debug.Log($"[BugOneTouch] PendingUpload: 로그인 감지! pending {pendingCount}개 자동 업로드 시작");

            // 비동기로 pending 처리 시작 (fire-and-forget)
            _ = ProcessAllPendingAsync();
        }

        /// <summary>
        /// pending 파일의 재시도 카운트를 증가시키고 저장합니다.
        /// </summary>
        private void UpdatePendingRetryCount(PendingEntry entry)
        {
            try
            {
                entry.retryCount++;
                string pendingPath = GetPendingFilePath(entry.bundleId);

                if (File.Exists(pendingPath))
                {
                    string json = JsonUtility.ToJson(entry, prettyPrint: true);
                    string tempPath = pendingPath + ".tmp";
                    File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                    File.Delete(pendingPath);
                    File.Move(tempPath, pendingPath);
                }

                Debug.Log($"[BugOneTouch] PendingUpload: 재시도 카운트 증가 (bundleId={entry.bundleId}, retryCount={entry.retryCount}/{MaxRetryCount})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] PendingUpload: 재시도 카운트 업데이트 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// pending 파일을 삭제합니다.
        /// </summary>
        private void RemovePendingFile(string bundleId)
        {
            try
            {
                string pendingPath = GetPendingFilePath(bundleId);
                if (File.Exists(pendingPath))
                {
                    File.Delete(pendingPath);
                    Debug.Log($"[BugOneTouch] PendingUpload: pending 파일 제거 (bundleId={bundleId})");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] PendingUpload: pending 파일 삭제 실패 ({bundleId}): {ex.Message}");
            }
        }

        /// <summary>
        /// 메타데이터를 기반으로 설명 문자열을 구성합니다.
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

        // ─── 경로 유틸리티 ──────────────────────────────────────────────────

        /// <summary>
        /// pending 디렉토리 경로를 반환합니다.
        /// </summary>
        public static string GetPendingDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "BugOneTouch", PendingFolderName);
        }

        /// <summary>
        /// 특정 bundleId의 pending 파일 경로를 반환합니다.
        /// </summary>
        private static string GetPendingFilePath(string bundleId)
        {
            return Path.Combine(GetPendingDirectory(), $"{bundleId}{PendingFileSuffix}");
        }

        /// <summary>
        /// pending 디렉토리가 존재하지 않으면 생성합니다.
        /// </summary>
        private static void EnsurePendingDirectory()
        {
            string dir = GetPendingDirectory();
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Debug.Log($"[BugOneTouch] PendingUpload: pending 디렉토리 생성: {dir}");
            }
        }
    }
}
