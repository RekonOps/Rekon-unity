using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.BugOneTouch;

namespace RekonOps.BugOneTouch.Tests
{
    /// <summary>
    /// CrashBundleWriter 단위 테스트.
    /// 크래시 번들 생성, 무결성 검증, 보존 정책을 검증합니다.
    /// </summary>
    [TestFixture]
    public class CrashBundleWriterTests
    {
        private CrashBundleWriter _writer;
        private string _tempActiveDir;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _writer = new CrashBundleWriter();

            // 임시 active/ 디렉토리를 실제 경로와 무관하게 준비
            // (BuildAsync는 PeriodicFlushManager.ActiveDir를 사용하므로
            //  테스트에서는 해당 경로에 더미 파일을 생성합니다)
            _tempActiveDir = PeriodicFlushManager.ActiveDir;
            Directory.CreateDirectory(_tempActiveDir);

            // 더미 플러시 파일 생성
            CreateDummyFlushFiles();
        }

        [TearDown]
        public void TearDown()
        {
            // 생성된 크래시 번들 정리
            if (Directory.Exists(CrashBundleWriter.CrashBundlesDir))
            {
                try { Directory.Delete(CrashBundleWriter.CrashBundlesDir, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }

            // active/ 디렉토리의 더미 파일 정리
            CleanupDummyFlushFiles();
        }

        // ──────────────────────────────────────────────────────────────
        // 경로 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CrashBundlesDir_IsUnderPersistentDataPath()
        {
            Assert.IsTrue(
                CrashBundleWriter.CrashBundlesDir.StartsWith(Application.persistentDataPath),
                "크래시 번들 루트 경로는 persistentDataPath 하위여야 합니다.");
        }

        [Test]
        public void CrashBundlesDir_ContainsCrashBundlesDirName()
        {
            Assert.IsTrue(
                CrashBundleWriter.CrashBundlesDir.EndsWith(CrashBundleWriter.CrashBundlesDirName),
                $"크래시 번들 루트는 '{CrashBundleWriter.CrashBundlesDirName}'으로 끝나야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // BuildAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator BuildAsync_ReturnsManifest()
        {
            var task = _writer.BuildAsync(crashType: "managed_exception");
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted, $"BuildAsync 실패: {task.Exception?.GetBaseException()?.Message}");
            Assert.IsNotNull(task.Result, "크래시 번들 매니페스트가 반환되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator BuildAsync_ManifestHasId()
        {
            var task = _writer.BuildAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(string.IsNullOrEmpty(task.Result.id), "번들 ID가 비어 있지 않아야 합니다.");
        }

        [UnityTest]
        public IEnumerator BuildAsync_ManifestTypeIsCrash()
        {
            var task = _writer.BuildAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.AreEqual("crash", task.Result.type, "번들 타입은 'crash'여야 합니다.");
        }

        [UnityTest]
        public IEnumerator BuildAsync_CreatesManifestJsonFile()
        {
            var task = _writer.BuildAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            string bundleDir = CrashBundleWriter.GetBundleDir(task.Result.id);
            string manifestPath = Path.Combine(bundleDir, "manifest.json");

            Assert.IsTrue(File.Exists(manifestPath), "manifest.json이 생성되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator BuildAsync_CreatesCrashInfoFile()
        {
            var task = _writer.BuildAsync(
                crashType: "managed_exception",
                exceptionType: "NullReferenceException",
                exceptionMessage: "Object reference not set",
                stackTrace: "at Player.Update()");
            yield return new WaitUntil(() => task.IsCompleted);

            string bundleDir = CrashBundleWriter.GetBundleDir(task.Result.id);
            string crashInfoPath = Path.Combine(bundleDir, "crash_info.json");

            Assert.IsTrue(File.Exists(crashInfoPath), "crash_info.json이 생성되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator BuildAsync_ManifestHasDataIntegrity()
        {
            var task = _writer.BuildAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotNull(task.Result.data_integrity, "data_integrity가 null이면 안 됩니다.");
            Assert.IsFalse(
                string.IsNullOrEmpty(task.Result.data_integrity.overall),
                "data_integrity.overall이 비어 있으면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator BuildAsync_WithLogsAndState_IntegrityIsOkOrPartial()
        {
            var task = _writer.BuildAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            var overall = task.Result.data_integrity.overall;
            Assert.IsTrue(
                overall == "ok" || overall == "partial",
                $"플러시 데이터가 있을 때 overall은 'ok' 또는 'partial'이어야 합니다. 실제: {overall}");
        }

        [UnityTest]
        public IEnumerator BuildAsync_ExceptionInfo_StoredInManifest()
        {
            string exType = "System.NullReferenceException";
            string exMsg = "Object reference not set to an instance of an object";
            string stack = "at Player.Update() in Player.cs:42";

            var task = _writer.BuildAsync(
                crashType: "managed_exception",
                exceptionType: exType,
                exceptionMessage: exMsg,
                stackTrace: stack);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.AreEqual(exType, task.Result.exception_type, "예외 타입이 매니페스트에 저장되어야 합니다.");
            Assert.AreEqual(exMsg, task.Result.exception_message, "예외 메시지가 매니페스트에 저장되어야 합니다.");
            Assert.AreEqual(stack, task.Result.stack_trace, "스택 트레이스가 매니페스트에 저장되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator BuildAsync_JiraFieldsInitiallyNull()
        {
            var task = _writer.BuildAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNull(task.Result.jira_issue_key, "초기에 jira_issue_key는 null이어야 합니다.");
            Assert.IsNull(task.Result.registered_at, "초기에 registered_at은 null이어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // ScanAllBundles 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ScanAllBundles_AfterBuild_ReturnsBundles()
        {
            var task = _writer.BuildAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            var bundles = CrashBundleWriter.ScanAllBundles();

            Assert.GreaterOrEqual(bundles.Count, 1, "번들 생성 후 ScanAllBundles()는 1개 이상을 반환해야 합니다.");
        }

        [UnityTest]
        public IEnumerator ScanAllBundles_SortedByCreatedAt()
        {
            // 여러 번들 생성
            var task1 = _writer.BuildAsync();
            yield return new WaitUntil(() => task1.IsCompleted);

            var task2 = _writer.BuildAsync();
            yield return new WaitUntil(() => task2.IsCompleted);

            var bundles = CrashBundleWriter.ScanAllBundles();

            Assert.GreaterOrEqual(bundles.Count, 2, "2개 이상의 번들이 있어야 합니다.");
            Assert.LessOrEqual(
                string.Compare(bundles[0].created_at, bundles[1].created_at, StringComparison.Ordinal),
                0,
                "번들은 created_at 오름차순으로 정렬되어야 합니다.");
        }

        [Test]
        public void ScanAllBundles_WhenNoBundles_ReturnsEmptyList()
        {
            // crash_bundles/ 디렉토리가 없거나 비어 있는 상태
            if (Directory.Exists(CrashBundleWriter.CrashBundlesDir))
                Directory.Delete(CrashBundleWriter.CrashBundlesDir, recursive: true);

            var bundles = CrashBundleWriter.ScanAllBundles();

            Assert.IsNotNull(bundles, "반환값은 null이 아니어야 합니다.");
            Assert.AreEqual(0, bundles.Count, "번들이 없으면 빈 리스트를 반환해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // CrashBundleRetentionPolicy 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void RetentionPolicy_Constructor_ValidArgs()
        {
            Assert.DoesNotThrow(() => new CrashBundleRetentionPolicy(maxBundles: 5, retentionDays: 7),
                "유효한 인수로 생성 시 예외가 없어야 합니다.");
        }

        [Test]
        public void RetentionPolicy_Constructor_InvalidMaxBundles_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CrashBundleRetentionPolicy(maxBundles: 0, retentionDays: 7),
                "maxBundles가 0이면 예외가 발생해야 합니다.");
        }

        [Test]
        public void RetentionPolicy_Constructor_InvalidRetentionDays_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CrashBundleRetentionPolicy(maxBundles: 5, retentionDays: 0),
                "retentionDays가 0이면 예외가 발생해야 합니다.");
        }

        [Test]
        public void RetentionPolicy_Properties_MatchConstructorArgs()
        {
            var policy = new CrashBundleRetentionPolicy(maxBundles: 7, retentionDays: 14);

            Assert.AreEqual(7, policy.MaxBundles, "MaxBundles가 생성자 인수와 일치해야 합니다.");
            Assert.AreEqual(14, policy.RetentionDays, "RetentionDays가 생성자 인수와 일치해야 합니다.");
        }

        [UnityTest]
        public IEnumerator RetentionPolicy_Apply_ExceedMaxBundles_DeletesOldest()
        {
            var policy = new CrashBundleRetentionPolicy(maxBundles: 2, retentionDays: 30);

            // 3개 번들 생성
            var task1 = _writer.BuildAsync();
            yield return new WaitUntil(() => task1.IsCompleted);
            string oldestId = task1.Result.id;

            var task2 = _writer.BuildAsync();
            yield return new WaitUntil(() => task2.IsCompleted);

            var task3 = _writer.BuildAsync();
            yield return new WaitUntil(() => task3.IsCompleted);

            // 정책 적용
            int deleted = policy.Apply();

            Assert.AreEqual(1, deleted, "최대 2개를 초과하여 1개가 삭제되어야 합니다.");

            string oldestDir = CrashBundleWriter.GetBundleDir(oldestId);
            Assert.IsFalse(Directory.Exists(oldestDir), "가장 오래된 번들이 삭제되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 더미 플러시 파일을 active/ 디렉토리에 생성합니다.
        /// </summary>
        private void CreateDummyFlushFiles()
        {
            try
            {
                // 더미 로그 ZIP (빈 파일이지만 존재해야 함)
                string logsPath = Path.Combine(_tempActiveDir, PeriodicFlushManager.LogsFlushFileName);
                File.WriteAllBytes(logsPath, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0x00 });

                // 더미 상태 JSON
                string statePath = Path.Combine(_tempActiveDir, PeriodicFlushManager.StateFlushFileName);
                File.WriteAllText(statePath,
                    "{\"engine\":\"Unity\",\"platform\":\"Editor\",\"captured_at\":\"" + DateTime.UtcNow.ToString("O") + "\"}",
                    System.Text.Encoding.UTF8);
            }
            catch
            {
                // 더미 파일 생성 실패는 테스트에서 허용
            }
        }

        /// <summary>
        /// 테스트 후 active/ 디렉토리의 더미 파일을 정리합니다.
        /// </summary>
        private void CleanupDummyFlushFiles()
        {
            try
            {
                string logsPath = Path.Combine(_tempActiveDir, PeriodicFlushManager.LogsFlushFileName);
                if (File.Exists(logsPath)) File.Delete(logsPath);

                string statePath = Path.Combine(_tempActiveDir, PeriodicFlushManager.StateFlushFileName);
                if (File.Exists(statePath)) File.Delete(statePath);
            }
            catch { /* 정리 실패는 무시 */ }
        }
    }
}
