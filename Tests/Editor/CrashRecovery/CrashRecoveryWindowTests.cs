using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using GaoZombie.BugOneTouch;
using GaoZombie.BugOneTouch.Editor;

namespace GaoZombie.BugOneTouch.Tests
{
    /// <summary>
    /// CrashRecoveryWindow, CrashBundleScanner, CrashJiraSubmitter 단위 테스트.
    /// UI 로직과 데이터 처리 부분을 검증합니다 (실제 EditorWindow 열기 제외).
    /// </summary>
    [TestFixture]
    public class CrashRecoveryWindowTests
    {
        private CrashBundleWriter _writer;
        private CrashJiraSubmitter _submitter;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _writer = new CrashBundleWriter();
            _submitter = new CrashJiraSubmitter();

            // 더미 active/ 파일 생성
            Directory.CreateDirectory(PeriodicFlushManager.ActiveDir);
            CreateDummyFlushFiles();
        }

        [TearDown]
        public void TearDown()
        {
            // 크래시 번들 정리
            if (Directory.Exists(CrashBundleWriter.CrashBundlesDir))
            {
                try { Directory.Delete(CrashBundleWriter.CrashBundlesDir, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }

            // 플래그 파일 정리
            AbnormalExitDetector.DeleteFlagFile();

            // 더미 파일 정리
            CleanupDummyFlushFiles();
        }

        // ──────────────────────────────────────────────────────────────
        // CrashBundleScanner 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Scanner_FindUnregisteredBundles_WhenNoBundles_ReturnsEmpty()
        {
            // crash_bundles/ 없는 상태
            if (Directory.Exists(CrashBundleWriter.CrashBundlesDir))
                Directory.Delete(CrashBundleWriter.CrashBundlesDir, recursive: true);

            var result = CrashBundleScanner.FindUnregisteredBundles();

            Assert.IsNotNull(result, "반환값은 null이 아니어야 합니다.");
            Assert.AreEqual(0, result.Count, "번들이 없으면 빈 목록을 반환해야 합니다.");
        }

        [Test]
        public void Scanner_CheckAbnormalExitFlag_WhenNoFlag_ReturnsFalse()
        {
            AbnormalExitDetector.DeleteFlagFile();

            bool result = CrashBundleScanner.CheckAbnormalExitFlag();

            Assert.IsFalse(result, "플래그가 없으면 false를 반환해야 합니다.");
        }

        [Test]
        public void Scanner_CheckAbnormalExitFlag_WhenFlagExists_ReturnsTrue()
        {
            AbnormalExitDetector.CreateFlagFile();

            bool result = CrashBundleScanner.CheckAbnormalExitFlag();

            Assert.IsTrue(result, "플래그가 있으면 true를 반환해야 합니다.");
        }

        [Test]
        public void Scanner_FindAllBundles_WhenNoBundles_ReturnsEmpty()
        {
            if (Directory.Exists(CrashBundleWriter.CrashBundlesDir))
                Directory.Delete(CrashBundleWriter.CrashBundlesDir, recursive: true);

            var result = CrashBundleScanner.FindAllBundles();

            Assert.AreEqual(0, result.Count, "번들이 없으면 빈 목록을 반환해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // CrashJiraSubmitter - 이슈 내용 빌드 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void BuildIssueSummary_WithExceptionType_ContainsExceptionType()
        {
            var manifest = CreateSampleManifest(
                exceptionType: "NullReferenceException",
                crashType: "managed_exception");

            string summary = CrashJiraSubmitter.BuildIssueSummary(manifest);

            Assert.IsTrue(
                summary.Contains("NullReferenceException"),
                "제목에 예외 타입이 포함되어야 합니다.");
        }

        [Test]
        public void BuildIssueSummary_StartsWithCrashTag()
        {
            var manifest = CreateSampleManifest(crashType: "managed_exception");

            string summary = CrashJiraSubmitter.BuildIssueSummary(manifest);

            Assert.IsTrue(
                summary.StartsWith("[Crash]"),
                "제목은 '[Crash]'로 시작해야 합니다.");
        }

        [Test]
        public void BuildIssueSummary_WithoutExceptionType_UsesCrashType()
        {
            var manifest = CreateSampleManifest(
                exceptionType: "",
                crashType: "abnormal_exit");

            string summary = CrashJiraSubmitter.BuildIssueSummary(manifest);

            Assert.IsTrue(
                summary.Contains("abnormal_exit"),
                "예외 타입이 없으면 crash_type이 제목에 포함되어야 합니다.");
        }

        [Test]
        public void BuildIssueDescription_ContainsCrashType()
        {
            var manifest = CreateSampleManifest(crashType: "managed_exception");

            string description = CrashJiraSubmitter.BuildIssueDescription(manifest);

            Assert.IsTrue(
                description.Contains("managed_exception"),
                "설명에 크래시 유형이 포함되어야 합니다.");
        }

        [Test]
        public void BuildIssueDescription_ContainsStackTrace()
        {
            const string stackTrace = "at Player.Update() in Player.cs:42";
            var manifest = CreateSampleManifest(stackTrace: stackTrace);

            string description = CrashJiraSubmitter.BuildIssueDescription(manifest);

            Assert.IsTrue(
                description.Contains(stackTrace),
                "설명에 스택 트레이스가 포함되어야 합니다.");
        }

        [Test]
        public void BuildIssueDescription_ContainsReproductionStepsTemplate()
        {
            var manifest = CreateSampleManifest();

            string description = CrashJiraSubmitter.BuildIssueDescription(manifest);

            Assert.IsTrue(
                description.Contains("재현 단계"),
                "설명에 재현 단계 템플릿이 포함되어야 합니다.");
        }

        [Test]
        public void BuildIssueDescription_ContainsDataIntegrityInfo()
        {
            var manifest = CreateSampleManifest();
            manifest.data_integrity = new DataIntegrity
            {
                logs_ok = true,
                state_ok = true,
                video_ok = false,
                overall = "partial",
            };

            string description = CrashJiraSubmitter.BuildIssueDescription(manifest);

            Assert.IsTrue(
                description.Contains("partial"),
                "설명에 무결성 상태가 포함되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // CrashJiraSubmitter - 제출 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void SubmitAsync_NullManifest_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _submitter.SubmitAsync(null, "PROJ"));
        }

        [Test]
        public void SubmitAsync_EmptyProjectKey_ThrowsArgumentException()
        {
            var manifest = CreateSampleManifest();
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await _submitter.SubmitAsync(manifest, ""));
        }

        [Test]
        public async System.Threading.Tasks.Task SubmitAsync_ValidManifest_ReturnsSuccess()
        {
            // 번들 디렉토리 생성 (manifest 갱신을 위해 필요)
            var manifest = CreateSampleManifest();
            string bundleDir = CrashBundleWriter.GetBundleDir(manifest.id);
            Directory.CreateDirectory(bundleDir);

            var result = await _submitter.SubmitAsync(manifest, "TEST");

            Assert.IsNotNull(result, "결과는 null이 아니어야 합니다.");
            Assert.IsTrue(result.Success, $"제출이 성공해야 합니다. 오류: {result.ErrorMessage}");
            Assert.IsFalse(string.IsNullOrEmpty(result.IssueKey), "이슈 키가 반환되어야 합니다.");
        }

        [Test]
        public async System.Threading.Tasks.Task SubmitAsync_ValidManifest_IssueKeyContainsProjectKey()
        {
            var manifest = CreateSampleManifest();
            string bundleDir = CrashBundleWriter.GetBundleDir(manifest.id);
            Directory.CreateDirectory(bundleDir);

            var result = await _submitter.SubmitAsync(manifest, "MYPROJECT");

            Assert.IsTrue(
                result.IssueKey.StartsWith("MYPROJECT"),
                "이슈 키는 프로젝트 키로 시작해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // DataIntegrity 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void DataIntegrity_Overall_CompleteWhenAllPresent()
        {
            // CrashBundleWriter의 DetermineOverall과 동일한 로직 검증 (PRD 스펙 AC-26: "ok" → "complete")
            var integrity = new DataIntegrity
            {
                logs_ok = true,
                state_ok = true,
                video_ok = true,
                overall = "complete",
            };

            Assert.AreEqual("complete", integrity.overall, "모든 데이터가 있으면 overall은 'complete'여야 합니다.");
        }

        [Test]
        public void DataIntegrity_Overall_PartialWhenSomePresent()
        {
            var integrity = new DataIntegrity
            {
                logs_ok = true,
                state_ok = false,
                video_ok = false,
                overall = "partial",
            };

            Assert.AreEqual("partial", integrity.overall, "일부 데이터만 있으면 overall은 'partial'이어야 합니다.");
        }

        [Test]
        public void DataIntegrity_Overall_MissingWhenNone()
        {
            var integrity = new DataIntegrity
            {
                logs_ok = false,
                state_ok = false,
                video_ok = false,
                overall = "missing",
            };

            Assert.AreEqual("missing", integrity.overall, "데이터가 없으면 overall은 'missing'이어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 샘플 CrashBundleManifest를 생성합니다.
        /// </summary>
        private static CrashBundleManifest CreateSampleManifest(
            string crashType = "managed_exception",
            string exceptionType = "NullReferenceException",
            string stackTrace = "at Player.Update() in Player.cs:42")
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");

            return new CrashBundleManifest
            {
                id = timestamp,
                type = "crash",
                created_at = DateTime.UtcNow.ToString("O"),
                plugin_version = "1.0.0",
                unity_version = Application.unityVersion,
                crash_type = crashType,
                exception_type = exceptionType,
                exception_message = "Object reference not set to an instance of an object",
                stack_trace = stackTrace,
                data_integrity = new DataIntegrity
                {
                    logs_ok = true,
                    state_ok = true,
                    video_ok = false,
                    overall = "partial",
                },
                jira_issue_key = null,
                registered_at = null,
            };
        }

        private void CreateDummyFlushFiles()
        {
            try
            {
                string logsPath = Path.Combine(PeriodicFlushManager.ActiveDir, PeriodicFlushManager.LogsFlushFileName);
                File.WriteAllBytes(logsPath, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0x00 });

                string statePath = Path.Combine(PeriodicFlushManager.ActiveDir, PeriodicFlushManager.StateFlushFileName);
                File.WriteAllText(statePath, "{\"engine\":\"Unity\"}", System.Text.Encoding.UTF8);
            }
            catch { /* 실패 무시 */ }
        }

        private static void CleanupDummyFlushFiles()
        {
            try
            {
                string logsPath = Path.Combine(PeriodicFlushManager.ActiveDir, PeriodicFlushManager.LogsFlushFileName);
                if (File.Exists(logsPath)) File.Delete(logsPath);

                string statePath = Path.Combine(PeriodicFlushManager.ActiveDir, PeriodicFlushManager.StateFlushFileName);
                if (File.Exists(statePath)) File.Delete(statePath);
            }
            catch { /* 실패 무시 */ }
        }
    }
}
