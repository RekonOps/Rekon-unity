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
            // 1~5번째 추가 (Capacity 채움)
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes3, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes4, DateTime.UtcNow);
            _queue.Enqueue(SampleBytes5, DateTime.UtcNow);

            // 6번째 추가 → 1번째(SampleBytes1)가 삭제되어야 함
            _queue.Enqueue(SampleBytes6, DateTime.UtcNow);

            var entries = _queue.PeekAll();
            Assert.AreEqual(_queue.Capacity, entries.Length);
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

            Assert.AreEqual(_queue.Capacity, _queue.Count);
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
        // Capacity — 플랜별 주입 (free 3 / team 5 / team_pro 10)
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Capacity_기본값은_5()
        {
            // 인자 없는 생성자 = 기존 동작(team 기준 5) 유지
            Assert.AreEqual(5, new ScreenshotQueue().Capacity);
        }

        [Test]
        public void Capacity_생성자_주입값_반영()
        {
            Assert.AreEqual(3, new ScreenshotQueue(3).Capacity);
            Assert.AreEqual(10, new ScreenshotQueue(10).Capacity);
        }

        [Test]
        public void Capacity_1미만_주입_시_5로_가드()
        {
            Assert.AreEqual(5, new ScreenshotQueue(0).Capacity);
            Assert.AreEqual(5, new ScreenshotQueue(-1).Capacity);
        }

        [Test]
        public void Capacity_10_team_pro_11번째에서_가장_오래된_항목_eviction()
        {
            var queue = new ScreenshotQueue(10);
            for (int i = 0; i < 10; i++)
                queue.Enqueue(new byte[] { 0x89, (byte)i }, DateTime.UtcNow);

            Assert.AreEqual(10, queue.Count, "10장까지는 eviction 없이 채워져야 합니다.");

            byte[] first = new byte[] { 0x89, 0x00 };
            // 11번째 → eviction 발생, Count 는 10 유지
            bool evicted = queue.Enqueue(new byte[] { 0x89, 0xAA }, DateTime.UtcNow);

            Assert.IsTrue(evicted, "11번째 추가 시 eviction 이 발생해야 합니다.");
            Assert.AreEqual(10, queue.Count);
            Assert.AreNotSame(first, queue.PeekAll()[0].PngBytes, "가장 오래된 항목이 삭제되어야 합니다.");
        }

        [Test]
        public void Capacity_3_free_4번째에서_eviction()
        {
            var queue = new ScreenshotQueue(3);
            queue.Enqueue(SampleBytes1, DateTime.UtcNow);
            queue.Enqueue(SampleBytes2, DateTime.UtcNow);
            queue.Enqueue(SampleBytes3, DateTime.UtcNow);

            Assert.AreEqual(3, queue.Count);

            bool evicted = queue.Enqueue(SampleBytes4, DateTime.UtcNow);

            Assert.IsTrue(evicted, "4번째 추가 시 eviction 이 발생해야 합니다.");
            Assert.AreEqual(3, queue.Count);
            Assert.AreNotSame(SampleBytes1, queue.PeekAll()[0].PngBytes, "가장 오래된 항목(SampleBytes1)이 삭제되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // CaptureRealtime — 단조 증가 순서 검증 (team_pro 싱크 핵심)
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Enqueue_CaptureRealtime_DrainAll_순서_검증()
        {
            // 서로 다른 CaptureRealtime 값으로 3개 Enqueue
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow, 1.0);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow, 2.0);
            _queue.Enqueue(SampleBytes3, DateTime.UtcNow, 3.0);

            var drained = _queue.DrainAll();

            // DrainAll 자체는 Enqueue 순서(FIFO) 반환 — 정렬은 Orchestrator 책임
            Assert.AreEqual(3, drained.Length);
            Assert.AreEqual(1.0, drained[0].CaptureRealtime, "첫 번째로 Enqueue된 항목의 CaptureRealtime = 1.0 이어야 합니다.");
            Assert.AreEqual(2.0, drained[1].CaptureRealtime);
            Assert.AreEqual(3.0, drained[2].CaptureRealtime);
        }

        [Test]
        public void Enqueue_CaptureRealtime_역순_DrainAll_후_FIFO_확인()
        {
            // 역순 CaptureRealtime 으로 Enqueue (비동기 경합 시뮬레이션)
            _queue.Enqueue(SampleBytes3, DateTime.UtcNow, 3.0);
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow, 1.0);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow, 2.0);

            var drained = _queue.DrainAll();

            // DrainAll 은 FIFO — 큐 삽입 순서 그대로 반환
            Assert.AreEqual(3.0, drained[0].CaptureRealtime, "DrainAll 은 Enqueue 순서(FIFO)를 그대로 반환해야 합니다.");
            Assert.AreEqual(1.0, drained[1].CaptureRealtime);
            Assert.AreEqual(2.0, drained[2].CaptureRealtime);

            // 정렬 후 단조 증가 검증 (Orchestrator 가 수행할 Array.Sort 동작 확인)
            Array.Sort(drained, (a, b) => a.CaptureRealtime.CompareTo(b.CaptureRealtime));
            Assert.AreEqual(1.0, drained[0].CaptureRealtime, "정렬 후 index 0 = 가장 작은 CaptureRealtime 이어야 합니다.");
            Assert.AreEqual(2.0, drained[1].CaptureRealtime);
            Assert.AreEqual(3.0, drained[2].CaptureRealtime);
        }

        [Test]
        public void Enqueue_CaptureRealtime_정렬_후_screenshot_N_단조증가_검증()
        {
            // 비동기 경합 시나리오: 두 번째 캡처가 먼저 완료되어 Enqueue 됨
            // (CaptureRealtime 은 await 이전에 기록하므로 올바른 시각을 보유)
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow, 2.0); // 첫 번째 캡처이지만 나중에 완료
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow, 1.0); // 두 번째 캡처이지만 먼저 완료

            var drained = _queue.DrainAll();

            // 정렬 전: FIFO 순서 (Enqueue 순서)
            Assert.AreEqual(2.0, drained[0].CaptureRealtime, "DrainAll 원본 순서: Enqueue 순서 그대로.");

            // 정렬 후: CaptureRealtime 오름차순
            Array.Sort(drained, (a, b) => a.CaptureRealtime.CompareTo(b.CaptureRealtime));
            Assert.AreEqual(1.0, drained[0].CaptureRealtime,
                "정렬 후 screenshot_0 = 가장 먼저 캡처된 항목(CaptureRealtime 최소값)이어야 합니다.");
            Assert.AreEqual(2.0, drained[1].CaptureRealtime,
                "정렬 후 screenshot_1 = 두 번째로 캡처된 항목이어야 합니다.");

            // 단조 증가 검증
            for (int i = 1; i < drained.Length; i++)
            {
                Assert.GreaterOrEqual(drained[i].CaptureRealtime, drained[i - 1].CaptureRealtime,
                    $"정렬 후 drained[{i}].CaptureRealtime >= drained[{i - 1}].CaptureRealtime 이어야 합니다 " +
                    $"(screenshot_{i}.png 의 captured_t_abs 가 screenshot_{i - 1}.png 보다 크거나 같아야 함).");
            }
        }

        [Test]
        public void Enqueue_5장_CaptureRealtime_정렬_후_screenshot_N_단조증가_검증()
        {
            // team_pro 5장 만료 시나리오 — 비동기 경합으로 역순 Enqueue
            _queue.Enqueue(SampleBytes5, DateTime.UtcNow, 5.0);
            _queue.Enqueue(SampleBytes4, DateTime.UtcNow, 4.0);
            _queue.Enqueue(SampleBytes3, DateTime.UtcNow, 3.0);
            _queue.Enqueue(SampleBytes2, DateTime.UtcNow, 2.0);
            _queue.Enqueue(SampleBytes1, DateTime.UtcNow, 1.0);

            var drained = _queue.DrainAll();
            Array.Sort(drained, (a, b) => a.CaptureRealtime.CompareTo(b.CaptureRealtime));

            // 단조 증가 검증
            for (int i = 1; i < drained.Length; i++)
            {
                Assert.GreaterOrEqual(drained[i].CaptureRealtime, drained[i - 1].CaptureRealtime,
                    $"drained[{i}].CaptureRealtime >= drained[{i - 1}].CaptureRealtime 이어야 합니다.");
            }

            // screenshot_0 이 가장 작은 CaptureRealtime
            Assert.AreEqual(1.0, drained[0].CaptureRealtime,
                "정렬 후 screenshot_0 = CaptureRealtime 최소값(1.0) 이어야 합니다.");
            Assert.AreEqual(5.0, drained[4].CaptureRealtime,
                "정렬 후 screenshot_4 = CaptureRealtime 최대값(5.0) 이어야 합니다.");
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
            Assert.LessOrEqual(_queue.Count, _queue.Capacity, $"Count는 Capacity({_queue.Capacity})를 초과할 수 없습니다.");
        }
    }
}
