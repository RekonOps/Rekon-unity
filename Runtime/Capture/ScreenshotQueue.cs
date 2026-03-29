using System;
using System.Collections.Generic;

namespace RekonOps.Rekon
{
    /// <summary>스크린샷 큐의 개별 항목</summary>
    public readonly struct ScreenshotEntry
    {
        public readonly byte[] PngBytes;
        public readonly DateTime Timestamp;

        public ScreenshotEntry(byte[] pngBytes, DateTime timestamp)
        {
            PngBytes  = pngBytes;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Play Mode 스크린샷 메모리 큐.
    /// lock 기반 스레드 안전, FIFO eviction, byte[] 보관.
    /// </summary>
    public class ScreenshotQueue
    {
        public const int MaxCapacity = 5;

        private readonly List<ScreenshotEntry> _entries = new List<ScreenshotEntry>();
        private readonly object _lock = new object();

        /// <summary>현재 큐 크기 (0~5)</summary>
        public int Count { get { lock(_lock) { return _entries.Count; } } }

        /// <summary>
        /// 큐에 스크린샷을 추가합니다. 5장 초과 시 가장 오래된 항목을 삭제(FIFO eviction)합니다.
        /// </summary>
        /// <returns>eviction이 발생하면 true, 그렇지 않으면 false</returns>
        public bool Enqueue(byte[] pngBytes, DateTime timestamp)
        {
            if (pngBytes == null || pngBytes.Length == 0) return false;
            lock (_lock)
            {
                bool evicted = _entries.Count >= MaxCapacity;
                if (evicted)
                    _entries.RemoveAt(0);
                _entries.Add(new ScreenshotEntry(pngBytes, timestamp));
                return evicted;
            }
        }

        /// <summary>큐의 모든 항목을 반환하고 큐를 초기화합니다.</summary>
        public ScreenshotEntry[] DrainAll()
        {
            lock (_lock)
            {
                var result = _entries.ToArray();
                _entries.Clear();
                return result;
            }
        }

        /// <summary>큐를 초기화합니다 (Play Mode 종료 시 호출).</summary>
        public void Clear()
        {
            lock (_lock) { _entries.Clear(); }
        }

        /// <summary>현재 항목을 읽기 전용으로 반환합니다 (UI 표시용).</summary>
        public ScreenshotEntry[] PeekAll()
        {
            lock (_lock) { return _entries.ToArray(); }
        }
    }
}
