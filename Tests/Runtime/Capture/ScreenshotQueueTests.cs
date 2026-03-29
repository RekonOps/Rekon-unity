using System;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ScreenshotQueue 단위 테스트.
    /// </summary>
    [TestFixture]
    public class ScreenshotQueueTests
    {
        private ScreenshotQueue _queue;

        // 테스트용 간단한 PNG 바이트 시퀀스
        private static readonly byte[] SampleBytes1 = new byte[] { 0x89, 0x50 };
        private static readonly byte[] SampleBytes2 = new byte[] { 0x89, 0x51 };
        private static readonly byte[] SampleBytes3 = new byte[] { 0x89, 0x52 };
        private static readonly byte[] SampleBytes4 = new byte[] { 0x89, 0x53 };
        private static readonly byte[] SampleBytes5 = new byte[] { 0x89, 0x54 };
        private static readonly byte[] SampleBytes6 = new byte[] { 0x89, 0x55 };

        [SetUp]
        public void SetUp()
        {
            _queue = new ScreenshotQueue();
        }

        // ──────────────────────────────────────────────────────────────
        // 기본 동작
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Count_초기값은_0()
        {
            Assert.AreEqual(0, _queue.Count);
        }

        [Test]
        public void Enqueue_1개_추가_후_Count는_1()
        {
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);

            Assert.AreEqual(1, _queue.Count);
        }

        [Test]
        public void Enqueue_null_바이트_무시()
        {
            _queue.Enqueue(null, DateTime.UtcNow);

            Assert.AreEqual(0, _queue.Count);
        }

        [Test]
        public void Enqueue_빈_바이트_무시()
        {
            _queue.Enqueue(new byte[0], DateTime.UtcNow);

            Assert.AreEqual(0, _queue.Count);
        }

        // ──────────────────────────────────────────────────────────────
        // FIFO eviction
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Enqueue_5장_초과_시_가장_오래된_항목_삭제()
        {
            // 1~5번째 추가 (MaxCapacity 채움)
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes3, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes4, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes5, DateTime.UtcNow);

            // 6번째 추가 → 1번째(SampleBytes1)가 삭제되어야 함
            _queue.Enqueue(SampleBytes6, DateTime.UtcNow);

            var entries = _queue.PeekAll();
            Assert.AreEqual(ScreenshotQueue.MaxCapacity, entries.Length);
            Assert.AreNotSame(SampleBytes1, entries[0].PngBytes, "가장 오래된 항목이 삭제되어야 합니다.");
        }

        [Test]
        public void Enqueue_6개_추가_후_Count는_5()
        {
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes3, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes4, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes5, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes6, DateTime.UtcNow);

            Assert.AreEqual(ScreenshotQueue.MaxCapacity, _queue.Count);
        }

        [Test]
        public void Enqueue_FIFO_순서_검증()
        {
            // 6개 추가 → 2~6번째(SampleBytes2~SampleBytes6)만 남아야 함
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes3, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes4, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes5, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes6, DateTime.UtcNow);

            var entries = _queue.PeekAll();

            Assert.AreEqual(5, entries.Length);
            Assert.AreSame(SampleBytes2, entries[0].PngBytes, "index 0: 2번째로 추가된 항목이어야 합니다.");
            Assert.AreSame(SampleBytes3, entries[1].PngBytes, "index 1: 3번째로 추가된 항목이어야 합니다.");
            Assert.AreSame(SampleBytes4, entries[2].PngBytes, "index 2: 4번째로 추가된 항목이어야 합니다.");
            Assert.AreSame(SampleBytes5, entries[3].PngBytes, "index 3: 5번째로 추가된 항목이어야 합니다.");
            Assert.AreSame(SampleBytes6, entries[4].PngBytes, "index 4: 6번째로 추가된 항목이어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // DrainAll
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void DrainAll_전체_반환_후_큐_비어있음()
        {
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow);

            var drained = _queue.DrainAll();

            Assert.AreEqual(2, drained.Length, "DrainAll은 2개를 반환해야 합니다.");
            Assert.AreEqual(0, _queue.Count, "DrainAll 후 큐는 비어 있어야 합니다.");
        }

        [Test]
        public void DrainAll_빈_큐에서_빈_배열_반환()
        {
            var drained = _queue.DrainAll();

            Assert.IsNotNull(drained);
            Assert.AreEqual(0, drained.Length);
        }

        [Test]
        public void DrainAll_반환된_항목_순서_검증()
        {
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes3, DateTime.UtcNow);

            var drained = _queue.DrainAll();

            Assert.AreEqual(3, drained.Length);
            Assert.AreSame(SampleBytes1, drained[0].PngBytes, "첫 번째로 추가된 항목이 먼저 반환되어야 합니다.");
            Assert.AreSame(SampleBytes2, drained[1].PngBytes);
            Assert.AreSame(SampleBytes3, drained[2].PngBytes);
        }

        // ──────────────────────────────────────────────────────────────
        // Clear
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Clear_큐_초기화()
        {
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow);
            _queue.Clear();

            Assert.AreEqual(0, _queue.Count);
            Assert.AreEqual(0, _queue.PeekAll().Length);
        }

        // ──────────────────────────────────────────────────────────────
        // PeekAll
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void PeekAll_큐_내용_변경_없음()
        {
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow);

            _queue.PeekAll(); // 호출 후에도 큐 유지
            var after = _queue.PeekAll();

            Assert.AreEqual(2, after.Length, "PeekAll은 큐를 변경하지 않아야 합니다.");
            Assert.AreEqual(2, _queue.Count);
        }

        [Test]
        public void PeekAll_빈_큐에서_빈_배열_반환()
        {
            var result = _queue.PeekAll();

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        // ──────────────────────────────────────────────────────────────
        // MaxCapacity 상수
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MaxCapacity는_5()
        {
            Assert.AreEqual(5, ScreenshotQueue.MaxCapacity);
        }

        // ──────────────────────────────────────────────────────────────
        // 스레드 안전성 (멀티스레드 스트레스)
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void 멀티스레드_동시_Enqueue_예외없음()
        {
            const int threadCount = 10;
            const int enqueuePer  = 20;

            var tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int seed = t; // 클로저 캡처 방지
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < enqueuePer; i++)
                    {
                        byte[] data = new byte[] { (byte)(seed & 0xFF), (byte)(i & 0xFF) };
                        _queue.Enqueue(data, DateTime.UtcNow);
                    }
                });
            }

            Assert.DoesNotThrow(() => Task.WaitAll(tasks), "멀티스레드 동시 Enqueue에서 예외가 발생하지 않아야 합니다.");
            Assert.LessOrEqual(_queue.Count, ScreenshotQueue.MaxCapacity, $"Count는 MaxCapacity({ScreenshotQueue.MaxCapacity})를 초과할 수 없습니다.");
        }
    }
}
