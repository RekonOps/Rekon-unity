using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ScreenshotCapturer 단위 테스트.
    /// </summary>
    [TestFixture]
    public class ScreenshotCapturerTests
    {
        private RekonSettings _settings;
        private ScreenshotCapturer _capturer;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<RekonSettings>();
            _settings.screenshotDownscale = 1;
            _capturer = new ScreenshotCapturer(_settings);

            _tempDir = Path.Combine(Path.GetTempPath(), "RekonTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_settings);

            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ──────────────────────────────────────────────────────────────
        // 생성자 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Constructor_NullSettings_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ScreenshotCapturer(null));
        }

        // ──────────────────────────────────────────────────────────────
        // SaveAsync 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SaveAsync_ValidPngBytes_FileCreated()
        {
            // Arrange: 유효한 PNG 더미 데이터 (실제 PNG 헤더 포함)
            byte[] pngBytes = CreateMinimalPngBytes();
            string filePath = Path.Combine(_tempDir, "screenshot.png");

            // Act
            var task = _capturer.SaveAsync(pngBytes, filePath);
            yield return new WaitUntil(() => task.IsCompleted);

            // Assert
            Assert.IsTrue(File.Exists(filePath), "파일이 생성되어야 합니다.");
            Assert.AreEqual(pngBytes.Length, new FileInfo(filePath).Length, "파일 크기가 일치해야 합니다.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_CreatesDirectoryIfNotExists()
        {
            // Arrange
            byte[] pngBytes = CreateMinimalPngBytes();
            string nestedDir = Path.Combine(_tempDir, "nested", "dir");
            string filePath = Path.Combine(nestedDir, "screenshot.png");

            // Act
            var task = _capturer.SaveAsync(pngBytes, filePath);
            yield return new WaitUntil(() => task.IsCompleted);

            // Assert
            Assert.IsTrue(File.Exists(filePath), "중첩된 디렉토리에 파일이 생성되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_NullBytes_DoesNotThrow()
        {
            // Arrange
            string filePath = Path.Combine(_tempDir, "screenshot.png");

            // Act: null 데이터 → 경고 로그만 출력, 예외 없음
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*PNG 데이터가 없습니다.*"));
            var task = _capturer.SaveAsync(null, filePath);
            yield return new WaitUntil(() => task.IsCompleted);

            // Assert
            Assert.IsFalse(File.Exists(filePath), "null 데이터는 파일을 생성하지 않아야 합니다.");
        }

        [Test]
        public void SaveAsync_NullFilePath_ThrowsArgumentNullException()
        {
            byte[] pngBytes = CreateMinimalPngBytes();
            Assert.ThrowsAsync<System.ArgumentNullException>(
                async () => await _capturer.SaveAsync(pngBytes, null));
        }

        // ──────────────────────────────────────────────────────────────
        // CaptureAsync 테스트 (Play Mode 전용)
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator CaptureAsync_InPlayMode_ReturnsPngBytes()
        {
            // Act: Play Mode에서 실제 화면 캡처
            var task = _capturer.CaptureAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            // Assert
            // Play Mode에서 화면이 존재하면 PNG 바이트가 반환됨
            // 헤드리스 환경에서는 null일 수 있어 엄격하게 검사하지 않음
            if (task.Result != null)
            {
                Assert.Greater(task.Result.Length, 0, "PNG 바이트 배열은 비어 있지 않아야 합니다.");
                // PNG 매직 넘버 확인 (첫 8바이트)
                Assert.AreEqual(0x89, task.Result[0], "PNG 매직 바이트[0] 불일치");
                Assert.AreEqual(0x50, task.Result[1], "PNG 매직 바이트[1] 불일치 ('P')");
                Assert.AreEqual(0x4E, task.Result[2], "PNG 매직 바이트[2] 불일치 ('N')");
                Assert.AreEqual(0x47, task.Result[3], "PNG 매직 바이트[3] 불일치 ('G')");
            }
        }

        [UnityTest]
        public IEnumerator CaptureAsync_WithDownscale_ReturnsValidResult()
        {
            // Arrange: 다운스케일 2배
            _settings.screenshotDownscale = 2;
            var capturer = new ScreenshotCapturer(_settings);

            // Act
            var task = capturer.CaptureAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            // Assert: 예외 없이 완료되어야 함
            Assert.IsTrue(task.IsCompleted);
            Assert.IsFalse(task.IsFaulted, $"캡처가 실패하지 않아야 합니다: {task.Exception?.Message}");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 최소한의 유효한 PNG 파일 바이트를 생성합니다.
        /// (1x1 픽셀 빨간색 PNG)
        /// </summary>
        private static byte[] CreateMinimalPngBytes()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.red);
            tex.Apply();
            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return bytes;
        }
    }
}
