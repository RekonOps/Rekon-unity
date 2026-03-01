using System.Threading;
using System.Threading.Tasks;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 번들 상태 갱신 인터페이스.
    /// 실제 구현은 M2(Bundle 시스템)에서 담당합니다.
    /// Jira 이슈 제출 완료/실패 시 번들의 상태를 갱신하는 데 사용됩니다.
    /// </summary>
    public interface IBundleStateUpdater
    {
        /// <summary>
        /// Jira 이슈 제출 성공 시 번들 상태를 '제출 완료'로 갱신합니다.
        /// </summary>
        /// <param name="bundleId">번들 고유 ID</param>
        /// <param name="jiraIssueKey">생성된 Jira 이슈 키 (예: "PROJ-123")</param>
        /// <param name="jiraIssueUrl">생성된 Jira 이슈 URL</param>
        /// <param name="cancellationToken">취소 토큰</param>
        Task UpdateSubmittedAsync(
            string bundleId,
            string jiraIssueKey,
            string jiraIssueUrl,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Jira 이슈 제출 실패 시 번들 상태를 '제출 실패'로 갱신합니다.
        /// </summary>
        /// <param name="bundleId">번들 고유 ID</param>
        /// <param name="errorMessage">실패 이유</param>
        /// <param name="cancellationToken">취소 토큰</param>
        Task UpdateFailedAsync(
            string bundleId,
            string errorMessage,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Jira 이슈 제출 중 상태를 '제출 중'으로 갱신합니다.
        /// </summary>
        /// <param name="bundleId">번들 고유 ID</param>
        /// <param name="cancellationToken">취소 토큰</param>
        Task UpdateSubmittingAsync(
            string bundleId,
            CancellationToken cancellationToken = default);
    }
}
