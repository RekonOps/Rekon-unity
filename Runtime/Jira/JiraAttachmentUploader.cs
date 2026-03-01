using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// Jira 이슈에 첨부파일을 업로드합니다.
    /// POST /rest/api/3/issue/{issueKey}/attachments
    /// X-Atlassian-Token: no-check 헤더로 XSRF 검증을 비활성화합니다.
    /// </summary>
    public class JiraAttachmentUploader
    {
        // ─── 상수 ─────────────────────────────────────────────────────────────────

        /// <summary>기본 파일 크기 제한 (10MB)</summary>
        public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly JiraApiClient _apiClient;
        private readonly long _maxFileSizeBytes;

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
        /// 여러 첨부파일을 Jira 이슈에 업로드합니다.
        /// 크기 초과 파일은 경고 로그 출력 후 건너뜁니다.
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

            foreach (var attachment in attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attachment?.Data == null || string.IsNullOrEmpty(attachment.FileName))
                {
                    Debug.LogWarning("[JiraAttachmentUploader] 유효하지 않은 첨부파일 건너뜀 (null 또는 빈 이름)");
                    continue;
                }

                // 크기 제한 확인
                if (attachment.Data.Length > _maxFileSizeBytes)
                {
                    var sizeMb = attachment.Data.Length / (1024f * 1024f);
                    var limitMb = _maxFileSizeBytes / (1024f * 1024f);
                    Debug.LogWarning(
                        $"[JiraAttachmentUploader] 파일 크기 초과, 건너뜀: {attachment.FileName} " +
                        $"({sizeMb:F1}MB > {limitMb:F1}MB 제한)");
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
