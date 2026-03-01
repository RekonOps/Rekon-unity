using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.BugOneTouch;

namespace RekonOps.BugOneTouch.Tests
{
    /// <summary>
    /// LogRingBuffer 단위 테스트.
    /// </summary>
    [TestFixture]
    public class LogRingBufferTests
    {
        private LogRingBuffer _buffer;

        [SetUp]
        public void SetUp()
        {
            _buffer = new LogRingBuffer(capacity: 5);
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
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new LogRingBuffer(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new LogRingBuffer(-1));
        }

        [Test]
        public void Constructor_ValidCapacity_InitialCountIsZero()
        {
            using var buf = new LogRingBuffer(10);
            Assert.AreEqual(0, buf.Count);
        }

        // ──────────────────────────────────────────────────────────────
        // 기본 추가/조회 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Add_SingleEntry_CountIsOne()
        {
            _buffer.Add(MakeEntry(1.0, LogType.Log, "테스트 메시지"));
            Assert.AreEqual(1, _buffer.Count);
        }

        [Test]
        public void Add_MultipleEntries_CountIncreases()
        {
            _buffer.Add(MakeEntry(1.0, LogType.Log, "A"));
            _buffer.Add(MakeEntry(2.0, LogType.Log, "B"));
            _buffer.Add(MakeEntry(3.0, LogType.Log, "C"));
            Assert.AreEqual(3, _buffer.Count);
        }

        [Test]
        public void GetEntries_Empty_ReturnsEmptyArray()
        {
            var entries = _buffer.GetEntries();
            Assert.IsNotNull(entries);
            Assert.AreEqual(0, entries.Length);
        }

        [Test]
        public void GetEntries_BelowCapacity_ReturnsSortedByTimestamp()
        {
            // 역순으로 추가
            _buffer.Add(MakeEntry(3.0, LogType.Log, "C"));
            _buffer.Add(MakeEntry(1.0, LogType.Log, "A"));
            _buffer.Add(MakeEntry(2.0, LogType.Log, "B"));

            var entries = _buffer.GetEntries();

            Assert.AreEqual(3, entries.Length);
            Assert.AreEqual("A", entries[0].Message, "첫 번째는 가장 이른 항목이어야 합니다.");
            Assert.AreEqual("B", entries[1].Message);
            Assert.AreEqual("C", entries[2].Message);
        }

        // ──────────────────────────────────────────────────────────────
        // 링버퍼 오버플로우 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Add_OverCapacity_CountStaysAtCapacity()
        {
            // capacity = 5, 7개 추가
            for (int i = 0; i < 7; i++)
                _buffer.Add(MakeEntry(i, LogType.Log, $"msg{i}"));

            Assert.AreEqual(5, _buffer.Count);
        }

        [Test]
        public void Add_OverCapacity_OldestEntryEvicted()
        {
            // capacity = 5, 7개 추가 → 최초 2개(msg0, msg1)가 밀려남
            for (int i = 0; i < 7; i++)
                _buffer.Add(MakeEntry(i, LogType.Log, $"msg{i}"));

            var entries = _buffer.GetEntries();

            Assert.AreEqual(5, entries.Length);
            // 타임스탬프 기준: msg2(2.0)~msg6(6.0) 유지
            Assert.AreEqual("msg2", entries[0].Message, "msg0, msg1이 삭제되어야 합니다.");
            Assert.AreEqual("msg6", entries[4].Message);
        }

        [Test]
        public void GetEntries_ExactlyAtCapacity_ReturnsAllInOrder()
        {
            // capacity = 5, 5개 정확히 추가
            for (int i = 0; i < 5; i++)
                _buffer.Add(MakeEntry(i, LogType.Log, $"msg{i}"));

            var entries = _buffer.GetEntries();

            Assert.AreEqual(5, entries.Length);
            for (int i = 0; i < 5; i++)
                Assert.AreEqual($"msg{i}", entries[i].Message);
        }

        // ──────────────────────────────────────────────────────────────
        // Clear 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Clear_AfterAddingEntries_CountIsZero()
        {
            _buffer.Add(MakeEntry(1.0, LogType.Log, "A"));
            _buffer.Add(MakeEntry(2.0, LogType.Log, "B"));
            _buffer.Clear();

            Assert.AreEqual(0, _buffer.Count);
            Assert.AreEqual(0, _buffer.GetEntries().Length);
        }

        // ──────────────────────────────────────────────────────────────
        // Dispose 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var buf = new LogRingBuffer(5);
            buf.Dispose();
            Assert.DoesNotThrow(() => buf.Dispose());
        }

        // ──────────────────────────────────────────────────────────────
        // Unity 로그 콜백 통합 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator LogCallback_UnityDebugLog_CapturedInBuffer()
        {
            using var buf = new LogRingBuffer(10);
            buf.Clear(); // 기존 로그 초기화

            // Play Mode에서 Debug.Log 호출 → 콜백을 통해 버퍼에 추가됨
            string testMsg = $"BugOneTouchTest_{System.Guid.NewGuid():N}";
            Debug.Log(testMsg);

            // 콜백이 동기적으로 처리됨 (Unity는 같은 프레임에 콜백 호출)
            yield return null;

            var entries = buf.GetEntries();
            bool found = false;
            foreach (var e in entries)
            {
                if (e.Message == testMsg)
                {
                    found = true;
                    Assert.AreEqual(LogType.Log, e.LogType);
                    break;
                }
            }
            Assert.IsTrue(found, $"'{testMsg}' 메시지가 버퍼에서 발견되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        private static LogEntry MakeEntry(double timestamp, LogType logType, string message, string stackTrace = "")
        {
            return new LogEntry(timestamp, logType, message, stackTrace);
        }
    }
}
