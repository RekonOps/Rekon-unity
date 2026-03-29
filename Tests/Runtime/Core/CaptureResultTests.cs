using System;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// CaptureResult.IsPartialSuccess / IsFullSuccess 단위 테스트.
    /// </summary>
    [TestFixture]
    public class CaptureResultTests
    {
        // ──────────────────────────────────────────────────────────────
        // IsPartialSuccess 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void IsPartialSuccess_ScreenshotEntries만_있을때_true()
        {
            // Arrange: ScreenshotEntries(메모리 큐 방식)만 존재
            var result = new CaptureResult
            {
                ScreenshotEntries = new[]
                {
                    new ScreenshotEntry(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, DateTime.UtcNow),
                },
            };

            // Assert
            Assert.IsTrue(result.IsPartialSuccess,
                "ScreenshotEntries에 항목이 있으면 IsPartialSuccess는 true여야 합니다.");
        }

        [Test]
        public void IsPartialSuccess_모두_비어있을때_false()
        {
            // Arrange: 아무 아티팩트도 없는 빈 결과
            var result = new CaptureResult();

            // Assert
            Assert.IsFalse(result.IsPartialSuccess,
                "아무 아티팩트도 없으면 IsPartialSuccess는 false여야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // IsFullSuccess 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void IsFullSuccess_ScreenshotEntries_포함_시_true()
        {
            // Arrange: ScreenshotEntries + LogsPath + StatePath 모두 존재
            var result = new CaptureResult
            {
                ScreenshotEntries = new[]
                {
                    new ScreenshotEntry(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, DateTime.UtcNow),
                },
                LogsPath = "/tmp/logs.zip",
                StatePath = "/tmp/state.json",
            };

            // Assert
            Assert.IsTrue(result.IsFullSuccess,
                "ScreenshotEntries + LogsPath + StatePath가 모두 있으면 IsFullSuccess는 true여야 합니다.");
        }

        [Test]
        public void IsFullSuccess_ScreenshotPath만_있을때_true()
        {
            // Arrange: 레거시 ScreenshotPath + LogsPath + StatePath
#pragma warning disable CS0618 // [Obsolete] ScreenshotPath 하위 호환 테스트
            var result = new CaptureResult
            {
                ScreenshotPath = "/tmp/screenshot.png",
                LogsPath = "/tmp/logs.zip",
                StatePath = "/tmp/state.json",
            };
#pragma warning restore CS0618

            // Assert: 레거시 필드도 스크린샷 조건을 충족해야 함
            Assert.IsTrue(result.IsFullSuccess,
                "레거시 ScreenshotPath + LogsPath + StatePath가 있으면 IsFullSuccess는 true여야 합니다.");
        }

        [Test]
        public void IsFullSuccess_모두_비어있을때_false()
        {
            // Arrange: 아무 아티팩트도 없는 빈 결과
            var result = new CaptureResult();

            // Assert
            Assert.IsFalse(result.IsFullSuccess,
                "아무 아티팩트도 없으면 IsFullSuccess는 false여야 합니다.");
        }
    }
}
