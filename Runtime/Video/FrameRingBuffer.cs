using System;
using System.Buffers;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 비디오 프레임을 순환 배열에 저장하는 링버퍼.
    /// System.Buffers.ArrayPool&lt;byte&gt;을 사용하여 GC 압력을 최소화합니다.
    ///
    /// 구조:
    ///   - capacity = fps * bufferSeconds
    ///   - _frames[]: FrameData 저장 (ArrayPool 대여 byte[] 포인터 보관)
    ///   - _head: 다음 쓸 인덱스
    ///   - _count: 현재 저장된 프레임 수
    ///
    /// 풀링 정책:
    ///   - Add() 시 기존 슬롯의 byte[]를 ArrayPool에 반환한 뒤 새 프레임으로 교체
    ///   - Dispose() 시 버퍼에 남아 있는 모든 byte[]를 ArrayPool에 반환
    ///   - FrameData.Data가 null인 슬롯은 반환 생략
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
        private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;

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
        /// 풀링: 덮어쓰여지는 슬롯의 byte[]를 ArrayPool에 반환합니다.
        /// FrameData.Data는 ArrayPool에서 대여한 배열이어야 합니다 (FrameCapturer 참조).
        /// </summary>
        /// <param name="frame">추가할 프레임 데이터 (Data는 ArrayPool 대여 배열)</param>
        public void Add(FrameData frame)
        {
            if (_disposed)
                return;

            if (!frame.IsValid)
                return;

            lock (_lock)
            {
                // 버퍼가 가득 찬 경우: 덮어쓰여지는 슬롯의 byte[]를 ArrayPool에 반환
                if (_count == _capacity)
                {
                    var evicted = _frames[_head];
                    if (evicted.Data != null)
                        _arrayPool.Return(evicted.Data);
                }

                _frames[_head] = frame;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity)
                    _count++;
            }
        }

        /// <summary>
        /// 현재 버퍼의 모든 프레임을 타임스탬프 오름차순으로 반환합니다.
        /// 내부 배열의 복사본을 반환합니다. byte[] 소유권은 링버퍼가 유지합니다.
        ///
        /// 주의: 반환된 FrameData의 byte[]를 직접 ArrayPool에 반환하지 마세요.
        ///       소유권을 이전받으려면 TakeFrames()를 사용하세요.
        /// </summary>
        /// <returns>시간순 정렬된 FrameData 배열 (읽기 전용 용도)</returns>
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
        /// 현재 버퍼의 모든 프레임을 타임스탬프 오름차순으로 반환하고,
        /// 내부 슬롯의 byte[] 참조를 null로 만들어 소유권을 호출자에게 이전합니다.
        ///
        /// 사용 후 반드시 각 FrameData.Data를 ArrayPool&lt;byte&gt;.Shared.Return()으로 반환하세요.
        /// 이 메서드 이후 링버퍼는 비워지지 않으며, 새 프레임을 계속 추가할 수 있습니다.
        /// 단, 이전된 슬롯은 이미 null 처리되었으므로 Dispose/Clear 시 중복 반환이 발생하지 않습니다.
        /// </summary>
        /// <returns>시간순 정렬된 FrameData 배열 (호출자가 byte[] 반환 책임)</returns>
        public FrameData[] TakeFrames()
        {
            lock (_lock)
            {
                if (_count == 0)
                    return Array.Empty<FrameData>();

                var result = new FrameData[_count];

                if (_count < _capacity)
                {
                    // 아직 꽉 차지 않음: 0부터 _count-1까지
                    for (int i = 0; i < _count; i++)
                    {
                        result[i] = _frames[i];
                        _frames[i] = default; // 소유권 이전: 내부 슬롯을 null로 초기화
                    }
                }
                else
                {
                    // 꽉 찬 경우: _head부터 끝까지 + 0부터 _head-1까지
                    int firstPart = _capacity - _head;
                    for (int i = 0; i < firstPart; i++)
                    {
                        result[i] = _frames[_head + i];
                        _frames[_head + i] = default;
                    }
                    for (int i = 0; i < _head; i++)
                    {
                        result[firstPart + i] = _frames[i];
                        _frames[i] = default;
                    }
                }

                // 타임스탬프 오름차순 정렬
                Array.Sort(result, (a, b) => a.Timestamp.CompareTo(b.Timestamp));

                // 슬롯 초기화 후 카운터 리셋 (head는 유지하여 연속 쓰기 가능)
                _head = 0;
                _count = 0;

                return result;
            }
        }

        /// <summary>
        /// 버퍼를 비웁니다.
        /// 현재 보관 중인 모든 프레임의 byte[]를 ArrayPool에 반환합니다.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                // 현재 저장된 프레임의 byte[]를 모두 반환
                for (int i = 0; i < _count; i++)
                {
                    int idx = (_head - _count + i + _capacity) % _capacity;
                    if (_frames[idx].Data != null)
                        _arrayPool.Return(_frames[idx].Data);
                    _frames[idx] = default;
                }
                _head = 0;
                _count = 0;
            }
        }

        /// <summary>
        /// 링버퍼와 내부 풀을 해제합니다.
        /// 보관 중인 모든 byte[]를 ArrayPool에 반환합니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            lock (_lock)
            {
                // 아직 반환되지 않은 모든 프레임의 byte[]를 ArrayPool에 반환
                for (int i = 0; i < _count; i++)
                {
                    int idx = (_head - _count + i + _capacity) % _capacity;
                    if (_frames[idx].Data != null)
                        _arrayPool.Return(_frames[idx].Data);
                    _frames[idx] = default;
                }
                _count = 0;
            }

            _pool?.Dispose();
        }
    }
}
