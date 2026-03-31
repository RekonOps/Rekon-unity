using System;
using Unity.Collections;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 비디오 프레임을 순환 배열에 저장하는 링버퍼.
    /// 슬롯별 byte[]를 생성자 또는 첫 프레임 크기 확정 시점에 사전 할당하여
    /// 이후 Add() 호출에서 GC 할당이 발생하지 않습니다.
    ///
    /// 구조:
    ///   - capacity = fps * bufferSeconds
    ///   - _preallocatedBuffers[]: capacity 개의 byte[] 슬롯 (사전 할당)
    ///   - _frames[]: 각 슬롯을 가리키는 FrameData 메타 정보
    ///   - _head: 다음 쓸 인덱스
    ///   - _count: 현재 저장된 프레임 수
    ///
    /// 할당 정책:
    ///   - EnsureBuffers(frameSize) 호출 시 모든 슬롯을 일괄 할당합니다.
    ///   - 첫 프레임 캡처 시점에 해상도가 확정되면 EnsureBuffers가 자동 호출됩니다.
    ///   - 해상도 변경(예: Game Window 리사이즈) 시 EnsureBuffers가 재할당합니다.
    ///   - 생성자 이후 new byte[] 호출 없음 (GC 할당 = 0).
    ///
    /// 메모리 총량:
    ///   capacity × frameSize (예: 1800 × ~800KB@720p = ~1.4GB, ~200KB@360p = ~360MB)
    ///
    /// 스레드 안전성:
    ///   Add/AddFromNativeArray/GetFrames는 lock으로 보호됩니다.
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

        // 사전 할당된 슬롯별 버퍼
        private byte[][] _preallocatedBuffers;
        private int _bufferSize;

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
        /// 첫 프레임 크기 확정 후 또는 해상도 변경 시 모든 슬롯을 일괄 사전 할당합니다.
        /// 이미 같은 크기로 할당된 경우 no-op입니다.
        /// 해상도 변경으로 frameSize가 커지면 기존 버퍼를 해제하고 재할당합니다.
        ///
        /// 이 메서드 이후 Add/AddFromNativeArray에서 new byte[] 호출이 없습니다.
        /// </summary>
        /// <param name="frameSize">프레임 1장의 바이트 수 (width × height × 4)</param>
        public void EnsureBuffers(int frameSize)
        {
            if (frameSize <= 0)
                return;

            if (_preallocatedBuffers != null && _bufferSize >= frameSize)
                return;

            // 메모리 상한 체크: 총 할당이 MaxMemoryMB를 초과하면 capacity 자동 축소
            const long MaxMemoryBytes = 1536L * 1024 * 1024; // 1.5GB 상한
            long totalBytes = (long)_capacity * frameSize;
            int effectiveCapacity = _capacity;
            if (totalBytes > MaxMemoryBytes)
            {
                effectiveCapacity = (int)(MaxMemoryBytes / frameSize);
                if (effectiveCapacity < 30) effectiveCapacity = 30; // 최소 1초분(30fps)
                Debug.LogWarning($"[Rekon] 메모리 상한({MaxMemoryBytes / 1024 / 1024}MB) 초과 → 버퍼 용량 자동 축소: {_capacity} → {effectiveCapacity}프레임");
                _capacity = effectiveCapacity;
                _frames = new FrameData[_capacity];
                _head = 0;
                _count = 0;
            }

            // 해상도 변경 또는 최초 할당: 슬롯 전체 재할당
            _preallocatedBuffers = new byte[_capacity][];
            for (int i = 0; i < _capacity; i++)
                _preallocatedBuffers[i] = new byte[frameSize];
            _bufferSize = frameSize;

            Debug.Log($"[Rekon] FrameRingBuffer 버퍼 사전 할당 완료: {_capacity}슬롯 × {frameSize / 1024.0 / 1024.0:F2}MB = {_capacity * (long)frameSize / 1024.0 / 1024.0:F0}MB");
        }

        /// <summary>
        /// NativeArray&lt;byte&gt;에서 직접 슬롯에 복사하여 프레임을 추가합니다.
        /// EnsureBuffers가 자동 호출되며, 이후 GC 할당이 발생하지 않습니다.
        ///
        /// 버퍼가 가득 찬 경우 가장 오래된 슬롯을 덮어씁니다 (소유권 이전 없음).
        /// </summary>
        /// <param name="source">GPU 읽기 완료된 NativeArray 픽셀 데이터 (RGBA32)</param>
        /// <param name="width">프레임 너비</param>
        /// <param name="height">프레임 높이</param>
        /// <param name="timestamp">캡처 시각</param>
        public void AddFromNativeArray(NativeArray<byte> source, int width, int height, double timestamp)
        {
            if (_disposed)
                return;

            if (!source.IsCreated || source.Length == 0 || width <= 0 || height <= 0)
                return;

            int dataLength = source.Length;
            EnsureBuffers(dataLength);

            lock (_lock)
            {
                // 사전 할당된 슬롯에 직접 복사 (new byte[] 없음)
                source.CopyTo(_preallocatedBuffers[_head]);
                _frames[_head] = new FrameData(_preallocatedBuffers[_head], dataLength, width, height, timestamp);
                _head = (_head + 1) % _capacity;
                if (_count < _capacity) _count++;
            }
        }

        /// <summary>
        /// 관리형 byte[] 소스에서 슬롯에 복사하여 프레임을 추가합니다.
        /// ReadPixels 폴백 경로에서 사용합니다.
        ///
        /// 버퍼가 가득 찬 경우 가장 오래된 슬롯을 덮어씁니다 (소유권 이전 없음).
        /// </summary>
        /// <param name="source">픽셀 데이터 원본 배열</param>
        /// <param name="dataLength">실제 유효 바이트 수</param>
        /// <param name="width">프레임 너비</param>
        /// <param name="height">프레임 높이</param>
        /// <param name="timestamp">캡처 시각</param>
        public void AddFromManagedArray(byte[] source, int dataLength, int width, int height, double timestamp)
        {
            if (_disposed)
                return;

            if (source == null || dataLength <= 0 || width <= 0 || height <= 0)
                return;

            EnsureBuffers(dataLength);

            lock (_lock)
            {
                // 사전 할당된 슬롯에 직접 복사 (new byte[] 없음)
                Buffer.BlockCopy(source, 0, _preallocatedBuffers[_head], 0, dataLength);
                _frames[_head] = new FrameData(_preallocatedBuffers[_head], dataLength, width, height, timestamp);
                _head = (_head + 1) % _capacity;
                if (_count < _capacity) _count++;
            }
        }

        /// <summary>
        /// 프레임을 링버퍼에 추가합니다 (외부 FrameData 직접 추가).
        /// 사전 할당 방식이 아니므로, 가급적 AddFromNativeArray / AddFromManagedArray 사용을 권장합니다.
        ///
        /// 버퍼가 가득 찬 경우 가장 오래된 프레임을 덮어씁니다.
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
                _frames[_head] = frame;
                _head = (_head + 1) % _capacity;
                if (_count < _capacity) _count++;
            }
        }

        /// <summary>
        /// 현재 버퍼의 모든 프레임을 타임스탬프 오름차순으로 반환합니다.
        /// 내부 배열의 복사본을 반환합니다. byte[] 소유권은 링버퍼가 유지합니다.
        ///
        /// 주의: 반환된 FrameData의 byte[]를 외부에서 반환하거나 수정하지 마세요.
        ///       사전 할당 슬롯이므로 버퍼 상태를 공유합니다.
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

                // byte[] 독립 복사 — 인코딩 중 링버퍼 슬롯 덮어쓰기로 인한 데이터 오염 방지
                // (캡처 트리거 시 1회만 호출되므로 GC 부담 미미)
                for (int i = 0; i < result.Length; i++)
                {
                    var f = result[i];
                    if (f.Data != null)
                    {
                        byte[] copy = new byte[f.DataLength];
                        Buffer.BlockCopy(f.Data, 0, copy, 0, f.DataLength);
                        result[i] = new FrameData(copy, f.DataLength, f.Width, f.Height, f.Timestamp);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 현재 버퍼의 모든 프레임을 타임스탬프 오름차순으로 반환합니다.
        /// 사전 할당 방식에서는 소유권 이전이 불필요하므로, GetFrames()와 동일하게 동작합니다.
        /// 하위 호환을 위해 유지됩니다.
        ///
        /// 주의: 반환된 FrameData.Data를 ArrayPool에 반환하지 마세요 (사전 할당 슬롯입니다).
        /// </summary>
        /// <returns>시간순 정렬된 FrameData 배열</returns>
        public FrameData[] TakeFrames()
        {
            // 사전 할당 방식에서는 소유권 이전이 불필요 — GetFrames()와 동일
            return GetFrames();
        }

        /// <summary>
        /// 버퍼를 비웁니다.
        /// 사전 할당된 슬롯 자체는 유지되고 메타 정보만 초기화됩니다.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                // 슬롯 메타 정보만 초기화 (byte[] 반환 불필요)
                for (int i = 0; i < _capacity; i++)
                    _frames[i] = default;
                _head = 0;
                _count = 0;
            }
        }

        /// <summary>
        /// 링버퍼와 내부 풀을 해제합니다.
        /// 사전 할당된 슬롯은 GC가 자동 수집합니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            lock (_lock)
            {
                // 사전 할당 방식: ArrayPool 반환 불필요
                // 슬롯 참조만 해제하여 GC 수집 허용
                for (int i = 0; i < _capacity; i++)
                    _frames[i] = default;
                _preallocatedBuffers = null;
                _count = 0;
            }

            _pool?.Dispose();
        }
    }
}
