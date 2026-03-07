using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// Jira 이슈 제출 통합 서비스.
    /// 이슈 생성 → 첨부파일 업로드 → 번들 상태 갱신을 순서대로 처리합니다.
    /// 부분 실패(이슈 생성 성공 + 첨부 실패)도 상태에 반영합니다.
    /// </summary>
#pragma warning disable CS0618 // Obsolete 경고 억제 (JiraAttachmentUploader 하위 호환성)
    public class JiraSubmissionService
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>제출 진행 상태 변경 이벤트. (진행률 0~1, 메시지)</summary>
        public event Action<float, string> OnProgressChanged;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly JiraIssueCreator _issueCreator;
        private readonly JiraAttachmentUploader _attachmentUploader;
        private readonly IBundleStateUpdater _bundleStateUpdater;

        // ─── 요청/응답 모델 ────────────────────────────────────────────────────────

        /// <summary>Jira 이슈 제출 요청</summary>
        public class SubmissionRequest
        {
            /// <summary>번들 고유 ID (번들 상태 갱신에 사용)</summary>
            public string BundleId { get; set; }

            /// <summary>Jira 이슈 생성 데이터</summary>
            public JiraIssueCreator.CreateIssueRequest IssueRequest { get; set; }

            /// <summary>업로드할 첨부파일 목록</summary>
            public List<JiraAttachmentUploader.AttachmentItem> Attachments { get; set; }
                = new List<JiraAttachmentUploader.AttachmentItem>();

            /// <summary>
            /// R2 URL이 이미 설정되어 있으면 true.
            /// true이면 직접 첨부 업로드를 건너뛰고 description에 R2 URL 링크를 사용합니다.
            /// </summary>
            public bool UseR2Links { get; set; }
        }

        /// <summary>Jira 이슈 제출 결과</summary>
        public class SubmissionResult
        {
            /// <summary>제출 성공 여부 (이슈 생성 기준)</summary>
            public bool Success { get; set; }

            /// <summary>생성된 이슈 키 (예: "PROJ-123")</summary>
            public string IssueKey { get; set; }

            /// <summary>생성된 이슈 URL</summary>
            public string IssueUrl { get; set; }

            /// <summary>첨부파일 업로드 결과 (null이면 첨부파일 없음)</summary>
            public JiraAttachmentUploader.UploadResult AttachmentResult { get; set; }

            /// <summary>오류 메시지 (실패 시)</summary>
            public string ErrorMessage { get; set; }

            /// <summary>
            /// 이슈 생성은 성공했지만 첨부파일 업로드가 부분 실패한 경우 true
            /// </summary>
            public bool IsPartialSuccess =>
                Success &&
                AttachmentResult != null &&
                !AttachmentResult.IsFullySuccessful;
        }

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// JiraSubmissionService를 초기화합니다.
        /// </summary>
        /// <param name="issueCreator">이슈 생성기</param>
        /// <param name="attachmentUploader">첨부파일 업로더 (R2 URL 방식 전환 후 Obsolete, null 허용)</param>
        /// <param name="bundleStateUpdater">번들 상태 갱신기 (M2 구현, null 허용)</param>
        public JiraSubmissionService(
            JiraIssueCreator issueCreator,
            JiraAttachmentUploader attachmentUploader,
            IBundleStateUpdater bundleStateUpdater = null)
        {
            _issueCreator = issueCreator ?? throw new ArgumentNullException(nameof(issueCreator));
            _attachmentUploader = attachmentUploader; // null 허용 (R2 URL 방식 전환 시)
            _bundleStateUpdater = bundleStateUpdater; // null 허용 (M2 미구현 시)
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Jira 이슈를 제출합니다.
        /// 이슈 생성 → 첨부파일 업로드 → 번들 상태 갱신 순서로 처리됩니다.
        /// </summary>
        /// <param name="request">제출 요청 데이터</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>제출 결과</returns>
        public async Task<SubmissionResult> SubmitAsync(
            SubmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.IssueRequest == null)
                throw new ArgumentException("IssueRequest는 필수입니다.", nameof(request));

            var result = new SubmissionResult();

            // Step 1: 번들 상태 → '제출 중'
            NotifyProgress(0f, "Jira 이슈 생성 중...");
            await UpdateBundleStateSubmittingAsync(request.BundleId, cancellationToken);

            // Step 2: Jira 이슈 생성
            try
            {
                Debug.Log($"[JiraSubmissionService] 이슈 생성 시작: {request.IssueRequest.Summary}");
                var issueResult = await _issueCreator.CreateAsync(
                    request.IssueRequest,
                    cancellationToken);

                result.Success = true;
                result.IssueKey = issueResult.IssueKey;
                result.IssueUrl = issueResult.IssueUrl;

                Debug.Log($"[JiraSubmissionService] 이슈 생성 완료: {result.IssueKey}");
                NotifyProgress(0.4f, $"이슈 생성 완료: {result.IssueKey}. 첨부파일 업로드 중...");
            }
            catch (OperationCanceledException)
            {
                await UpdateBundleStateFailedAsync(
                    request.BundleId, "제출 취소됨", cancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                var errorMsg = $"이슈 생성 실패: {ex.Message}";
                Debug.LogError($"[JiraSubmissionService] {errorMsg}");

                result.Success = false;
                result.ErrorMessage = errorMsg;
                NotifyProgress(1f, $"제출 실패: {errorMsg}");

                await UpdateBundleStateFailedAsync(request.BundleId, errorMsg, cancellationToken);
                return result;
            }

            // Step 3: 첨부파일 업로드 (R2 URL이 설정된 경우 건너뜀)
            if (request.UseR2Links)
            {
                Debug.Log("[JiraSubmissionService] R2 URL 링크 방식 사용. 직접 첨부 업로드를 건너뜁니다.");
                NotifyProgress(0.9f, "R2 URL 링크가 이슈 설명에 포함되었습니다.");
            }
            else if (request.Attachments != null && request.Attachments.Count > 0 && _attachmentUploader != null)
            {
                try
                {
                    Debug.Log($"[JiraSubmissionService] 첨부파일 업로드 시작 " +
                              $"({request.Attachments.Count}개 파일)");

                    result.AttachmentResult = await _attachmentUploader.UploadAsync(
                        result.IssueKey,
                        request.Attachments,
                        cancellationToken);

                    if (result.AttachmentResult.IsFullySuccessful)
                    {
                        NotifyProgress(0.9f, "첨부파일 업로드 완료.");
                        Debug.Log("[JiraSubmissionService] 첨부파일 모두 업로드 완료");
                    }
                    else
                    {
                        // 부분 실패: 이슈는 생성되었으나 일부 첨부파일 실패
                        var skipped = result.AttachmentResult.SkippedFiles.Count;
                        var failed = result.AttachmentResult.FailedFiles.Count;
                        var succeeded = result.AttachmentResult.SucceededFiles.Count;

                        Debug.LogWarning(
                            $"[JiraSubmissionService] 첨부파일 부분 실패. " +
                            $"성공: {succeeded}, 건너뜀: {skipped}, 실패: {failed}");

                        NotifyProgress(0.9f,
                            $"일부 첨부파일 업로드 실패 (성공: {succeeded}, 건너뜀: {skipped}, 실패: {failed})");
                    }
                }
                catch (OperationCanceledException)
                {
                    // 이슈는 생성된 상태로 취소 → 부분 성공으로 처리
                    Debug.LogWarning("[JiraSubmissionService] 첨부파일 업로드 중 취소됨. 이슈는 생성 완료.");
                    NotifyProgress(0.9f, "첨부파일 업로드가 취소되었습니다. 이슈는 생성되었습니다.");
                    // 취소여도 이슈는 이미 생성되었으므로 번들 상태는 성공으로 처리
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[JiraSubmissionService] 첨부파일 업로드 예외: {ex.Message}");
                    // 이슈 생성은 성공 → 부분 성공 상태 유지
                }
            }
            else if (!request.UseR2Links && request.Attachments != null
                     && request.Attachments.Count > 0 && _attachmentUploader == null)
            {
                // 첨부파일이 있지만 업로더가 null인 비정상 상태
                Debug.LogError(
                    "[JiraSubmissionService] 첨부파일이 있지만 AttachmentUploader가 null입니다. " +
                    "R2 URL 방식을 사용하거나 AttachmentUploader를 제공하세요.");
                NotifyProgress(0.9f, "첨부파일 업로드 불가: 업로더 미설정.");
            }
            else
            {
                NotifyProgress(0.9f, "첨부파일 없음.");
            }

            // Step 4: 번들 상태 → '제출 완료'
            await UpdateBundleStateSubmittedAsync(
                request.BundleId,
                result.IssueKey,
                result.IssueUrl,
                cancellationToken);

            NotifyProgress(1f, $"Jira 이슈 제출 완료! 이슈 키: {result.IssueKey}");
            Debug.Log($"[JiraSubmissionService] 전체 제출 프로세스 완료. 이슈: {result.IssueKey}");

            return result;
        }

        // ─── 내부 메서드 ───────────────────────────────────────────────────────────

        private void NotifyProgress(float progress, string message)
        {
            try
            {
                OnProgressChanged?.Invoke(progress, message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JiraSubmissionService] OnProgressChanged 이벤트 핸들러 오류: {ex.Message}");
            }
        }

        private async Task UpdateBundleStateSubmittingAsync(
            string bundleId,
            CancellationToken cancellationToken)
        {
            if (_bundleStateUpdater == null || string.IsNullOrEmpty(bundleId))
                return;

            try
            {
                await _bundleStateUpdater.UpdateSubmittingAsync(bundleId, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JiraSubmissionService] 번들 상태 '제출 중' 갱신 실패 (무시): {ex.Message}");
            }
        }

        private async Task UpdateBundleStateSubmittedAsync(
            string bundleId,
            string issueKey,
            string issueUrl,
            CancellationToken cancellationToken)
        {
            if (_bundleStateUpdater == null || string.IsNullOrEmpty(bundleId))
                return;

            try
            {
                await _bundleStateUpdater.UpdateSubmittedAsync(bundleId, issueKey, issueUrl, cancellationToken);
                Debug.Log($"[JiraSubmissionService] 번들 상태 '제출 완료' 갱신. bundleId: {bundleId}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JiraSubmissionService] 번들 상태 '제출 완료' 갱신 실패 (무시): {ex.Message}");
            }
        }

        private async Task UpdateBundleStateFailedAsync(
            string bundleId,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            if (_bundleStateUpdater == null || string.IsNullOrEmpty(bundleId))
                return;

            try
            {
                await _bundleStateUpdater.UpdateFailedAsync(bundleId, errorMessage, cancellationToken);
                Debug.Log($"[JiraSubmissionService] 번들 상태 '제출 실패' 갱신. bundleId: {bundleId}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JiraSubmissionService] 번들 상태 '제출 실패' 갱신 실패 (무시): {ex.Message}");
            }
        }
    }
#pragma warning restore CS0618
}
