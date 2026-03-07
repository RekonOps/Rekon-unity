using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// Jira 이슈에 첨부파일을 업로드합니다.
    /// POST /rest/api/3/issue/{issueKey}/attachments
    /// X-Atlassian-Token: no-check 헤더로 XSRF 검증을 비활성화합니다.
    ///
    /// 업로드 전 Jira REST API에서 첨부 제한 설정을 조회합니다 (AC-11).
    /// </summary>
    [System.Obsolete("R2 URL 링크 방식으로 대체됨. JiraIssueCreator에서 R2 URL을 사용합니다. 하위 호환성을 위해 유지됩니다.")]
    public class JiraAttachmentUploader
    {
        // ─── 상수 ─────────────────────────────────────────────────────────────────

        /// <summary>기본 파일 크기 제한 (10MB)</summary>
        public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly JiraApiClient _apiClient;
        private readonly long _maxFileSizeBytes;

        // ─── Jira 설정 응답 역직렬화용 모델 ──────────────────────────────────────

        /// <summary>GET /rest/api/3/configuration 응답 모델</summary>
        [System.Serializable]
        private class JiraConfigurationResponse
        {
            /// <summary>첨부파일 활성화 여부</summary>
            public bool attachmentsEnabled;

            /// <summary>최대 첨부파일 크기 (바이트). 0이면 제한 없음.</summary>
            public long maxAttachmentSize;
        }

        // ─── 데이터 모델 ───────────────────────────────────────────────────────────

        /// <summary>첨부파일 항목</summary>
        public class AttachmentItem
        {
            /// <summary>파일 이름 (확장자 포함)</summary>
            public string FileName { get; set; }

            /// <summary>파일 데이터 바이트 배열</summary>
            public byte[] Data { get; set; }

            /// <summary>MIME 타입 (기본: application/octet-stream)</summary>
            public string ContentType { get; set; } = "application/octet-stream";
        }

        /// <summary>첨부파일 업로드 결과</summary>
        public class UploadResult
        {
            /// <summary>업로드 성공한 파일 목록</summary>
            public List<string> SucceededFiles { get; } = new List<string>();

            /// <summary>크기 초과로 건너뛴 파일 목록</summary>
            public List<string> SkippedFiles { get; } = new List<string>();

            /// <summary>업로드 실패한 파일 목록</summary>
            public List<(string FileName, string Error)> FailedFiles { get; }
                = new List<(string, string)>();

            /// <summary>모든 파일이 성공적으로 업로드되었는지 여부</summary>
            public bool IsFullySuccessful =>
                SkippedFiles.Count == 0 && FailedFiles.Count == 0 && SucceededFiles.Count > 0;

            /// <summary>최소 하나의 파일이 업로드되었는지 여부</summary>
            public bool HasAnySuccess => SucceededFiles.Count > 0;
        }

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// JiraAttachmentUploader를 초기화합니다.
        /// </summary>
        /// <param name="apiClient">Jira API 클라이언트</param>
        /// <param name="maxFileSizeBytes">파일당 최대 크기 (기본: 10MB)</param>
        public JiraAttachmentUploader(
            JiraApiClient apiClient,
            long maxFileSizeBytes = DefaultMaxFileSizeBytes)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _maxFileSizeBytes = maxFileSizeBytes;
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Jira REST API에서 첨부파일 최대 크기 제한을 조회합니다 (AC-11).
        /// GET /rest/api/3/configuration → attachmentsEnabled, maxAttachmentSize 확인.
        /// API 호출 실패 시 기본값(_maxFileSizeBytes)을 반환합니다.
        /// </summary>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>
        /// 첨부 비활성화 시 0 반환 (업로드 불가).
        /// 정상 시 서버에서 조회된 최대 크기(바이트) 반환.
        /// 조회 실패 시 기본값 반환.
        /// </returns>
        public async Task<long> GetMaxAttachmentSizeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var responseJson = await _apiClient.GetAsync("/configuration", cancellationToken);

                if (string.IsNullOrEmpty(responseJson))
                    return _maxFileSizeBytes;

                var config = JsonUtility.FromJson<JiraConfigurationResponse>(responseJson);

                if (config == null)
                    return _maxFileSizeBytes;

                // 첨부파일 기능 비활성화된 경우
                if (!config.attachmentsEnabled)
                {
                    Debug.LogWarning("[JiraAttachmentUploader] Jira 인스턴스에서 첨부파일 기능이 비활성화되어 있습니다.");
                    return 0L;
                }

                // maxAttachmentSize가 0이면 제한 없음 → 기본값 사용
                if (config.maxAttachmentSize > 0)
                {
                    var limitMb = config.maxAttachmentSize / (1024f * 1024f);
                    Debug.Log($"[JiraAttachmentUploader] Jira 첨부파일 최대 크기 조회 완료: {limitMb:F1}MB");
                    return config.maxAttachmentSize;
                }

                return _maxFileSizeBytes;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JiraAttachmentUploader] 첨부파일 제한 조회 실패, 기본값 사용: {ex.Message}");
                return _maxFileSizeBytes;
            }
        }

        /// <summary>
        /// 여러 첨부파일을 Jira 이슈에 업로드합니다.
        /// 업로드 전 Jira API에서 첨부 제한을 조회하여 크기 초과 파일을 자동 제외합니다 (AC-11).
        /// 영상 파일이 크기 제한 초과로 제외되는 경우 사용자 알림 로그를 출력합니다.
        /// </summary>
        /// <param name="issueKey">대상 Jira 이슈 키 (예: "PROJ-123")</param>
        /// <param name="attachments">업로드할 첨부파일 목록</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>업로드 결과</returns>
        public async Task<UploadResult> UploadAsync(
            string issueKey,
            IEnumerable<AttachmentItem> attachments,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(issueKey))
                throw new ArgumentNullException(nameof(issueKey));

            if (attachments == null)
                throw new ArgumentNullException(nameof(attachments));

            var result = new UploadResult();
            var apiPath = $"/issue/{Uri.EscapeDataString(issueKey)}/attachments";

            // Jira 서버에서 실제 첨부 제한 조회 (AC-11)
            long effectiveMaxSize = await GetMaxAttachmentSizeAsync(cancellationToken);

            // 첨부파일 기능 비활성화 처리
            if (effectiveMaxSize == 0L)
            {
                Debug.LogWarning("[JiraAttachmentUploader] 첨부파일 기능이 비활성화되어 모든 첨부파일을 건너뜁니다.");
                foreach (var item in attachments)
                {
                    if (item?.FileName != null)
                        result.SkippedFiles.Add(item.FileName);
                }
                return result;
            }

            foreach (var attachment in attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attachment?.Data == null || string.IsNullOrEmpty(attachment.FileName))
                {
                    Debug.LogWarning("[JiraAttachmentUploader] 유효하지 않은 첨부파일 건너뜀 (null 또는 빈 이름)");
                    continue;
                }

                // 크기 제한 확인 (Jira 서버에서 조회된 실제 제한 적용)
                if (attachment.Data.Length > effectiveMaxSize)
                {
                    var sizeMb  = attachment.Data.Length / (1024f * 1024f);
                    var limitMb = effectiveMaxSize / (1024f * 1024f);

                    // 영상 파일 제외 시 사용자 알림 로그 (AC-11)
                    bool isVideo = IsVideoFile(attachment.FileName);
                    if (isVideo)
                    {
                        Debug.LogWarning(
                            $"[BugOneTouch] 영상 파일이 Jira 첨부 크기 제한을 초과하여 자동 제외됩니다: " +
                            $"{attachment.FileName} ({sizeMb:F1}MB > {limitMb:F1}MB 제한). " +
                            $"영상 없이 다른 첨부파일만 업로드됩니다.");
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[JiraAttachmentUploader] 파일 크기 초과, 건너뜀: {attachment.FileName} " +
                            $"({sizeMb:F1}MB > {limitMb:F1}MB 제한)");
                    }

                    result.SkippedFiles.Add(attachment.FileName);
                    continue;
                }

                try
                {
                    await UploadSingleFileAsync(apiPath, attachment, cancellationToken);
                    result.SucceededFiles.Add(attachment.FileName);
                    Debug.Log($"[JiraAttachmentUploader] 업로드 완료: {attachment.FileName} " +
                              $"({attachment.Data.Length / 1024f:F1}KB)");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JiraAttachmentUploader] 업로드 실패: {attachment.FileName} → {ex.Message}");
                    result.FailedFiles.Add((attachment.FileName, ex.Message));
                }
            }

            Debug.Log($"[JiraAttachmentUploader] 업로드 완료. " +
                      $"성공: {result.SucceededFiles.Count}, " +
                      $"건너뜀: {result.SkippedFiles.Count}, " +
                      $"실패: {result.FailedFiles.Count}");

            return result;
        }

        /// <summary>
        /// 파일 경로 목록에서 첨부파일을 로드하여 업로드합니다.
        /// </summary>
        /// <param name="issueKey">대상 Jira 이슈 키</param>
        /// <param name="filePaths">업로드할 파일 경로 목록</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>업로드 결과</returns>
        public async Task<UploadResult> UploadFromPathsAsync(
            string issueKey,
            IEnumerable<string> filePaths,
            CancellationToken cancellationToken = default)
        {
            var attachments = new List<AttachmentItem>();

            foreach (var path in filePaths)
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[JiraAttachmentUploader] 파일 없음, 건너뜀: {path}");
                    continue;
                }

                try
                {
                    var data = File.ReadAllBytes(path);
                    var fileName = Path.GetFileName(path);
                    var contentType = GuessContentType(fileName);
                    attachments.Add(new AttachmentItem
                    {
                        FileName = fileName,
                        Data = data,
                        ContentType = contentType
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JiraAttachmentUploader] 파일 로드 실패: {path} → {ex.Message}");
                }
            }

            return await UploadAsync(issueKey, attachments, cancellationToken);
        }

        // ─── 내부 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 파일 이름이 영상 파일인지 판단합니다 (AC-11 영상 파일 감지용).
        /// </summary>
        private static bool IsVideoFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".mkv" || ext == ".webm";
        }

        /// <summary>
        /// 단일 파일을 Jira에 업로드합니다.
        /// multipart/form-data 형식으로 전송합니다.
        /// </summary>
        private async Task UploadSingleFileAsync(
            string apiPath,
            AttachmentItem attachment,
            CancellationToken cancellationToken)
        {
            var formData = new MultipartFormData();
            formData.AddFile(attachment.FileName, attachment.Data, attachment.ContentType);

            await _apiClient.PostMultipartAsync(apiPath, formData, cancellationToken);
        }

        /// <summary>
        /// 파일 확장자로 MIME 타입을 추측합니다.
        /// </summary>
        private static string GuessContentType(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "application/octet-stream";

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".png"  => "image/png",
                ".jpg"  => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif"  => "image/gif",
                ".bmp"  => "image/bmp",
                ".mp4"  => "video/mp4",
                ".avi"  => "video/avi",
                ".mov"  => "video/quicktime",
                ".txt"  => "text/plain",
                ".log"  => "text/plain",
                ".json" => "application/json",
                ".xml"  => "application/xml",
                ".zip"  => "application/zip",
                _       => "application/octet-stream"
            };
        }
    }
}
