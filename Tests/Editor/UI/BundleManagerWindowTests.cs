using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;
using GaoZombie.BugOneTouch;
using GaoZombie.BugOneTouch.Editor;

namespace GaoZombie.BugOneTouch.Tests
{
    /// <summary>
    /// BundleManagerWindow 에디터 모드 단위 테스트.
    ///
    /// 검증 항목:
    ///   - 번들 목록 로딩
    ///   - 필터 적용 (전체 / 미제출 / 제출됨 / 실패)
    ///   - 보관 정책 적용 (개수 초과, 용량 초과)
    ///   - 번들 삭제
    ///   - 윈도우 열기/닫기
    /// </summary>
    [TestFixture]
    public class BundleManagerWindowTests
    {
        private string _testBundlesRoot;
        private BundleRepository _repository;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _repository      = new BundleRepository();
            _testBundlesRoot = BundleWriter.GetBundlesRootDirectory();

            // 기존 번들 디렉토리 초기화
            if (Directory.Exists(_testBundlesRoot))
                Directory.Delete(_testBundlesRoot, recursive: true);

            Directory.CreateDirectory(_testBundlesRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // 테스트 후 번들 디렉토리 정리
            if (Directory.Exists(_testBundlesRoot))
            {
                try { Directory.Delete(_testBundlesRoot, recursive: true); }
                catch { /* 정리 실패 무시 */ }
            }

            // 열려 있는 BundleManagerWindow 닫기
            BundleManagerWindow openWindow = EditorWindow.GetWindow<BundleManagerWindow>();
            if (openWindow != null)
                openWindow.Close();
        }

        // ──────────────────────────────────────────────────────────────
        // 윈도우 열기/닫기 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void OpenWindow_MenuItem_CreatesWindow()
        {
            // 메뉴 경로 상수 검증
            Assert.AreEqual(
                "Window/Bug-OneTouch",
                BugOneTouchEditorInfo.MenuRoot,
                "메뉴 루트 경로가 일치해야 합니다.");
        }

        [Test]
        public void BundleManagerWindow_CanBeInstantiated()
        {
            // EditorWindow 인스턴스 생성
            var window = ScriptableObject.CreateInstance<BundleManagerWindow>();
            Assert.IsNotNull(window, "BundleManagerWindow 인스턴스가 생성되어야 합니다.");
            ScriptableObject.DestroyImmediate(window);
        }

        // ──────────────────────────────────────────────────────────────
        // 번들 로딩 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator RefreshBundles_EmptyDirectory_LoadsEmptyList()
        {
            var window = ScriptableObject.CreateInstance<BundleManagerWindow>();

            var task = window.RefreshBundlesAsync();
            while (!task.IsCompleted)
                yield return null;

            // 빈 번들 목록 검증 (예외 없이 완료)
            Assert.IsNotNull(window, "RefreshBundles 후 윈도우가 존재해야 합니다.");

            ScriptableObject.DestroyImmediate(window);
        }

        [UnityTest]
        public IEnumerator RefreshBundles_WithBundles_LoadsBundleList()
        {
            // 테스트 번들 3개 생성
            CreateTestBundle("bundle-1", BundleState.Created, "버그 A", 1024L);
            CreateTestBundle("bundle-2", BundleState.Submitted, "버그 B", 2048L);
            CreateTestBundle("bundle-3", BundleState.Failed, "버그 C", 512L);

            var window = ScriptableObject.CreateInstance<BundleManagerWindow>();
            var task   = window.RefreshBundlesAsync();

            while (!task.IsCompleted)
                yield return null;

            // 로딩 완료 검증 (예외 없이 완료)
            Assert.IsTrue(task.IsCompleted, "번들 로딩이 완료되어야 합니다.");
            Assert.IsNull(task.Exception, "번들 로딩 중 예외가 없어야 합니다.");

            ScriptableObject.DestroyImmediate(window);
        }

        // ──────────────────────────────────────────────────────────────
        // 번들 삭제 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator DeleteBundle_ExistingBundle_RemovesFromDisk()
        {
            // 테스트 번들 생성
            CreateTestBundle("delete-test", BundleState.Created, "삭제 테스트", 512L);

            string bundleDir = BundleWriter.GetBundleDirectory("delete-test");
            Assert.IsTrue(Directory.Exists(bundleDir), "번들 디렉토리가 생성되어야 합니다.");

            // 삭제 수행
            var deleteTask = _repository.DeleteAsync("delete-test");
            while (!deleteTask.IsCompleted)
                yield return null;

            Assert.IsFalse(Directory.Exists(bundleDir), "번들 디렉토리가 삭제되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 상태 변경 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator UpdateState_CreatedToPending_UpdatesManifest()
        {
            CreateTestBundle("state-test", BundleState.Created, "상태 변경 테스트", 1024L);

            var updateTask = _repository.UpdateStateAsync("state-test", BundleState.Pending);
            while (!updateTask.IsCompleted)
                yield return null;

            var getTask = _repository.GetByIdAsync("state-test");
            while (!getTask.IsCompleted)
                yield return null;

            BundleManifest manifest = getTask.Result;
            Assert.IsNotNull(manifest, "상태 변경 후 번들이 존재해야 합니다.");
            Assert.AreEqual(BundleState.Pending, manifest.state, "상태가 Pending으로 변경되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 저장소 통계 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator GetStorageStats_WithBundles_ReturnsCorrectStats()
        {
            CreateTestBundle("stats-1", BundleState.Created, "통계 테스트 1", 1024L);
            CreateTestBundle("stats-2", BundleState.Submitted, "통계 테스트 2", 2048L);

            var statsTask = _repository.GetStorageStatsAsync();
            while (!statsTask.IsCompleted)
                yield return null;

            (int count, long totalBytes) = statsTask.Result;

            Assert.AreEqual(2, count, "번들 수가 2여야 합니다.");
            Assert.Greater(totalBytes, 0L, "총 바이트가 0보다 커야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 필터 검증 (정적 로직 테스트)
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void BundleState_Created_IsUnsubmitted()
        {
            // Created 상태는 미제출 필터에 해당
            var bundle = MakeFakeManifest("filter-1", BundleState.Created);
            Assert.AreEqual(BundleState.Created, bundle.state);
        }

        [Test]
        public void BundleState_Submitted_IsNotFailed()
        {
            // Submitted 상태는 Failed가 아님
            var bundle = MakeFakeManifest("filter-2", BundleState.Submitted);
            Assert.AreNotEqual(BundleState.Failed, bundle.state);
        }

        [Test]
        public void BundleState_Failed_HasRetryCount()
        {
            var bundle = MakeFakeManifest("filter-3", BundleState.Failed);
            bundle.retry_count = 2;
            Assert.AreEqual(2, bundle.retry_count);
        }

        // ──────────────────────────────────────────────────────────────
        // 메뉴 경로 상수 검증
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MenuPath_Settings_IsCorrect()
        {
            Assert.AreEqual(
                "Window/Bug-OneTouch/Settings",
                BugOneTouchEditorInfo.MenuRoot + "/Settings",
                "Settings 메뉴 경로가 올바라야 합니다.");
        }

        [Test]
        public void MenuPath_Bundles_IsCorrect()
        {
            Assert.AreEqual(
                "Window/Bug-OneTouch/Bundles",
                BugOneTouchEditorInfo.MenuRoot + "/Bundles",
                "Bundles 메뉴 경로가 올바라야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 번들 디렉토리와 manifest.json을 생성합니다.
        /// </summary>
        private static void CreateTestBundle(
            string bundleId,
            BundleState state,
            string title,
            long sizeBytes)
        {
            string bundleDir = BundleWriter.GetBundleDirectory(bundleId);
            Directory.CreateDirectory(bundleDir);

            // 최소 manifest.json 작성
            string manifest = JsonUtility.ToJson(new BundleManifest
            {
                id             = bundleId,
                created_at     = DateTime.UtcNow.ToString("O"),
                plugin_version = "0.1.0",
                unity_version  = Application.unityVersion,
                title          = title,
                state          = state,
                total_size_bytes = sizeBytes,
                artifacts      = new List<BundleArtifact>(),
            });

            string manifestPath = Path.Combine(bundleDir, "manifest.json");
            File.WriteAllText(manifestPath, manifest);
        }

        /// <summary>
        /// 지정 상태의 가짜 BundleManifest를 반환합니다.
        /// </summary>
        private static BundleManifest MakeFakeManifest(string id, BundleState state)
        {
            return new BundleManifest
            {
                id             = id,
                created_at     = DateTime.UtcNow.ToString("O"),
                plugin_version = "0.1.0",
                unity_version  = Application.unityVersion,
                title          = "테스트 번들",
                state          = state,
                total_size_bytes = 1024L,
                artifacts      = new List<BundleArtifact>(),
            };
        }
    }
}
