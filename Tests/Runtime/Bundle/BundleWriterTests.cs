using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GaoZombie.BugOneTouch;

namespace GaoZombie.BugOneTouch.Tests
{
    /// <summary>
    /// BundleWriter 단위 테스트.
    /// 아티팩트 복사, manifest.json 생성, SHA-256 해시 계산을 검증합니다.
    /// </summary>
    [TestFixture]
    public class BundleWriterTests
    {
        private BundleWriter _writer;
        private ManifestGenerator _manifestGenerator;
        private string _tempSourceDir;
        private string _tempBundlesDir;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _manifestGenerator = new ManifestGenerator();
            _writer = new BundleWriter(_manifestGenerator);

            // 임시 소스 디렉토리 (캡처 결과 파일 저장)
            _tempSourceDir = Path.Combine(Path.GetTempPath(), "BundleWriterTests_src_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempSourceDir);

            // persistentDataPath 대신 임시 디렉토리 사용은 불가 (Application.persistentDataPath가 고정값)
            // 번들은 실제 persistentDataPath에 생성되므로 TearDown에서 정리합니다.
        }

        [TearDown]
        public void TearDown()
        {
            // 임시 소스 디렉토리 정리
            if (Directory.Exists(_tempSourceDir))
                Directory.Delete(_tempSourceDir, recursive: true);

            // 생성된 번들 디렉토리 정리
            string bundlesRoot = BundleWriter.GetBundlesRootDirectory();
            if (Directory.Exists(bundlesRoot))
            {
                try { Directory.Delete(bundlesRoot, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 생성자 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Constructor_NullManifestGenerator_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new BundleWriter(null));
        }

        // ──────────────────────────────────────────────────────────────
        // WriteAsync 기본 동작 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator WriteAsync_ValidCaptureResult_ReturnsBundleManifest()
        {
            var captureResult = CreateSampleCaptureResult();

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted, $"WriteAsync 실패: {task.Exception?.GetBaseException()?.Message}");
            Assert.IsNotNull(task.Result, "BundleManifest가 반환되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator WriteAsync_ValidCaptureResult_ManifestHasId()
        {
            var captureResult = CreateSampleCaptureResult();

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotEmpty(task.Result.id, "번들 ID가 비어 있지 않아야 합니다.");
            Assert.IsTrue(Guid.TryParse(task.Result.id, out _), "번들 ID는 GUID 형식이어야 합니다.");
        }

        [UnityTest]
        public IEnumerator WriteAsync_ValidCaptureResult_CreatesManifestJsonFile()
        {
            var captureResult = CreateSampleCaptureResult();

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            string manifestPath = Path.Combine(
                BundleWriter.GetBundleDirectory(task.Result.id),
                "manifest.json");

            Assert.IsTrue(File.Exists(manifestPath), "manifest.json 파일이 생성되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator WriteAsync_ValidCaptureResult_ManifestStateIsCreated()
        {
            var captureResult = CreateSampleCaptureResult();

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.AreEqual(BundleState.Created, task.Result.state, "초기 상태는 Created여야 합니다.");
        }

        [UnityTest]
        public IEnumerator WriteAsync_ValidCaptureResult_ArtifactsCopiedToBundleDir()
        {
            var captureResult = CreateSampleCaptureResult();

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            string bundleDir = BundleWriter.GetBundleDirectory(task.Result.id);

            // 스크린샷 확인
            Assert.IsTrue(File.Exists(Path.Combine(bundleDir, "screenshot.png")),
                "스크린샷이 번들 디렉토리에 복사되어야 합니다.");

            // 로그 확인
            Assert.IsTrue(File.Exists(Path.Combine(bundleDir, "logs.zip")),
                "로그 파일이 번들 디렉토리에 복사되어야 합니다.");

            // 상태 확인
            Assert.IsTrue(File.Exists(Path.Combine(bundleDir, "state.json")),
                "상태 파일이 번들 디렉토리에 복사되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // SHA-256 해시 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator WriteAsync_ValidCaptureResult_ArtifactsHaveSHA256Hash()
        {
            var captureResult = CreateSampleCaptureResult();

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            BundleManifest manifest = task.Result;

            foreach (var artifact in manifest.artifacts)
            {
                // 영상 디렉토리는 해시 없음
                if (artifact.type == BundleArtifactType.Video)
                    continue;

                Assert.IsNotEmpty(artifact.sha256_hash,
                    $"{artifact.type} 아티팩트에 SHA-256 해시가 있어야 합니다.");
                Assert.AreEqual(64, artifact.sha256_hash.Length,
                    $"{artifact.type} SHA-256 해시는 64자(hex)여야 합니다.");
            }
        }

        [UnityTest]
        public IEnumerator WriteAsync_SameFile_ProducesSameSHA256Hash()
        {
            // 동일한 내용의 파일에 대해 같은 해시 생성 확인
            var captureResult = CreateSampleCaptureResult();

            var task1 = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task1.IsCompleted);

            // 동일한 내용으로 두 번째 캡처 결과 생성
            var captureResult2 = CreateSampleCaptureResult(sameContent: true);

            var task2 = _writer.WriteAsync(captureResult2);
            yield return new WaitUntil(() => task2.IsCompleted);

            // 스크린샷 해시 비교
            var screenshot1 = task1.Result.artifacts.FirstOrDefault(a => a.type == BundleArtifactType.Screenshot);
            var screenshot2 = task2.Result.artifacts.FirstOrDefault(a => a.type == BundleArtifactType.Screenshot);

            Assert.IsNotNull(screenshot1);
            Assert.IsNotNull(screenshot2);
            Assert.AreEqual(screenshot1.sha256_hash, screenshot2.sha256_hash,
                "동일한 파일 내용은 동일한 SHA-256 해시를 가져야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 총 크기 계산 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator WriteAsync_ValidCaptureResult_TotalSizeIsPositive()
        {
            var captureResult = CreateSampleCaptureResult();

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.Greater(task.Result.total_size_bytes, 0L, "총 크기는 0보다 커야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 영상 아티팩트 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator WriteAsync_WithVideoPath_VideoArtifactIncluded()
        {
            // 영상 디렉토리 생성
            string videoDir = Path.Combine(_tempSourceDir, "video");
            Directory.CreateDirectory(videoDir);
            File.WriteAllBytes(Path.Combine(videoDir, "frame_000.raw"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(videoDir, "frame_001.raw"), new byte[1024]);

            var captureResult = CreateSampleCaptureResult(videoPath: videoDir);

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            var videoArtifact = task.Result.artifacts.FirstOrDefault(a => a.type == BundleArtifactType.Video);

            Assert.IsNotNull(videoArtifact, "영상 아티팩트가 포함되어야 합니다.");
            Assert.Greater(videoArtifact.size_bytes, 0L, "영상 크기가 0보다 커야 합니다.");
            Assert.AreEqual(string.Empty, videoArtifact.sha256_hash, "영상 디렉토리는 SHA-256 해시가 없어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 예외 처리 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void WriteAsync_NullCaptureResult_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () => await _writer.WriteAsync(null));
        }

        // ──────────────────────────────────────────────────────────────
        // manifest.json 파싱 검증
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator WriteAsync_ManifestJsonIsValidJson()
        {
            var captureResult = CreateSampleCaptureResult();

            var task = _writer.WriteAsync(captureResult);
            yield return new WaitUntil(() => task.IsCompleted);

            string manifestPath = Path.Combine(
                BundleWriter.GetBundleDirectory(task.Result.id),
                "manifest.json");

            string json = File.ReadAllText(manifestPath);
            var parsed = JsonUtility.FromJson<BundleManifest>(json);

            Assert.IsNotNull(parsed, "manifest.json이 유효한 JSON이어야 합니다.");
            Assert.AreEqual(task.Result.id, parsed.id, "파싱된 ID가 원본과 일치해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 CaptureResult를 생성합니다.
        /// 임시 디렉토리에 더미 아티팩트 파일을 생성합니다.
        /// </summary>
        private CaptureResult CreateSampleCaptureResult(bool sameContent = false, string videoPath = null)
        {
            // 스크린샷 (PNG 더미)
            string screenshotPath = Path.Combine(_tempSourceDir, $"screenshot_{Guid.NewGuid():N}.png");
            byte[] pngContent = sameContent
                ? new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02 }
                : new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02 };
            File.WriteAllBytes(screenshotPath, pngContent);

            // 로그 (ZIP 더미)
            string logsPath = Path.Combine(_tempSourceDir, $"logs_{Guid.NewGuid():N}.zip");
            File.WriteAllBytes(logsPath, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });

            // 상태 (JSON 더미)
            string statePath = Path.Combine(_tempSourceDir, $"state_{Guid.NewGuid():N}.json");
            File.WriteAllText(statePath, "{\"engine\":\"Unity\",\"engine_version\":\"2022.3.22f1\"}");

            return new CaptureResult
            {
                ScreenshotPath = screenshotPath,
                LogsPath       = logsPath,
                StatePath      = statePath,
                VideoPath      = videoPath,
                Timestamp      = DateTime.UtcNow,
            };
        }
    }
}
