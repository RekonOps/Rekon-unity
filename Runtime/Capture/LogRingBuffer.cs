using System;
using UnityEngine;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// Application.logMessageReceived 이벤트를 구독하여 로그를 순환 배열에 저장하는 링버퍼.
    ///
    /// 구조:
    ///   - 고정 크기 배열 (capacity = BugBeaconSettings.logBufferSize)
    ///   - _head: 다음 쓸 위치 (쓰기 후 증가)
    ///   - _count: 현재 저장된 항목 수 (capacity를 넘으면 capacity 유지)
    ///
    /// 스레드 안전성:
    ///   Application.logMessageReceived는 임의 스레드에서 호출될 수 있으므로
    ///   lock으로 동시 접근을 보호합니다.
    /// </summary>
    public class LogRingBuffer : ILogCollector, IDisposable
    {
        private readonly LogEntry[] _buffer;
        private readonly int _capacity;
        private int _head;     // 다음 쓸 인덱스
        private int _count;    // 현재 저장된 항목 수
        private bool _disposed;
        private readonly object _lock = new object();
        private double _lastMainThreadTime; // 백그라운드 스레드 fallback용

        /// <summary>현재 버퍼에 저장된 로그 항목 수</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                    return _count;
            }
        }

        /// <summary>
        /// 지정한 용량으로 링버퍼를 초기화하고 Unity 로그 콜백을 등록합니다.
        /// </summary>
        /// <param name="capacity">최대 저장 가능한 로그 항목 수</param>
        public LogRingBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "용량은 1 이상이어야 합니다.");

            _capacity = capacity;
            _buffer = new LogEntry[capacity];
            _head = 0;
            _count = 0;

            Application.logMessageReceivedThreaded += OnLogReceived;
        }

        /// <summary>
        /// 로그 콜백 등록을 해제합니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Application.logMessageReceivedThreaded -= OnLogReceived;
            _disposed = true;
        }

        /// <summary>
        /// 현재 버퍼에 저장된 모든 로그를 시간 오름차순으로 반환합니다.
        /// 내부 배열의 복사본을 반환하므로 호출 후 변경해도 안전합니다.
        /// </summary>
        public LogEntry[] GetEntries()
        {
            lock (_lock)
            {
                if (_count == 0)
                    return Array.Empty<LogEntry>();

                var result = new LogEntry[_count];

                if (_count < _capacity)
                {
                    // 버퍼가 꽉 차지 않은 경우: 인덱스 0부터 _count-1까지 순서대로
                    Array.Copy(_buffer, 0, result, 0, _count);
                }
                else
                {
                    // 버퍼가 꽉 찬 경우: _head부터 끝까지, 그다음 0부터 _head-1까지
                    int firstPartLen = _capacity - _head;
                    Array.Copy(_buffer, _head, result, 0, firstPartLen);
                    Array.Copy(_buffer, 0, result, firstPartLen, _head);
                }

                // 타임스탬프 기준 오름차순 정렬
                Array.Sort(result, (a, b) => a.Timestamp.CompareTo(b.Timestamp));
                return result;
            }
        }

        /// <summary>
        /// 로그 항목을 직접 추가합니다 (테스트 및 내부 사용).
        /// </summary>
        public void Add(LogEntry entry)
        {
            lock (_lock)
            {
                _buffer[_head] = entry;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity)
                    _count++;
            }
        }

        /// <summary>
        /// 버퍼를 비웁니다.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _head = 0;
                _count = 0;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 콜백
        // ──────────────────────────────────────────────────────────────

        private void OnLogReceived(string condition, string stackTrace, LogType logType)
        {
            if (_disposed)
                return;

            // Time.realtimeSinceStartupAsDouble는 메인 스레드 전용.
            // 백그라운드 스레드(Task.Run 등)에서 Debug.Log 호출 시
            // 이 콜백이 같은 스레드에서 발동되므로 스레드 안전한 타임스탬프 사용.
            double timestamp;
            try
            {
                timestamp = Time.realtimeSinceStartupAsDouble;
                _lastMainThreadTime = timestamp;
            }
            catch
            {
                // 백그라운드 스레드에서 호출된 경우 마지막 메인 스레드 시간 사용
                timestamp = _lastMainThreadTime;
            }

            var entry = new LogEntry(
                timestamp: timestamp,
                logType: logType,
                message: condition,
                stackTrace: stackTrace
            );

            Add(entry);
        }
    }
}
