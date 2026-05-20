using System;
using System.Collections.Generic;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// team_pro 전용 시간 윈도우 기반 로그 수집기.
    ///
    /// LogRingBuffer와 동일하게 Application.logMessageReceivedThreaded 를 구독하되
    /// 줄 수 고정(500개) 대신 시간 윈도우(기본 180초) + 누적 바이트 상한(기본 32MB) 정책으로 보관합니다.
    ///
    /// 보관 정책 (Add 시 순서대로 적용):
    ///   1. 시간 윈도우: oldest.Timestamp &lt; now - windowSeconds 이면 oldest 제거
    ///   2. 바이트 상한: 누적 추정 바이트(= msg.Length + stack.Length 기준) &gt; maxBytes 이면 oldest 제거
    ///
    /// 스레드 안전: lock(_lock)으로 LinkedList 접근 보호.
    /// Time.realtimeSinceStartupAsDouble 처리: LogRingBuffer와 동일 패턴 (try/catch + fallback).
    ///
    /// ⚠️ LogRingBuffer, PeriodicFlushManager, CrashRecovery 에 0 의존/0 변경.
    /// </summary>
    public class ReplayLogCollector : ILogCollector, IDisposable
    {
        // ── 보관 정책 기본값 ────────────────────────────────────────────────
        private const double DefaultWindowSeconds = 180.0;
        private const long   DefaultMaxBytes      = 32L * 1024 * 1024; // 32 MB

        // ── 내부 상태 ────────────────────────────────────────────────────────
        private readonly LinkedList<LogEntry> _entries = new LinkedList<LogEntry>();
        private readonly object _lock = new object();

        private readonly double _windowSeconds;
        private readonly long   _maxBytes;

        /// <summary>현재 추정 누적 바이트 (msg.Length + stack.Length 기준, char당 2bytes 근사)</summary>
        private long _estimatedBytes;

        private bool _disposed;

        /// <summary>백그라운드 스레드 fallback용 마지막 메인스레드 타임스탬프</summary>
        private double _lastMainThreadTime;

        // ── ILogCollector ────────────────────────────────────────────────────

        /// <summary>현재 보관 중인 로그 항목 수</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                    return _entries.Count;
            }
        }

        /// <summary>
        /// ReplayLogCollector를 초기화하고 Unity 로그 콜백을 등록합니다.
        /// </summary>
        /// <param name="windowSeconds">보관할 최대 시간 윈도우 (초, 기본 180)</param>
        /// <param name="maxBytes">보관할 최대 추정 바이트 (기본 32MB)</param>
        public ReplayLogCollector(double windowSeconds = DefaultWindowSeconds, long maxBytes = DefaultMaxBytes)
        {
            if (windowSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowSeconds), "시간 윈도우는 0보다 커야 합니다.");
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes), "최대 바이트는 0보다 커야 합니다.");

            _windowSeconds = windowSeconds;
            _maxBytes      = maxBytes;

            Application.logMessageReceivedThreaded += OnLogReceived;
        }

        /// <summary>
        /// 로그 콜백 등록을 해제하고 내부 버퍼를 비웁니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Application.logMessageReceivedThreaded -= OnLogReceived;
            _disposed = true;

            lock (_lock)
            {
                _entries.Clear();
                _estimatedBytes = 0;
            }
        }

        /// <summary>
        /// 현재 보관 중인 모든 로그를 시간 오름차순으로 반환합니다 (복사본).
        /// LinkedList는 삽입 순서(시간순)이므로 추가 정렬 없이 반환합니다.
        /// </summary>
        public LogEntry[] GetEntries()
        {
            lock (_lock)
            {
                if (_entries.Count == 0)
                    return Array.Empty<LogEntry>();

                var result = new LogEntry[_entries.Count];
                int i = 0;
                foreach (var entry in _entries)
                    result[i++] = entry;

                return result;
            }
        }

        // ── 내부 API (테스트용) ───────────────────────────────────────────────

        /// <summary>
        /// 로그 항목을 직접 추가합니다 (테스트에서 logMessageReceivedThreaded 직접 호출 대체 용도).
        /// OnLogReceived가 이 메서드를 호출하므로 보관 정책이 동일하게 적용됩니다.
        /// </summary>
        internal void AddEntry(LogEntry entry)
        {
            if (_disposed)
                return;

            long entryBytes = (long)((entry.Message?.Length ?? 0) + (entry.StackTrace?.Length ?? 0)) * 2;

            lock (_lock)
            {
                // 1단계: 시간 윈도우 초과 oldest 제거
                double cutoff = entry.Timestamp - _windowSeconds;
                while (_entries.Count > 0 && _entries.First.Value.Timestamp < cutoff)
                {
                    var first = _entries.First.Value;
                    long removedBytes = (long)((first.Message?.Length ?? 0) + (first.StackTrace?.Length ?? 0)) * 2;
                    _entries.RemoveFirst();
                    _estimatedBytes -= removedBytes;
                    if (_estimatedBytes < 0) _estimatedBytes = 0;
                }

                // 2단계: 바이트 상한 초과 oldest 제거 (신규 항목 포함 가정하여 미리 evict)
                while (_entries.Count > 0 && (_estimatedBytes + entryBytes) > _maxBytes)
                {
                    var first = _entries.First.Value;
                    long removedBytes = (long)((first.Message?.Length ?? 0) + (first.StackTrace?.Length ?? 0)) * 2;
                    _entries.RemoveFirst();
                    _estimatedBytes -= removedBytes;
                    if (_estimatedBytes < 0) _estimatedBytes = 0;
                }

                // 신규 항목 추가 (LinkedList AddLast = 시간 오름차순 유지)
                _entries.AddLast(entry);
                _estimatedBytes += entryBytes;
            }
        }

        // ── 내부 콜백 ────────────────────────────────────────────────────────

        private void OnLogReceived(string condition, string stackTrace, LogType logType)
        {
            if (_disposed)
                return;

            // Time.realtimeSinceStartupAsDouble는 메인 스레드 전용.
            // LogRingBuffer와 동일 패턴: 백그라운드 스레드 호출 시 fallback.
            double timestamp;
            try
            {
                timestamp = Time.realtimeSinceStartupAsDouble;
                _lastMainThreadTime = timestamp;
            }
            catch
            {
                // 백그라운드 스레드에서 호출된 경우 마지막 메인스레드 시간 사용
                timestamp = _lastMainThreadTime;
            }

            var entry = new LogEntry(
                timestamp:  timestamp,
                logType:    logType,
                message:    condition,
                stackTrace: stackTrace
            );

            AddEntry(entry);
        }
    }
}
