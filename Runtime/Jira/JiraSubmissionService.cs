using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// Jira 이슈 제출 통합 서비스.
    /// 이슈 생성 → 번들 상태 갱신을 순서대로 처리합니다.
    ///
    /// ⚠️ JAM.dev 패턴 적용 (ADR-047):
    /// Unity에서 Jira로 직접 파일을 첨부 업로드하는 기능은 제거되었습니다.
    /// 파일은 R2에 업로드되고, description에 R2 URL 링크로 삽입됩니다.
    /// Jira 직접 업로드가 필요한 경우 웹 대시보드(push-to-jira)를 사용하세요.
    /// </summary>
    public class JiraSubmissionService
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>제출 진행 상태 변경 이벤트. (진행률 0~1, 메시지)</summary>
        public event Action<float, string> OnProgressChanged;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly JiraIssueCreator _issueCreator;
        private readonly IBundleStateUpdater _bundleStateUpdater;

        // ─── 요청/응답 모델 ────────────────────────────────────────────────────────

        /// <summary>Jira 이슈 제출 요청</summary>
        public class SubmissionRequest
        {
            /// <summary>번들 고유 ID (번들 상태 갱신에 사용)</summary>
            public string BundleId { get; set; }

            /// <summary>Jira 이슈 생성 데이터</summary>
            public JiraIssueCreator.CreateIssueRequest IssueRequest { get; set; }
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

            /// <summary>오류 메시지 (실패 시)</summary>
            public string ErrorMessage { get; set; }
        }

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// JiraSubmissionService를 초기화합니다.
        /// </summary>
        /// <param name="issueCreator">이슈 생성기</param>
        /// <param name="bundleStateUpdater">번들 상태 갱신기 (null 허용)</param>
        public JiraSubmissionService(
            JiraIssueCreator issueCreator,
            IBundleStateUpdater bundleStateUpdater = null)
        {
            _issueCreator = issueCreator ?? throw new ArgumentNullException(nameof(issueCreator));
            _bundleStateUpdater = bundleStateUpdater; // null 허용
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Jira 이슈를 제출합니다.
        /// 이슈 생성 → 번들 상태 갱신 순서로 처리됩니다.
        /// 파일 첨부는 R2 URL 링크 방식(IssueRequest.R2Urls)으로만 지원합니다.
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

            // Step 2: Jira 이슈 생성 (R2 URL이 있으면 description에 자동으로 링크 삽입됨)
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
                NotifyProgress(0.9f, $"이슈 생성 완료: {result.IssueKey}");
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

            // Step 3: 번들 상태 → '제출 완료'
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
}
