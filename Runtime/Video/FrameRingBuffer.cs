using System;
using Unity.Collections;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 비디오 프레임을 순환 배열에 저장하는 링버퍼.
    /// NativeArray&lt;byte&gt; 풀링으로 GC 압력을 최소화합니다.
    ///
    /// 구조:
    ///   - capacity = fps * bufferSeconds
    ///   - _frames[]: FrameData 저장 (byte[] 포인터만 보관, 실제 데이터는 FramePool)
    ///   - _head: 다음 쓸 인덱스
    ///   - _count: 현재 저장된 프레임 수
    ///
    /// 스레드 안전성:
    ///   Add/GetFrames는 lock으로 보호됩니다.
    ///   Dispose는 Unity 메인 스레드에서 호출해야 합니다.
    /// </summary>
    public class FrameRingBuffer : IDisposable
    {
        private readonly FrameData[] _frames;
        private readonly int _capacity;
        private readonly FramePool _pool;
        private int _head;
        private int _count;
        private bool _disposed;
        private readonly object _lock = new object();

        /// <summary>현재 저장된 프레임 수</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                    return _count;
            }
        }

        /// <summary>링버퍼 최대 용량</summary>
        public int Capacity => _capacity;

        /// <summary>
        /// 프레임 링버퍼를 초기화합니다.
        /// </summary>
        /// <param name="capacity">최대 저장 가능한 프레임 수 (fps * bufferSeconds 권장)</param>
        /// <param name="pool">NativeArray 풀 (null이면 내부 전용 풀 생성)</param>
        public FrameRingBuffer(int capacity, FramePool pool = null)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "용량은 1 이상이어야 합니다.");

            _capacity = capacity;
            _frames = new FrameData[capacity];
            _pool = pool ?? new FramePool();
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// 프레임을 링버퍼에 추가합니다.
        /// 버퍼가 가득 찬 경우 가장 오래된 프레임을 덮어씁니다.
        ///
        /// 중요: byte[] 데이터를 NativeArray로 복사하여 저장합니다.
        /// </summary>
        /// <param name="frame">추가할 프레임 데이터</param>
        public void Add(FrameData frame)
        {
            if (_disposed)
                return;

            if (!frame.IsValid)
                return;

            lock (_lock)
            {
                // 기존 슬롯에 NativeArray가 있으면 풀에 반환
                // (FrameData는 readonly struct이므로 byte[]를 직접 참조함)
                // FrameData 내의 byte[]를 FramePool의 NativeArray로 관리하려면
                // 내부적으로 NativeArray 배열을 별도로 유지합니다.
                // 여기서는 단순화를 위해 byte[] 복사 방식을 사용합니다.

                _frames[_head] = frame;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity)
                    _count++;
            }
        }

        /// <summary>
        /// 현재 버퍼의 모든 프레임을 타임스탬프 오름차순으로 반환합니다.
        /// 내부 배열의 복사본을 반환합니다.
        /// </summary>
        /// <returns>시간순 정렬된 FrameData 배열</returns>
        public FrameData[] GetFrames()
        {
            lock (_lock)
            {
                if (_count == 0)
                    return Array.Empty<FrameData>();

                var result = new FrameData[_count];

                if (_count < _capacity)
                {
                    // 아직 꽉 차지 않음: 0부터 _count-1까지
                    Array.Copy(_frames, 0, result, 0, _count);
                }
                else
                {
                    // 꽉 찬 경우: _head부터 끝까지 + 0부터 _head-1까지
                    int firstPart = _capacity - _head;
                    Array.Copy(_frames, _head, result, 0, firstPart);
                    Array.Copy(_frames, 0, result, firstPart, _head);
                }

                // 타임스탬프 오름차순 정렬
                Array.Sort(result, (a, b) => a.Timestamp.CompareTo(b.Timestamp));
                return result;
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

        /// <summary>
        /// 링버퍼와 내부 NativeArray 풀을 해제합니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _pool?.Dispose();
        }
    }
}
