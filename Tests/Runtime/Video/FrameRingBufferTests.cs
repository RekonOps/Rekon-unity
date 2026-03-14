using System;
using NUnit.Framework;
using RekonOps.BugBeacon;

namespace RekonOps.BugBeacon.Tests
{
    /// <summary>
    /// FrameRingBuffer 단위 테스트.
    /// </summary>
    [TestFixture]
    public class FrameRingBufferTests
    {
        private FrameRingBuffer _buffer;

        [SetUp]
        public void SetUp()
        {
            _buffer = new FrameRingBuffer(capacity: 5);
        }

        [TearDown]
        public void TearDown()
        {
            _buffer?.Dispose();
        }

        // ──────────────────────────────────────────────────────────────
        // 생성자 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Constructor_InvalidCapacity_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRingBuffer(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRingBuffer(-1));
        }

        [Test]
        public void Constructor_ValidCapacity_CountIsZero()
        {
            using var buf = new FrameRingBuffer(10);
            Assert.AreEqual(0, buf.Count);
            Assert.AreEqual(10, buf.Capacity);
        }

        // ──────────────────────────────────────────────────────────────
        // 기본 추가/조회 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Add_ValidFrame_CountIncreases()
        {
            _buffer.Add(MakeFrame(1.0, 100));
            Assert.AreEqual(1, _buffer.Count);
        }

        [Test]
        public void Add_InvalidFrame_CountDoesNotIncrease()
        {
            // 데이터 없는 프레임
            var invalid = new FrameData(null, 0, 0, 1.0);
            _buffer.Add(invalid);
            Assert.AreEqual(0, _buffer.Count, "유효하지 않은 프레임은 추가되지 않아야 합니다.");
        }

        [Test]
        public void GetFrames_Empty_ReturnsEmptyArray()
        {
            var frames = _buffer.GetFrames();
            Assert.IsNotNull(frames);
            Assert.AreEqual(0, frames.Length);
        }

        [Test]
        public void GetFrames_ReturnsTimeSortedFrames()
        {
            // 역순으로 추가
            _buffer.Add(MakeFrame(3.0, 100));
            _buffer.Add(MakeFrame(1.0, 100));
            _buffer.Add(MakeFrame(2.0, 100));

            var frames = _buffer.GetFrames();

            Assert.AreEqual(3, frames.Length);
            Assert.AreEqual(1.0, frames[0].Timestamp, 0.001);
            Assert.AreEqual(2.0, frames[1].Timestamp, 0.001);
            Assert.AreEqual(3.0, frames[2].Timestamp, 0.001);
        }

        // ──────────────────────────────────────────────────────────────
        // 링버퍼 오버플로우 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Add_OverCapacity_CountStaysAtCapacity()
        {
            for (int i = 0; i < 8; i++)
                _buffer.Add(MakeFrame(i, 100));

            Assert.AreEqual(5, _buffer.Count);
        }

        [Test]
        public void Add_OverCapacity_OldestEvicted()
        {
            // capacity = 5, 7개 추가 → 처음 2개(t=0, t=1)가 삭제됨
            for (int i = 0; i < 7; i++)
                _buffer.Add(MakeFrame(i, 100));

            var frames = _buffer.GetFrames();

            Assert.AreEqual(5, frames.Length);
            Assert.AreEqual(2.0, frames[0].Timestamp, 0.001, "처음 2개 프레임이 삭제되어야 합니다.");
            Assert.AreEqual(6.0, frames[4].Timestamp, 0.001);
        }

        [Test]
        public void GetFrames_ExactlyAtCapacity_ReturnsAll()
        {
            for (int i = 0; i < 5; i++)
                _buffer.Add(MakeFrame(i, 100));

            var frames = _buffer.GetFrames();
            Assert.AreEqual(5, frames.Length);
        }

        // ──────────────────────────────────────────────────────────────
        // Clear 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Clear_ResetsBuffer()
        {
            _buffer.Add(MakeFrame(1.0, 100));
            _buffer.Add(MakeFrame(2.0, 100));
            _buffer.Clear();

            Assert.AreEqual(0, _buffer.Count);
            Assert.AreEqual(0, _buffer.GetFrames().Length);
        }

        // ──────────────────────────────────────────────────────────────
        // Dispose 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var buf = new FrameRingBuffer(5);
            buf.Dispose();
            Assert.DoesNotThrow(() => buf.Dispose());
        }

        [Test]
        public void Add_AfterDispose_DoesNotThrow()
        {
            var buf = new FrameRingBuffer(5);
            buf.Dispose();
            // Dispose 후 Add 호출 → 예외 없이 무시해야 함
            Assert.DoesNotThrow(() => buf.Add(MakeFrame(1.0, 100)));
        }

        // ──────────────────────────────────────────────────────────────
        // FrameData 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void FrameData_ValidData_IsValidTrue()
        {
            var frame = MakeFrame(1.0, 100);
            Assert.IsTrue(frame.IsValid);
        }

        [Test]
        public void FrameData_NullData_IsValidFalse()
        {
            var frame = new FrameData(null, 100, 100, 1.0);
            Assert.IsFalse(frame.IsValid);
        }

        [Test]
        public void FrameData_ZeroDimensions_IsValidFalse()
        {
            var frame = new FrameData(new byte[100], 0, 100, 1.0);
            Assert.IsFalse(frame.IsValid);
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        private static FrameData MakeFrame(double timestamp, int sizeBytes)
        {
            return new FrameData(new byte[sizeBytes], 32, 18, timestamp);
        }
    }
}
