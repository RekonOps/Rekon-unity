using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 번들 제출 재시도 큐.
    ///
    /// 역할:
    ///   - Failed 상태의 번들을 Pending으로 전환하여 재시도
    ///   - 성공 시 Submitted 상태로 변경 및 Jira 이슈 키 기록
    ///   - 최대 재시도 횟수(3회) 초과 시 Failed 상태 유지
    ///   - 지수 백오프 간격: 5초, 15초, 45초
    ///
    /// 사용법:
    ///   var queue = new SubmissionQueue(repository, submitFunc);
    ///   await queue.ProcessPendingAsync();
    /// </summary>
    public class SubmissionQueue
    {
        // 최대 재시도 횟수
        public const int MaxRetryCount = 3;

        // 지수 백오프 간격 (초 단위)
        private static readonly int[] RetryDelaysSeconds = { 5, 15, 45 };

        private readonly BundleRepository _repository;
        private readonly Func<BundleManifest, CancellationToken, Task<string>> _submitFunc;

        /// <summary>
        /// 제출 완료 이벤트. (bundleId, jiraIssueKey) 형태로 발행됩니다.
        /// </summary>
        public event Action<string, string> OnSubmitted;

        /// <summary>
        /// 제출 실패 이벤트. (bundleId, retryCount, errorMessage) 형태로 발행됩니다.
        /// </summary>
        public event Action<string, int, string> OnFailed;

        /// <summary>
        /// SubmissionQueue를 초기화합니다.
        /// </summary>
        /// <param name="repository">번들 저장소.</param>
        /// <param name="submitFunc">
        /// 실제 Jira 제출 함수.
        /// (BundleManifest, CancellationToken) → Task&lt;string&gt; (jiraIssueKey 반환)
        /// 실패 시 예외를 던져야 합니다.
        /// </param>
        /// <exception cref="ArgumentNullException">repository 또는 submitFunc가 null인 경우.</exception>
        public SubmissionQueue(
            BundleRepository repository,
            Func<BundleManifest, CancellationToken, Task<string>> submitFunc)
        {
            _repository = repository
                ?? throw new ArgumentNullException(nameof(repository), "BundleRepository가 null입니다.");
            _submitFunc = submitFunc
                ?? throw new ArgumentNullException(nameof(submitFunc), "제출 함수가 null입니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 공개 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Pending 상태의 모든 번들을 순차적으로 제출합니다.
        /// 각 번들 제출은 독립적으로 처리되며, 하나 실패해도 나머지는 계속 처리합니다.
        /// </summary>
        /// <param name="cancellationToken">작업 취소 토큰.</param>
        /// <returns>성공적으로 제출된 번들 수.</returns>
        public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
        {
            List<BundleManifest> pending = await _repository.GetByStateAsync(BundleState.Pending);

            if (pending.Count == 0)
            {
                Debug.Log("[BugOneTouch] 제출 대기 중인 번들이 없습니다.");
                return 0;
            }

            Debug.Log($"[BugOneTouch] Pending 번들 {pending.Count}개 제출 시작.");

            int successCount = 0;

            foreach (var manifest in pending)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.Log("[BugOneTouch] 제출 큐 처리가 취소되었습니다.");
                    break;
                }

                bool success = await ProcessSingleBundleAsync(manifest, cancellationToken);
                if (success) successCount++;
            }

            return successCount;
        }

        /// <summary>
        /// Failed 상태의 번들을 재시도 대기열(Pending)로 복구합니다.
        /// 최대 재시도 횟수를 초과한 번들은 건너뜁니다.
        /// </summary>
        /// <returns>Pending으로 전환된 번들 수.</returns>
        public async Task<int> RequeueFailedAsync()
        {
            List<BundleManifest> failed = await _repository.GetByStateAsync(BundleState.Failed);

            int requeuedCount = 0;

            foreach (var manifest in failed)
            {
                // 최대 재시도 횟수 초과 시 건너뜀
                if (manifest.retry_count >= MaxRetryCount)
                {
                    Debug.LogWarning($"[BugOneTouch] 최대 재시도 초과 - 번들 건너뜀: {manifest.id} " +
                                     $"(재시도 횟수: {manifest.retry_count}/{MaxRetryCount})");
                    continue;
                }

                try
                {
                    await _repository.UpdateStateAsync(manifest.id, BundleState.Pending);
                    Debug.Log($"[BugOneTouch] 실패 번들 재시도 큐 등록: {manifest.id} (재시도 횟수: {manifest.retry_count})");
                    requeuedCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BugOneTouch] 번들 상태 변경 실패 ({manifest.id}): {ex.Message}");
                }
            }

            return requeuedCount;
        }

        /// <summary>
        /// 단일 번들을 제출합니다 (지수 백오프 재시도 포함).
        /// </summary>
        /// <param name="bundleId">제출할 번들 ID.</param>
        /// <param name="cancellationToken">작업 취소 토큰.</param>
        /// <returns>성공 여부.</returns>
        public async Task<bool> SubmitWithRetryAsync(string bundleId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(bundleId))
                throw new ArgumentNullException(nameof(bundleId), "번들 ID가 null 또는 빈 문자열입니다.");

            BundleManifest manifest = await _repository.GetByIdAsync(bundleId);
            if (manifest == null)
            {
                Debug.LogError($"[BugOneTouch] 번들을 찾을 수 없습니다: {bundleId}");
                return false;
            }

            return await ProcessSingleBundleAsync(manifest, cancellationToken);
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 단일 번들의 제출 프로세스를 처리합니다.
        /// 1. Submitting 상태로 전환
        /// 2. 제출 시도 (지수 백오프 재시도)
        /// 3. 성공 시 Submitted 상태로 전환
        /// 4. 실패 시 Failed 상태로 전환 및 재시도 횟수 증가
        /// </summary>
        private async Task<bool> ProcessSingleBundleAsync(BundleManifest manifest, CancellationToken cancellationToken)
        {
            string bundleId = manifest.id;

            try
            {
                // Submitting 상태로 전환
                await _repository.UpdateStateAsync(bundleId, BundleState.Submitting);
                Debug.Log($"[BugOneTouch] 번들 제출 시작: {bundleId}");

                // 지수 백오프 재시도
                string jiraIssueKey = await SubmitWithExponentialBackoffAsync(manifest, cancellationToken);

                // 성공: Submitted 상태로 전환
                await _repository.MarkSubmittedAsync(bundleId, jiraIssueKey);

                Debug.Log($"[BugOneTouch] 번들 제출 성공: {bundleId} → Jira={jiraIssueKey}");
                OnSubmitted?.Invoke(bundleId, jiraIssueKey);

                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[BugOneTouch] 번들 제출 취소됨: {bundleId}");
                // 취소 시 Pending으로 복원
                await SafeUpdateStateAsync(bundleId, BundleState.Pending);
                return false;
            }
            catch (Exception ex)
            {
                // 실패: 재시도 횟수 증가 및 Failed 상태로 전환
                Debug.LogError($"[BugOneTouch] 번들 제출 최종 실패: {bundleId} - {ex.Message}");

                int retryCount = await SafeIncrementRetryCountAsync(bundleId);
                await SafeUpdateStateAsync(bundleId, BundleState.Failed);

                OnFailed?.Invoke(bundleId, retryCount, ex.Message);

                return false;
            }
        }

        /// <summary>
        /// 지수 백오프를 적용하여 번들 제출을 재시도합니다.
        /// </summary>
        private async Task<string> SubmitWithExponentialBackoffAsync(
            BundleManifest manifest,
            CancellationToken cancellationToken)
        {
            Exception lastException = null;

            for (int attempt = 0; attempt <= MaxRetryCount; attempt++)
            {
                if (attempt > 0)
                {
                    // 지수 백오프 대기 (5초, 15초, 45초)
                    int delaySeconds = RetryDelaysSeconds[Math.Min(attempt - 1, RetryDelaysSeconds.Length - 1)];
                    Debug.Log($"[BugOneTouch] 번들 재시도 대기 {delaySeconds}초: {manifest.id} (시도 {attempt}/{MaxRetryCount})");

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string jiraIssueKey = await _submitFunc(manifest, cancellationToken);

                    if (string.IsNullOrEmpty(jiraIssueKey))
                        throw new InvalidOperationException("제출 함수가 빈 Jira 이슈 키를 반환했습니다.");

                    return jiraIssueKey;
                }
                catch (OperationCanceledException)
                {
                    throw; // 취소는 그대로 전파
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Debug.LogWarning($"[BugOneTouch] 번들 제출 시도 {attempt + 1} 실패: {manifest.id} - {ex.Message}");
                }
            }

            throw new AggregateException(
                $"번들 제출 실패 (최대 {MaxRetryCount}회 재시도 초과): {manifest.id}",
                lastException);
        }

        /// <summary>
        /// 상태 변경 실패 시 예외를 무시하고 경고만 로깅합니다.
        /// </summary>
        private async Task SafeUpdateStateAsync(string bundleId, BundleState state)
        {
            try
            {
                await _repository.UpdateStateAsync(bundleId, state);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 번들 상태 변경 실패 ({bundleId} → {state}): {ex.Message}");
            }
        }

        /// <summary>
        /// 재시도 횟수 증가 실패 시 예외를 무시하고 경고만 로깅합니다.
        /// </summary>
        private async Task<int> SafeIncrementRetryCountAsync(string bundleId)
        {
            try
            {
                return await _repository.IncrementRetryCountAsync(bundleId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 재시도 횟수 증가 실패 ({bundleId}): {ex.Message}");
                return -1;
            }
        }
    }
}
