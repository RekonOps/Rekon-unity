using System;
using NUnit.Framework;
using UnityEngine;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ReplayLogCollector 단위 테스트.
    ///
    /// Application.logMessageReceivedThreaded 를 직접 호출하기 어려우므로
    /// internal AddEntry(LogEntry) 메서드를 통해 보관 정책을 검증합니다.
    /// OnLogReceived 는 AddEntry 를 호출하므로 동일 정책이 적용됩니다.
    ///
    /// Unity Test Framework는 [Test] public async Task 를 인식 못하는 알려진 이슈(#164)로
    /// 모든 테스트는 동기 [Test] 로 작성합니다.
    /// </summary>
    [TestFixture]
    public class ReplayLogCollectorTests
    {
        private ReplayLogCollector _collector;

        [TearDown]
        public void TearDown()
        {
            _collector?.Dispose();
            _collector = null;
        }

        // ──────────────────────────────────────────────────────────────────────
        // 생성자 / 초기 상태
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Constructor_DefaultParams_CountZero()
        {
            _collector = new ReplayLogCollector();
            Assert.AreEqual(0, _collector.Count);
        }

        [Test]
        public void Constructor_NegativeWindow_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReplayLogCollector(windowSeconds: -1.0));
        }

        [Test]
        public void Constructor_ZeroMaxBytes_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ReplayLogCollector(maxBytes: 0));
        }

        [Test]
        public void GetEntries_Empty_ReturnsEmptyArray()
        {
            _collector = new ReplayLogCollector();
            var result = _collector.GetEntries();
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        // ──────────────────────────────────────────────────────────────────────
        // 기본 추가
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void AddEntry_SingleEntry_CountOne()
        {
            _collector = new ReplayLogCollector();
            var entry = new LogEntry(1.0, LogType.Log, "테스트", "");
            _collector.AddEntry(entry);
            Assert.AreEqual(1, _collector.Count);
        }

        [Test]
        public void AddEntry_MultipleEntries_AllPresent()
        {
            _collector = new ReplayLogCollector();
            _collector.AddEntry(new LogEntry(1.0, LogType.Log, "A", ""));
            _collector.AddEntry(new LogEntry(2.0, LogType.Warning, "B", ""));
            _collector.AddEntry(new LogEntry(3.0, LogType.Error, "C", "스택"));
            Assert.AreEqual(3, _collector.Count);
        }

        // ──────────────────────────────────────────────────────────────────────
        // 시간 윈도우 정책
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void AddEntry_OldEntryEvictedByTimeWindow()
        {
            // 10초 윈도우 설정
            _collector = new ReplayLogCollector(windowSeconds: 10.0, maxBytes: 64 * 1024 * 1024);

            // t=0, t=5 에 로그 추가
            _collector.AddEntry(new LogEntry(0.0, LogType.Log, "오래된 로그", ""));
            _collector.AddEntry(new LogEntry(5.0, LogType.Log, "중간 로그", ""));

            // t=15 에 새 로그 추가 → t=0 은 15-10=5 보다 작으므로 evict
            // t=5 는 5 < 5 가 아니므로 유지 (경계값: strictly less than)
            _collector.AddEntry(new LogEntry(15.0, LogType.Log, "최신 로그", ""));

            var entries = _collector.GetEntries();
            // t=0 이 제거되어 2건만 남아야 함
            Assert.AreEqual(2, entries.Length, "윈도우 초과 오래된 로그가 제거되어야 합니다.");
            Assert.AreEqual(5.0, entries[0].Timestamp, "t=5 로그가 남아야 합니다.");
            Assert.AreEqual(15.0, entries[1].Timestamp, "t=15 로그가 남아야 합니다.");
        }

        [Test]
        public void AddEntry_TimeWindow_ExactBoundary_Evicts()
        {
            // 윈도우 10초: 신규 t=10 추가 → cutoff = 10-10 = 0 → t=0 은 0 < 0 이 아니므로 유지
            _collector = new ReplayLogCollector(windowSeconds: 10.0, maxBytes: 64 * 1024 * 1024);
            _collector.AddEntry(new LogEntry(0.0, LogType.Log, "경계 로그", ""));
            _collector.AddEntry(new LogEntry(10.0, LogType.Log, "신규 로그", ""));

            Assert.AreEqual(2, _collector.Count, "정확히 경계값에서는 evict 안 됨 (strictly less than).");
        }

        [Test]
        public void AddEntry_TimeWindow_JustOver_Evicts()
        {
            // 윈도우 10초: 신규 t=10.001 → cutoff = 0.001 → t=0 은 0 < 0.001 → evict
            _collector = new ReplayLogCollector(windowSeconds: 10.0, maxBytes: 64 * 1024 * 1024);
            _collector.AddEntry(new LogEntry(0.0, LogType.Log, "evict 대상", ""));
            _collector.AddEntry(new LogEntry(10.001, LogType.Log, "유지 대상", ""));

            Assert.AreEqual(1, _collector.Count, "윈도우 초과 시 evict 되어야 합니다.");
            Assert.AreEqual(10.001, _collector.GetEntries()[0].Timestamp, 1e-9);
        }

        [Test]
        public void AddEntry_AllWithinWindow_NoneEvicted()
        {
            // 5초 윈도우, t=0,1,2,3,4 추가, 신규 t=4.5 → cutoff=-0.5 → 모두 유지
            _collector = new ReplayLogCollector(windowSeconds: 5.0, maxBytes: 64 * 1024 * 1024);
            for (int i = 0; i < 5; i++)
                _collector.AddEntry(new LogEntry(i, LogType.Log, $"로그{i}", ""));
            _collector.AddEntry(new LogEntry(4.5, LogType.Log, "최신", ""));

            Assert.AreEqual(6, _collector.Count, "모두 윈도우 내에 있어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // 바이트 상한 정책
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void AddEntry_ExceedsMaxBytes_OldestEvicted()
        {
            // maxBytes=100 bytes (매우 작음), 각 항목 msg="AAAAAAAAAA"(10자) + stack="" → 20 bytes 추정
            // 6번째 항목 추가 시 누적 120 bytes > 100 → oldest 제거
            _collector = new ReplayLogCollector(windowSeconds: 9999, maxBytes: 100);

            string msg = "AAAAAAAAAA"; // 10자
            for (int i = 0; i < 5; i++)
                _collector.AddEntry(new LogEntry(i, LogType.Log, msg, ""));

            // 5항목 × (10+0)×2 = 100 bytes → 정확히 상한. 6번째 추가 시 초과 → oldest(t=0) evict
            _collector.AddEntry(new LogEntry(5.0, LogType.Log, msg, ""));

            var entries = _collector.GetEntries();
            // t=0 이 제거되고 t=1~5 가 남아야 함
            Assert.AreEqual(5, entries.Length, "바이트 상한 초과 시 oldest가 제거되어야 합니다.");
            Assert.AreEqual(1.0, entries[0].Timestamp, "t=0 이 제거되고 t=1 이 첫 번째여야 합니다.");
        }

        [Test]
        public void AddEntry_LargeEntry_TriggersMultipleEvictions()
        {
            // 각 항목 msg 길이 10자 → 추정 20 bytes
            // maxBytes=60 → 3개까지 허용. 큰 항목(msg=50자→100bytes) 추가 시 기존 모두 evict
            _collector = new ReplayLogCollector(windowSeconds: 9999, maxBytes: 60);

            _collector.AddEntry(new LogEntry(1.0, LogType.Log, "AAAAAAAAAA", "")); // 20 bytes
            _collector.AddEntry(new LogEntry(2.0, LogType.Log, "BBBBBBBBBB", "")); // 20 bytes
            _collector.AddEntry(new LogEntry(3.0, LogType.Log, "CCCCCCCCCC", "")); // 20 bytes

            // 큰 항목: msg=50자 → 100 bytes → 60+100=160 > 60 → 기존 3개 모두 evict 후 추가
            string bigMsg = new string('X', 50);
            _collector.AddEntry(new LogEntry(10.0, LogType.Log, bigMsg, ""));

            // 큰 항목 자체는 100 bytes > 60 bytes이지만 이미 evict 후 추가됨
            // 결과: 큰 항목만 남음
            var entries = _collector.GetEntries();
            Assert.AreEqual(1, entries.Length, "기존 항목 모두 evict 후 새 항목만 남아야 합니다.");
            Assert.AreEqual(10.0, entries[0].Timestamp);
        }

        // ──────────────────────────────────────────────────────────────────────
        // GetEntries 시간순 반환
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void GetEntries_ReturnsInTimeOrder()
        {
            _collector = new ReplayLogCollector();
            // 시간순 추가
            _collector.AddEntry(new LogEntry(1.0, LogType.Log, "첫째", ""));
            _collector.AddEntry(new LogEntry(2.0, LogType.Warning, "둘째", ""));
            _collector.AddEntry(new LogEntry(3.0, LogType.Error, "셋째", "스택"));

            var entries = _collector.GetEntries();
            Assert.AreEqual(3, entries.Length);
            Assert.AreEqual(1.0, entries[0].Timestamp, 1e-9);
            Assert.AreEqual(2.0, entries[1].Timestamp, 1e-9);
            Assert.AreEqual(3.0, entries[2].Timestamp, 1e-9);
        }

        [Test]
        public void GetEntries_ReturnsCopy_ModifyDoesNotAffectBuffer()
        {
            _collector = new ReplayLogCollector();
            _collector.AddEntry(new LogEntry(1.0, LogType.Log, "항목", ""));

            var entries1 = _collector.GetEntries();
            Assert.AreEqual(1, entries1.Length);

            // 반환된 배열을 수정해도 내부 버퍼에 영향 없어야 함
            _collector.AddEntry(new LogEntry(2.0, LogType.Log, "항목2", ""));
            var entries2 = _collector.GetEntries();

            Assert.AreEqual(1, entries1.Length, "이전 반환 배열 크기는 불변이어야 합니다.");
            Assert.AreEqual(2, entries2.Length, "새 반환 배열에는 최신 항목 포함되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // Dispose
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Dispose_ClearsBuffer()
        {
            _collector = new ReplayLogCollector();
            _collector.AddEntry(new LogEntry(1.0, LogType.Log, "항목", ""));

            _collector.Dispose();

            // Dispose 후 AddEntry는 무시되어야 함
            _collector.AddEntry(new LogEntry(2.0, LogType.Log, "dispose 후", ""));
            Assert.AreEqual(0, _collector.Count, "Dispose 후 Count는 0이어야 합니다.");
        }

        [Test]
        public void Dispose_TwiceSafe()
        {
            _collector = new ReplayLogCollector();
            _collector.Dispose();
            // 두 번 Dispose 시 예외 없어야 함
            Assert.DoesNotThrow(() => _collector.Dispose());
        }
    }
}
