using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// BundleRepository 단위 테스트.
    /// 번들 목록 조회, 상태 변경, 삭제 기능을 검증합니다.
    /// </summary>
    [TestFixture]
    public class BundleRepositoryTests
    {
        private BundleRepository _repository;
        private string _testBundlesRoot;
        private string _originalPersistentDataPath;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _repository = new BundleRepository();

            // 테스트 번들 루트 디렉토리 (Application.persistentDataPath 기반)
            _testBundlesRoot = BundleWriter.GetBundlesRootDirectory();

            // 테스트 전 기존 번들 정리
            if (Directory.Exists(_testBundlesRoot))
                Directory.Delete(_testBundlesRoot, recursive: true);

            Directory.CreateDirectory(_testBundlesRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // 테스트 후 생성된 번들 정리
            if (Directory.Exists(_testBundlesRoot))
            {
                try { Directory.Delete(_testBundlesRoot, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // GetAllAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GetAllAsync_EmptyDirectory_ReturnsEmptyList()
        {
            var task = _repository.GetAllAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(0, task.Result.Count, "빈 디렉토리는 빈 목록을 반환해야 합니다.");
        }

        [UnityTest]
        public IEnumerator GetAllAsync_WithBundles_ReturnsAllBundles()
        {
            // 3개의 번들 생성
            CreateTestBundle("bundle-001", BundleState.Created);
            CreateTestBundle("bundle-002", BundleState.Pending);
            CreateTestBundle("bundle-003", BundleState.Submitted);

            var task = _repository.GetAllAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.AreEqual(3, task.Result.Count, "3개의 번들이 반환되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator GetAllAsync_SortedByCreatedAt_OldestFirst()
        {
            // 타임스탬프가 다른 번들 생성
            CreateTestBundle("bundle-A", BundleState.Created, "2024-01-01T10:00:00.000Z");
            CreateTestBundle("bundle-B", BundleState.Created, "2024-01-03T10:00:00.000Z");
            CreateTestBundle("bundle-C", BundleState.Created, "2024-01-02T10:00:00.000Z");

            var task = _repository.GetAllAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            List<BundleManifest> results = task.Result;
            Assert.AreEqual(3, results.Count);

            // 오름차순 정렬 확인 (가장 오래된 것이 앞)
            Assert.IsTrue(
                string.Compare(results[0].created_at, results[1].created_at, StringComparison.Ordinal) <= 0,
                "첫 번째 항목이 두 번째보다 같거나 오래되어야 합니다.");
            Assert.IsTrue(
                string.Compare(results[1].created_at, results[2].created_at, StringComparison.Ordinal) <= 0,
                "두 번째 항목이 세 번째보다 같거나 오래되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator GetAllAsync_InvalidManifest_SkipsInvalidBundles()
        {
            // 유효한 번들 1개
            CreateTestBundle("bundle-valid", BundleState.Created);

            // manifest.json이 없는 번들 디렉토리
            string emptyBundleDir = Path.Combine(_testBundlesRoot, "bundle-no-manifest");
            Directory.CreateDirectory(emptyBundleDir);

            // manifest.json이 손상된 번들
            string corruptedBundleDir = Path.Combine(_testBundlesRoot, "bundle-corrupted");
            Directory.CreateDirectory(corruptedBundleDir);
            File.WriteAllText(Path.Combine(corruptedBundleDir, "manifest.json"), "{ invalid json <<<");

            var task = _repository.GetAllAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            // 유효한 번들만 반환
            Assert.AreEqual(1, task.Result.Count, "유효하지 않은 번들은 건너뛰어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // GetByStateAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GetByStateAsync_FiltersPendingBundles()
        {
            CreateTestBundle("bundle-created",  BundleState.Created);
            CreateTestBundle("bundle-pending1", BundleState.Pending);
            CreateTestBundle("bundle-pending2", BundleState.Pending);
            CreateTestBundle("bundle-failed",   BundleState.Failed);

            var task = _repository.GetByStateAsync(BundleState.Pending);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.AreEqual(2, task.Result.Count, "Pending 번들이 2개 반환되어야 합니다.");
            foreach (var bundle in task.Result)
                Assert.AreEqual(BundleState.Pending, bundle.state);
        }

        [UnityTest]
        public IEnumerator GetByStateAsync_NoMatchingBundles_ReturnsEmptyList()
        {
            CreateTestBundle("bundle-created", BundleState.Created);

            var task = _repository.GetByStateAsync(BundleState.Submitted);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.AreEqual(0, task.Result.Count, "일치하는 상태가 없으면 빈 목록을 반환해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // GetByIdAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GetByIdAsync_ExistingBundle_ReturnsManifest()
        {
            CreateTestBundle("test-bundle-id", BundleState.Pending);

            var task = _repository.GetByIdAsync("test-bundle-id");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotNull(task.Result, "존재하는 번들은 manifest를 반환해야 합니다.");
            Assert.AreEqual("test-bundle-id", task.Result.id);
        }

        [UnityTest]
        public IEnumerator GetByIdAsync_NonExistentBundle_ReturnsNull()
        {
            var task = _repository.GetByIdAsync("non-existent-id");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNull(task.Result, "존재하지 않는 번들은 null을 반환해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // UpdateStateAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator UpdateStateAsync_ChangesStateInManifest()
        {
            CreateTestBundle("state-test-bundle", BundleState.Created);

            var updateTask = _repository.UpdateStateAsync("state-test-bundle", BundleState.Pending);
            yield return new WaitUntil(() => updateTask.IsCompleted);

            Assert.IsFalse(updateTask.IsFaulted, $"UpdateStateAsync 실패: {updateTask.Exception?.GetBaseException()?.Message}");

            // 상태가 변경되었는지 확인
            var getTask = _repository.GetByIdAsync("state-test-bundle");
            yield return new WaitUntil(() => getTask.IsCompleted);

            Assert.AreEqual(BundleState.Pending, getTask.Result.state, "상태가 Pending으로 변경되어야 합니다.");
        }

        [Test]
        public void UpdateStateAsync_NullBundleId_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await _repository.UpdateStateAsync(null, BundleState.Pending));
        }

        [UnityTest]
        public IEnumerator UpdateStateAsync_NonExistentBundle_ThrowsFileNotFoundException()
        {
            bool threw = false;
            var task = _repository.UpdateStateAsync("non-existent", BundleState.Pending);
            yield return new WaitUntil(() => task.IsCompleted);

            threw = task.IsFaulted;
            Assert.IsTrue(threw, "존재하지 않는 번들 업데이트 시 예외가 발생해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // MarkSubmittedAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator MarkSubmittedAsync_SetsJiraIssueKeyAndRegisteredAt()
        {
            CreateTestBundle("submit-test-bundle", BundleState.Submitting);

            var task = _repository.MarkSubmittedAsync("submit-test-bundle", "BUG-123");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);

            var getTask = _repository.GetByIdAsync("submit-test-bundle");
            yield return new WaitUntil(() => getTask.IsCompleted);

            BundleManifest manifest = getTask.Result;
            Assert.AreEqual(BundleState.Submitted, manifest.state);
            Assert.AreEqual("BUG-123", manifest.jira_issue_key);
            Assert.IsNotEmpty(manifest.registered_at, "registered_at이 설정되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // IncrementRetryCountAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator IncrementRetryCountAsync_IncrementsCount()
        {
            CreateTestBundle("retry-test-bundle", BundleState.Failed, retryCount: 0);

            var task = _repository.IncrementRetryCountAsync("retry-test-bundle");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(1, task.Result, "재시도 횟수가 1 증가해야 합니다.");

            // 다시 증가
            var task2 = _repository.IncrementRetryCountAsync("retry-test-bundle");
            yield return new WaitUntil(() => task2.IsCompleted);

            Assert.AreEqual(2, task2.Result, "재시도 횟수가 2로 증가해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // DeleteAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator DeleteAsync_ExistingBundle_RemovesDirectory()
        {
            CreateTestBundle("delete-test-bundle", BundleState.Failed);

            string bundleDir = BundleWriter.GetBundleDirectory("delete-test-bundle");
            Assert.IsTrue(Directory.Exists(bundleDir), "삭제 전 번들 디렉토리가 존재해야 합니다.");

            var task = _repository.DeleteAsync("delete-test-bundle");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.IsFalse(Directory.Exists(bundleDir), "삭제 후 번들 디렉토리가 없어야 합니다.");
        }

        [UnityTest]
        public IEnumerator DeleteAsync_NonExistentBundle_DoesNotThrow()
        {
            LogAssert.ignoreFailingMessages = true;
            var task = _repository.DeleteAsync("non-existent-bundle");
            yield return new WaitUntil(() => task.IsCompleted);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(task.IsFaulted, "존재하지 않는 번들 삭제 시 예외가 발생하지 않아야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // GetStorageStatsAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GetStorageStatsAsync_WithBundles_ReturnsCorrectStats()
        {
            CreateTestBundle("stats-bundle-1", BundleState.Created, totalSizeBytes: 1024L);
            CreateTestBundle("stats-bundle-2", BundleState.Pending, totalSizeBytes: 2048L);

            var task = _repository.GetStorageStatsAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.AreEqual(2, task.Result.count, "번들 수가 2개여야 합니다.");
            Assert.AreEqual(3072L, task.Result.totalBytes, "총 크기가 3072바이트여야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 번들 디렉토리와 manifest.json을 생성합니다.
        /// </summary>
        private void CreateTestBundle(
            string bundleId,
            BundleState state,
            string createdAt = null,
            long totalSizeBytes = 1024L,
            int retryCount = 0)
        {
            string bundleDir = BundleWriter.GetBundleDirectory(bundleId);
            Directory.CreateDirectory(bundleDir);

            var manifest = new BundleManifest
            {
                id              = bundleId,
                created_at      = createdAt ?? DateTime.UtcNow.ToString("O"),
                plugin_version  = "0.1.0",
                unity_version   = "2022.3.22f1",
                title           = string.Empty,
                description     = string.Empty,
                artifacts       = new System.Collections.Generic.List<BundleArtifact>(),
                total_size_bytes = totalSizeBytes,
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
    }
}
