using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.BugBeacon;

namespace RekonOps.BugBeacon.Tests
{
    /// <summary>
    /// VideoEncoder 단위 테스트.
    /// </summary>
    [TestFixture]
    public class VideoEncoderTests
    {
        private VideoEncoder _encoder;
        private VideoEncoderConfig _config;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _encoder = new VideoEncoder();
            _config = new VideoEncoderConfig
            {
                Width = 320,
                Height = 180,
                Fps = 30,
                BitrateMbps = 5f,
            };
            _tempDir = Path.Combine(Path.GetTempPath(), "VideoEncoderTests_" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ──────────────────────────────────────────────────────────────
        // 기본 검증 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void EncodeAsync_NullOutputPath_ThrowsArgumentNullException()
        {
            var frames = new[] { MakeFrame(1.0) };
            Assert.ThrowsAsync<System.ArgumentNullException>(
                async () => await _encoder.EncodeAsync(frames, null, _config));
        }

        [Test]
        public void EncodeAsync_NullConfig_ThrowsArgumentNullException()
        {
            var frames = new[] { MakeFrame(1.0) };
            Assert.ThrowsAsync<System.ArgumentNullException>(
                async () => await _encoder.EncodeAsync(frames, _tempDir, null));
        }

        [UnityTest]
        public IEnumerator EncodeAsync_NullFrames_CompletesWithoutError()
        {
            var task = _encoder.EncodeAsync(null, _tempDir, _config);
            yield return new WaitUntil(() => task.IsCompleted);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*인코딩할 프레임이 없습니다.*"));
            Assert.IsFalse(task.IsFaulted, "null 프레임은 예외 없이 처리되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator EncodeAsync_EmptyFrames_CompletesWithoutError()
        {
            var task = _encoder.EncodeAsync(System.Array.Empty<FrameData>(), _tempDir, _config);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
        }

        // ──────────────────────────────────────────────────────────────
        // 인코딩 결과 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator EncodeAsync_ValidFrames_CreatesOutputDirectory()
        {
            var frames = new[]
            {
                MakeFrame(0.0),
                MakeFrame(0.033),
                MakeFrame(0.066),
            };

            var task = _encoder.EncodeAsync(frames, _tempDir, _config);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted, $"인코딩 실패: {task.Exception?.Message}");
            Assert.IsTrue(Directory.Exists(_tempDir), "출력 디렉토리가 생성되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator EncodeAsync_ValidFrames_CreatesMetadataJson()
        {
            var frames = new[]
            {
                MakeFrame(0.0),
                MakeFrame(0.033),
            };

            var task = _encoder.EncodeAsync(frames, _tempDir, _config);
            yield return new WaitUntil(() => task.IsCompleted);

            string metadataPath = Path.Combine(_tempDir, "metadata.json");
            Assert.IsTrue(File.Exists(metadataPath), "metadata.json이 생성되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator EncodeAsync_ValidFrames_MetadataContainsFrameCount()
        {
            var frames = new[]
            {
                MakeFrame(0.0),
                MakeFrame(0.033),
                MakeFrame(0.066),
            };

            var task = _encoder.EncodeAsync(frames, _tempDir, _config);
            yield return new WaitUntil(() => task.IsCompleted);

            string metadataPath = Path.Combine(_tempDir, "metadata.json");
            string content = File.ReadAllText(metadataPath);

            StringAssert.Contains("\"frame_count\": 3", content);
            StringAssert.Contains("\"width\": 320", content);
            StringAssert.Contains("\"height\": 180", content);
        }

        [UnityTest]
        public IEnumerator EncodeAsync_ValidFrames_CreatesRawFiles()
        {
            int frameCount = 3;
            var frames = new FrameData[frameCount];
            for (int i = 0; i < frameCount; i++)
                frames[i] = MakeFrame(i * 0.033);

            var task = _encoder.EncodeAsync(frames, _tempDir, _config);
            yield return new WaitUntil(() => task.IsCompleted);

            // 각 프레임 .raw 파일 확인
            for (int i = 0; i < frameCount; i++)
            {
                string rawPath = Path.Combine(_tempDir, $"frame_{i:D6}.raw");
                Assert.IsTrue(File.Exists(rawPath), $"frame_{i:D6}.raw가 생성되어야 합니다.");
            }
        }

        [UnityTest]
        public IEnumerator EncodeAsync_CreatesDirectoryIfNotExists()
        {
            string nestedDir = Path.Combine(_tempDir, "nested", "output");
            var frames = new[] { MakeFrame(0.0) };

            var task = _encoder.EncodeAsync(frames, nestedDir, _config);
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(task.IsFaulted);
            Assert.IsTrue(Directory.Exists(nestedDir));
        }

        // ──────────────────────────────────────────────────────────────
        // VideoEncoderConfig 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void VideoEncoderConfig_FromSettings_ReadsCorrectValues()
        {
            var settings = ScriptableObject.CreateInstance<BugBeaconSettings>();
            settings.videoWidth = 1280;
            settings.videoHeight = 720;
            settings.videoFps = 30;
            settings.videoBitrateMbps = 10f;

            var config = VideoEncoderConfig.FromSettings(settings);

            Assert.AreEqual(1280, config.Width);
            Assert.AreEqual(720, config.Height);
            Assert.AreEqual(30, config.Fps);
            Assert.AreEqual(10f, config.BitrateMbps, 0.001f);

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void VideoEncoderConfig_FromSettings_NullSettings_ThrowsException()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => VideoEncoderConfig.FromSettings(null));
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        private static FrameData MakeFrame(double timestamp)
        {
            int width = 320;
            int height = 180;
            // RGBA32: 4바이트/픽셀
            byte[] data = new byte[width * height * 4];
            // 더미 데이터 채우기
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 256);

            return new FrameData(data, width, height, timestamp);
        }
    }
}
