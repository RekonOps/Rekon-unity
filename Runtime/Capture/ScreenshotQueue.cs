using System;
using System.Collections.Generic;

namespace RekonOps.Rekon
{
    /// <summary>스크린샷 큐의 개별 항목</summary>
    public readonly struct ScreenshotEntry
    {
        public readonly byte[] PngBytes;
        public readonly DateTime Timestamp;

        /// <summary>
        /// 스크린샷이 캡처된 시점의 Time.realtimeSinceStartupAsDouble 값 (초).
        /// 로그 .jsonl 의 t_abs 와 동일한 시간축 — 싱크 마커로 사용됩니다.
        /// 기존 2-파라미터 생성자 호출 시 0.0 (하위 호환 기본값).
        /// team_pro 전용 스크린샷 리포트에서 captured_t_abs 로 전송됩니다.
        /// </summary>
        public readonly double CaptureRealtime;

        /// <summary>기존 생성자 (하위 호환 유지 — CaptureRealtime = 0.0)</summary>
        public ScreenshotEntry(byte[] pngBytes, DateTime timestamp)
        {
            PngBytes       = pngBytes;
            Timestamp      = timestamp;
            CaptureRealtime = 0.0;
        }

        /// <summary>
        /// team_pro 싱크용 생성자.
        /// captureRealtime: 캡처 시점의 Time.realtimeSinceStartupAsDouble 값.
        /// </summary>
        public ScreenshotEntry(byte[] pngBytes, DateTime timestamp, double captureRealtime)
        {
            PngBytes        = pngBytes;
            Timestamp       = timestamp;
            CaptureRealtime = captureRealtime;
        }
    }

    /// <summary>
    /// Play Mode 스크린샷 메모리 큐.
    /// lock 기반 스레드 안전, FIFO eviction, byte[] 보관.
    /// </summary>
    public class ScreenshotQueue
    {
        /// <summary>
        /// 큐가 보관할 수 있는 최대 스크린샷 장수. 생성자에서 플랜값으로 주입됩니다.
        ///   free 3 / team 5 / team_pro 10 (백엔드 validate-license 의 maxAllowedScreenshotCount).
        /// </summary>
        public int Capacity { get; }

        private readonly List<ScreenshotEntry> _entries = new List<ScreenshotEntry>();
        private readonly object _lock = new object();

        /// <summary>
        /// 스크린샷 큐를 생성합니다.
        /// </summary>
        /// <param name="capacity">
        /// 최대 보관 장수(플랜값). 1 미만이면 기본값 5 로 가드합니다.
        /// 기본값 5 — 인자 미지정 시 기존 동작(team 기준)을 유지합니다.
        /// </param>
        public ScreenshotQueue(int capacity = 5)
        {
            Capacity = capacity < 1 ? 5 : capacity;
        }

        /// <summary>현재 큐 크기 (0~Capacity)</summary>
        public int Count { get { lock(_lock) { return _entries.Count; } } }

        /// <summary>
        /// 큐에 스크린샷을 추가합니다. Capacity 초과 시 가장 오래된 항목을 삭제(FIFO eviction)합니다.
        /// (기존 오버로드 — 하위 호환 유지, CaptureRealtime = 0.0)
        /// </summary>
        /// <returns>eviction이 발생하면 true, 그렇지 않으면 false</returns>
        public bool Enqueue(byte[] pngBytes, DateTime timestamp)
        {
            return Enqueue(pngBytes, timestamp, 0.0);
        }

        /// <summary>
        /// 큐에 스크린샷을 추가합니다. Capacity 초과 시 가장 오래된 항목을 삭제(FIFO eviction)합니다.
        /// team_pro 싱크용 오버로드 — captureRealtime: Time.realtimeSinceStartupAsDouble.
        /// </summary>
        /// <param name="pngBytes">PNG 바이트</param>
        /// <param name="timestamp">캡처 시각 (DateTime.UtcNow)</param>
        /// <param name="captureRealtime">캡처 시점 Time.realtimeSinceStartupAsDouble</param>
        /// <returns>eviction이 발생하면 true, 그렇지 않으면 false</returns>
        public bool Enqueue(byte[] pngBytes, DateTime timestamp, double captureRealtime)
        {
            if (pngBytes == null || pngBytes.Length == 0) return false;
            lock (_lock)
            {
                bool evicted = _entries.Count >= Capacity;
                if (evicted)
                    _entries.RemoveAt(0);
                _entries.Add(new ScreenshotEntry(pngBytes, timestamp, captureRealtime));
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
