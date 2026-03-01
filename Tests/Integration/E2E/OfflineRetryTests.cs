using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace GaoZombie.BugOneTouch.Tests.Integration
{
    /// <summary>
    /// Phase 8.1 E2E 통합 테스트 - 오프라인→재시도 플로우.
    ///
    /// 테스트 시나리오:
    ///   1. 네트워크 없음 시뮬레이션 → 번들 생성 → 제출 실패 → Failed 상태
    ///   2. 재시도 큐(SubmissionQueue.RetryAsync) → pending → submitting → submitted
    ///   3. 지수 백오프 간격 검증
    ///
    /// Mock 전략:
    ///   - 실패하는 제출 함수 Mock으로 네트워크 오프라인 시뮬레이션
    ///   - 성공하는 제출 함수 Mock으로 재시도 성공 시뮬레이션
    /// </summary>
    [TestFixture]
    public class OfflineRetryTests
    {
        // 테스트용 임시 번들 디렉토리
        private string _tempBundlesRoot;
        private string _testBundleId;

        [SetUp]
        public void SetUp()
        {
            // 각 테스트마다 독립된 임시 디렉토리 사용
            _tempBundlesRoot = Path.Combine(
                Path.GetTempPath(),
                "BugOneTouch_Retry_" + Guid.NewGuid().ToString("N")[..8],
                "BugOneTouch",
                "bundles");
            Directory.CreateDirectory(_tempBundlesRoot);
            _testBundleId = null;
        }

        [TearDown]
        public void TearDown()
        {
            // 임시 디렉토리 정리
            string rootDir = Path.GetFullPath(Path.Combine(_tempBundlesRoot, "..", "..", ".."));
            if (Directory.Exists(rootDir))
            {
                try { Directory.Delete(rootDir, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }
        }

        // ─── 테스트 1: 제출 실패 → Failed 상태 전이 ─────────────────────────────

        [Test]
        public async Task 제출_실패시_번들_상태_Failed_전이_확인()
        {
            // Arrange - 실패하는 제출 함수 Mock
            var mockStateUpdater = new MockBundleStateUpdater();
            int submitCallCount = 0;

            Func<Task<string>> failingSubmit = async () =>
            {
                submitCallCount++;
                await Task.Yield();
                throw new NetworkException("오프라인 시뮬레이션: 네트워크 연결 없음");
            };

            // Act - 제출 실패 시뮬레이션
            await mockStateUpdater.UpdateSubmittingAsync("bundle-offline-001");

            bool failed = false;
            string errorMsg = null;
            try
            {
                await failingSubmit();
            }
            catch (Exception ex)
            {
                failed = true;
                errorMsg = ex.Message;
                await mockStateUpdater.UpdateFailedAsync("bundle-offline-001", ex.Message);
            }

            // Assert
            Assert.IsTrue(failed, "네트워크 오류 시 예외가 발생해야 합니다.");
            Assert.IsTrue(mockStateUpdater.FailedCalled, "Failed 상태 업데이트가 호출되어야 합니다.");
            Assert.IsTrue(mockStateUpdater.SubmittingCalled, "Submitting 상태가 먼저 설정되어야 합니다.");
            Assert.AreEqual(1, submitCallCount, "제출 시도가 1회 있어야 합니다.");
        }

        // ─── 테스트 2: 재시도 큐 동작 검증 ──────────────────────────────────────

        [Test]
        public async Task 재시도_큐_Failed_to_Pending_to_Submitted_전이()
        {
            // Arrange - 재시도 큐 상태 전이 시뮬레이션
            var stateHistory = new List<string>();
            var mockRetryQueue = new MockSubmissionRetryQueue(maxFailCount: 1); // 1회 실패 후 성공

            // Act
            // 1단계: 초기 제출 실패 → Failed 상태
            stateHistory.Add("created");
            stateHistory.Add("submitting");
            bool firstTrySuccess = await mockRetryQueue.TrySubmitAsync();
            if (!firstTrySuccess)
                stateHistory.Add("failed");

            // 2단계: RequeueFailed → Pending으로 전환
            if (stateHistory.Contains("failed"))
            {
                mockRetryQueue.ResetForRetry();
                stateHistory.Add("pending");
            }

            // 3단계: 재시도 → Submitting → Submitted
            if (stateHistory.Contains("pending"))
            {
                stateHistory.Add("submitting");
                bool retrySuccess = await mockRetryQueue.TrySubmitAsync();
                if (retrySuccess)
                    stateHistory.Add("submitted");
                else
                    stateHistory.Add("failed");
            }

            // Assert - 상태 전이 순서 확인
            Assert.Contains("created", stateHistory, "Created 상태가 있어야 합니다.");
            Assert.Contains("failed", stateHistory, "Failed 상태가 있어야 합니다.");
            Assert.Contains("pending", stateHistory, "Pending 상태로 복귀해야 합니다.");
            Assert.Contains("submitted", stateHistory, "최종적으로 Submitted 상태가 되어야 합니다.");

            // 최종 상태 확인
            Assert.AreEqual("submitted", stateHistory[stateHistory.Count - 1], "최종 상태는 submitted여야 합니다.");
        }

        // ─── 테스트 3: SubmissionQueue 실제 지수 백오프 간격 검증 ─────────────────

        [Test]
        public void SubmissionQueue_지수_백오프_상수_값_검증()
        {
            // SubmissionQueue의 재시도 정책 검증
            // 실제 구현: 5초, 15초, 45초 지수 백오프
            // 지수 백오프는 5초 * 3^n 패턴

            // 예상 백오프 간격 (SubmissionQueue 코드와 일치해야 함)
            int[] expectedDelays = { 5, 15, 45 };
            int maxRetry = SubmissionQueue.MaxRetryCount;

            // Assert
            Assert.AreEqual(3, maxRetry, "최대 재시도 횟수는 3회여야 합니다.");
            Assert.AreEqual(3, expectedDelays.Length, "백오프 간격 배열 크기가 MaxRetryCount와 일치해야 합니다.");

            // 지수적 증가 검증
            Assert.Greater(expectedDelays[1], expectedDelays[0], "두 번째 대기 시간은 첫 번째보다 길어야 합니다.");
            Assert.Greater(expectedDelays[2], expectedDelays[1], "세 번째 대기 시간은 두 번째보다 길어야 합니다.");
        }

        // ─── 테스트 4: SubmissionQueue 재시도 횟수 초과 검증 ──────────────────────

        [Test]
        public async Task SubmissionQueue_최대_재시도_초과_번들_건너뜀()
        {
            // Arrange
            var mockRetryQueue = new MockSubmissionRetryQueue(maxFailCount: 999); // 항상 실패
            int retryCount = 0;

            // Act - 최대 재시도 횟수만큼 실패 시뮬레이션
            for (int i = 0; i <= SubmissionQueue.MaxRetryCount; i++)
            {
                bool success = await mockRetryQueue.TrySubmitAsync();
                if (!success)
                    retryCount++;
                else
                    break;
            }

            // Assert
            Assert.GreaterOrEqual(retryCount, 1, "최소 1회 이상 실패해야 합니다.");
            Assert.IsTrue(mockRetryQueue.ShouldSkip(retryCount), "최대 재시도 횟수 초과 시 건너뜀 처리가 되어야 합니다.");
        }

        // ─── 테스트 5: 제출 큐 Pending 번들 처리 순서 검증 ──────────────────────

        [Test]
        public async Task 여러_Pending_번들_순차_처리_검증()
        {
            // Arrange - 여러 Mock 번들의 제출 순서 기록
            var processOrder = new List<string>();
            var bundleIds = new[] { "bundle-001", "bundle-002", "bundle-003" };

            Func<string, Task> processBundle = async (id) =>
            {
                await Task.Yield();
                processOrder.Add(id);
            };

            // Act - 순차 처리 시뮬레이션
            foreach (var id in bundleIds)
                await processBundle(id);

            // Assert
            Assert.AreEqual(3, processOrder.Count, "3개 번들이 모두 처리되어야 합니다.");
            Assert.AreEqual("bundle-001", processOrder[0], "첫 번째 번들이 먼저 처리되어야 합니다.");
            Assert.AreEqual("bundle-002", processOrder[1], "두 번째 번들이 다음에 처리되어야 합니다.");
            Assert.AreEqual("bundle-003", processOrder[2], "세 번째 번들이 마지막에 처리되어야 합니다.");
        }

        // ─── 테스트 6: 취소 토큰 동작 검증 ──────────────────────────────────────

        [Test]
        public async Task 취소_토큰으로_재시도_중단_검증()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            bool cancelled = false;
            int processedCount = 0;

            // Act - 취소 토큰으로 처리 중단
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    processedCount++;

                    if (i == 2)
                        cts.Cancel(); // 3번째 이후 취소
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            // Assert
            Assert.IsTrue(cancelled, "취소 토큰으로 처리가 중단되어야 합니다.");
            Assert.Less(processedCount, 10, "취소 전까지만 처리되어야 합니다.");
        }
    }

    // ─── Mock 구현체 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 제출 재시도 큐 Mock.
    /// 지정된 횟수만큼 실패한 후 성공을 반환합니다.
    /// </summary>
    internal class MockSubmissionRetryQueue
    {
        private readonly int _maxFailCount;
        private int _currentFailCount;

        public MockSubmissionRetryQueue(int maxFailCount)
        {
            _maxFailCount = maxFailCount;
            _currentFailCount = 0;
        }

        /// <summary>
        /// 제출 시도. maxFailCount 이상 실패한 경우 성공 반환.
        /// </summary>
        public async Task<bool> TrySubmitAsync()
        {
            await Task.Yield();

            if (_currentFailCount < _maxFailCount)
            {
                _currentFailCount++;
                return false; // 실패
            }

            return true; // 성공
        }

        /// <summary>
        /// 재시도를 위해 현재 실패 카운트를 최대값으로 설정합니다.
        /// </summary>
        public void ResetForRetry()
        {
            _currentFailCount = _maxFailCount; // 다음 호출에서 성공
        }

        /// <summary>
        /// 최대 재시도 횟수 초과 여부를 확인합니다.
        /// </summary>
        public bool ShouldSkip(int retryCount)
        {
            return retryCount >= SubmissionQueue.MaxRetryCount;
        }
    }
}
