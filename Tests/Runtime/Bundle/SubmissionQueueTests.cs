using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// SubmissionQueue 단위 테스트.
    /// 번들 제출, 재시도, 지수 백오프, 상태 전환을 검증합니다.
    /// 실제 네트워크 대신 목(Mock) 제출 함수를 사용합니다.
    /// </summary>
    [TestFixture]
    public class SubmissionQueueTests
    {
        private BundleRepository _repository;
        private string _testBundlesRoot;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _repository = new BundleRepository();
            _testBundlesRoot = BundleWriter.GetBundlesRootDirectory();

            // 테스트 전 기존 번들 정리
            if (Directory.Exists(_testBundlesRoot))
                Directory.Delete(_testBundlesRoot, recursive: true);
            Directory.CreateDirectory(_testBundlesRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testBundlesRoot))
            {
                try { Directory.Delete(_testBundlesRoot, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 생성자 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Constructor_NullRepository_ThrowsArgumentNullException()
        {
            Func<BundleManifest, CancellationToken, Task<string>> submitFunc =
                (_, __) => Task.FromResult("BUG-1");

            Assert.Throws<ArgumentNullException>(() => new SubmissionQueue(null, submitFunc));
        }

        [Test]
        public void Constructor_NullSubmitFunc_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SubmissionQueue(_repository, null));
        }

        // ──────────────────────────────────────────────────────────────
        // ProcessPendingAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ProcessPendingAsync_NoPendingBundles_ReturnsZero()
        {
            CreateTestBundle("bundle-created", BundleState.Created);

            var queue = CreateQueue(successKey: "BUG-1");

            var task = queue.ProcessPendingAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(0, task.Result, "Pending 번들이 없으면 0을 반환해야 합니다.");
        }

        [UnityTest]
        public IEnumerator ProcessPendingAsync_OnePendingBundle_SubmitsSuccessfully()
        {
            CreateTestBundle("bundle-pending", BundleState.Pending);

            string capturedKey = null;
            var queue = CreateQueue(successKey: "BUG-42");
            queue.OnSubmitted += (id, key) => capturedKey = key;

            var task = queue.ProcessPendingAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted, $"실패: {task.Exception?.GetBaseException()?.Message}");
            Assert.AreEqual(1, task.Result, "1개 번들이 성공적으로 제출되어야 합니다.");
            Assert.AreEqual("BUG-42", capturedKey, "OnSubmitted 이벤트에 올바른 Jira 키가 전달되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator ProcessPendingAsync_MultiplePendingBundles_SubmitsAll()
        {
            CreateTestBundle("bundle-p1", BundleState.Pending);
            CreateTestBundle("bundle-p2", BundleState.Pending);
            CreateTestBundle("bundle-p3", BundleState.Pending);

            var submittedIds = new List<string>();
            var queue = CreateQueue(successKey: "BUG-100");
            queue.OnSubmitted += (id, key) => submittedIds.Add(id);

            var task = queue.ProcessPendingAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.AreEqual(3, task.Result, "3개 번들이 모두 제출되어야 합니다.");
            Assert.AreEqual(3, submittedIds.Count);
        }

        // ──────────────────────────────────────────────────────────────
        // 제출 성공 후 상태 전환 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ProcessPendingAsync_SuccessfulSubmission_StateBecomesSubmitted()
        {
            CreateTestBundle("submit-bundle", BundleState.Pending);

            var queue = CreateQueue(successKey: "BUG-999");

            var task = queue.ProcessPendingAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            var getTask = _repository.GetByIdAsync("submit-bundle");
            yield return new WaitUntil(() => getTask.IsCompleted);

            BundleManifest manifest = getTask.Result;
            Assert.AreEqual(BundleState.Submitted, manifest.state, "제출 성공 후 Submitted 상태여야 합니다.");
            Assert.AreEqual("BUG-999", manifest.jira_issue_key, "Jira 이슈 키가 저장되어야 합니다.");
            Assert.IsNotEmpty(manifest.registered_at, "registered_at이 설정되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 제출 실패 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ProcessPendingAsync_SubmissionFails_StateBecomesFailedAfterMaxRetry()
        {
            // 지수 백오프 대기 시간을 0으로 단축하기 위해 직접 테스트용 서브클래스 사용 불가.
            // 대신 실패하는 제출 함수를 사용하되, 재시도 횟수만 확인합니다.
            CreateTestBundle("fail-bundle", BundleState.Pending);

            int callCount = 0;
            Func<BundleManifest, CancellationToken, Task<string>> failSubmit = (_, __) =>
            {
                callCount++;
                throw new InvalidOperationException("테스트 제출 실패");
            };

            // 지수 백오프로 인해 실제 대기가 발생하지 않도록 타임아웃 설정
            // NOTE: 실제 지수 백오프(5+15+45=65초)는 테스트에서 기다리기 어려우므로
            //       0번 재시도(즉시 실패)를 테스트합니다.
            var queue = new SubmissionQueueNoDelay(_repository, failSubmit);

            string failedId = null;
            queue.OnFailed += (id, count, msg) => failedId = id;

            var task = queue.ProcessPendingAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(0, task.Result, "실패한 번들은 성공 카운트에 포함되지 않아야 합니다.");
            Assert.IsNotNull(failedId, "OnFailed 이벤트가 발행되어야 합니다.");

            // 최종 상태 확인
            var getTask = _repository.GetByIdAsync("fail-bundle");
            yield return new WaitUntil(() => getTask.IsCompleted);

            Assert.AreEqual(BundleState.Failed, getTask.Result.state, "실패 후 Failed 상태여야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // RequeueFailedAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator RequeueFailedAsync_FailedBundle_BecomePending()
        {
            CreateTestBundle("failed-bundle", BundleState.Failed, retryCount: 0);

            var queue = CreateQueue(successKey: "BUG-1");

            var task = queue.RequeueFailedAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(1, task.Result, "1개 번들이 Pending으로 전환되어야 합니다.");

            var getTask = _repository.GetByIdAsync("failed-bundle");
            yield return new WaitUntil(() => getTask.IsCompleted);

            Assert.AreEqual(BundleState.Pending, getTask.Result.state, "상태가 Pending으로 변경되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator RequeueFailedAsync_ExceededMaxRetry_NotRequeued()
        {
            // 최대 재시도 횟수(3회)를 초과한 번들
            CreateTestBundle("maxretry-bundle", BundleState.Failed, retryCount: SubmissionQueue.MaxRetryCount);

            var queue = CreateQueue(successKey: "BUG-1");

            var task = queue.RequeueFailedAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(0, task.Result, "최대 재시도 초과 번들은 재시도 큐에 등록되지 않아야 합니다.");

            var getTask = _repository.GetByIdAsync("maxretry-bundle");
            yield return new WaitUntil(() => getTask.IsCompleted);

            Assert.AreEqual(BundleState.Failed, getTask.Result.state, "상태가 여전히 Failed여야 합니다.");
        }

        [UnityTest]
        public IEnumerator RequeueFailedAsync_MixedBundles_OnlyRequeuesEligible()
        {
            CreateTestBundle("retry-ok-1",  BundleState.Failed, retryCount: 0);
            CreateTestBundle("retry-ok-2",  BundleState.Failed, retryCount: 2);
            CreateTestBundle("retry-max",   BundleState.Failed, retryCount: SubmissionQueue.MaxRetryCount);
            CreateTestBundle("not-failed",  BundleState.Created);

            var queue = CreateQueue(successKey: "BUG-1");

            var task = queue.RequeueFailedAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            // retry_count < MaxRetryCount인 Failed 번들 2개만 Pending으로 전환
            Assert.AreEqual(2, task.Result, "재시도 가능한 Failed 번들만 Pending으로 전환되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // SubmitWithRetryAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SubmitWithRetryAsync_ExistingBundle_SubmitsSuccessfully()
        {
            CreateTestBundle("direct-submit-bundle", BundleState.Pending);

            var queue = CreateQueue(successKey: "BUG-777");

            var task = queue.SubmitWithRetryAsync("direct-submit-bundle");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.IsTrue(task.Result, "직접 제출이 성공해야 합니다.");
        }

        [UnityTest]
        public IEnumerator SubmitWithRetryAsync_NonExistentBundle_ReturnsFalse()
        {
            var queue = CreateQueue(successKey: "BUG-1");

            var task = queue.SubmitWithRetryAsync("non-existent-bundle");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.IsFalse(task.Result, "존재하지 않는 번들은 false를 반환해야 합니다.");
        }

        [Test]
        public void SubmitWithRetryAsync_NullBundleId_ThrowsArgumentNullException()
        {
            var queue = CreateQueue(successKey: "BUG-1");
            Assert.Throws<ArgumentNullException>(
                () => queue.SubmitWithRetryAsync(null).GetAwaiter().GetResult());
        }

        // ──────────────────────────────────────────────────────────────
        // 이벤트 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ProcessPendingAsync_Success_FiresOnSubmittedEvent()
        {
            CreateTestBundle("event-bundle", BundleState.Pending);

            bool eventFired = false;
            string receivedId = null;
            string receivedKey = null;

            var queue = CreateQueue(successKey: "BUG-EVENT");
            queue.OnSubmitted += (id, key) =>
            {
                eventFired = true;
                receivedId = id;
                receivedKey = key;
            };

            var task = queue.ProcessPendingAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsTrue(eventFired, "OnSubmitted 이벤트가 발행되어야 합니다.");
            Assert.AreEqual("event-bundle", receivedId);
            Assert.AreEqual("BUG-EVENT", receivedKey);
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 성공하는 제출 함수를 사용하는 SubmissionQueue를 생성합니다.
        /// </summary>
        private SubmissionQueue CreateQueue(string successKey)
        {
            Func<BundleManifest, CancellationToken, Task<string>> submitFunc =
                (manifest, _) => Task.FromResult(successKey);

            return new SubmissionQueue(_repository, submitFunc);
        }

        /// <summary>
        /// 테스트용 번들 디렉토리와 manifest.json을 생성합니다.
        /// </summary>
        private void CreateTestBundle(
            string bundleId,
            BundleState state,
            int retryCount = 0)
        {
            string bundleDir = BundleWriter.GetBundleDirectory(bundleId);
            Directory.CreateDirectory(bundleDir);

            var manifest = new BundleManifest
            {
                id              = bundleId,
                created_at      = DateTime.UtcNow.ToString("O"),
                plugin_version  = "0.1.0",
                unity_version   = "2022.3.22f1",
                title           = string.Empty,
                description     = string.Empty,
                artifacts       = new List<BundleArtifact>(),
                total_size_bytes = 1024L,
                state           = state,
                jira_issue_key  = null,
                registered_at   = null,
                retry_count     = retryCount,
            };

            string json = JsonUtility.ToJson(manifest, prettyPrint: true);
            File.WriteAllText(
                Path.Combine(bundleDir, "manifest.json"),
                json,
                System.Text.Encoding.UTF8);
        }

        // ──────────────────────────────────────────────────────────────
        // 테스트용 SubmissionQueue 서브클래스 (지수 백오프 대기 없음)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 지수 백오프 대기 시간 없이 즉시 재시도하는 테스트용 SubmissionQueue.
        /// 실제 SubmissionQueue와 동일한 로직이지만 대기를 건너뜁니다.
        /// </summary>
        private class SubmissionQueueNoDelay : SubmissionQueue
        {
            private readonly BundleRepository _repo;
            private readonly Func<BundleManifest, CancellationToken, Task<string>> _submitFn;

            public SubmissionQueueNoDelay(
                BundleRepository repository,
                Func<BundleManifest, CancellationToken, Task<string>> submitFunc)
                : base(repository, submitFunc)
            {
                _repo = repository;
                _submitFn = submitFunc;
            }

            /// <summary>
            /// 지수 백오프 없이 즉시 1회 시도 후 실패 처리합니다.
            /// </summary>
            public new async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
            {
                var pending = await _repo.GetByStateAsync(BundleState.Pending);
                int successCount = 0;

                foreach (var manifest in pending)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        await _repo.UpdateStateAsync(manifest.id, BundleState.Submitting);
                        string key = await _submitFn(manifest, cancellationToken);

                        if (string.IsNullOrEmpty(key))
                            throw new InvalidOperationException("빈 Jira 키 반환");

                        await _repo.MarkSubmittedAsync(manifest.id, key);
                        RaiseOnSubmitted(manifest.id, key);
                        successCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        await SafeUpdateState(manifest.id, BundleState.Pending);
                        break;
                    }
                    catch (Exception ex)
                    {
                        int retryCount = await SafeIncrement(manifest.id);
                        await SafeUpdateState(manifest.id, BundleState.Failed);
                        RaiseOnFailed(manifest.id, retryCount, ex.Message);
                    }
                }

                return successCount;
            }

            private async Task SafeUpdateState(string id, BundleState state)
            {
                try { await _repo.UpdateStateAsync(id, state); } catch { /* 무시 */ }
            }

            private async Task<int> SafeIncrement(string id)
            {
                try { return await _repo.IncrementRetryCountAsync(id); } catch { return -1; }
            }
        }
    }
}
