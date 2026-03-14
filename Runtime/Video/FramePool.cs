using System;
using System.Collections.Generic;
using Unity.Collections;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// NativeArray&lt;byte&gt; 풀 관리자.
    /// GC 압력을 최소화하기 위해 같은 크기의 NativeArray를 재사용합니다.
    ///
    /// 스레드 안전성:
    ///   Rent/Return은 lock으로 보호됩니다.
    /// </summary>
    public class FramePool : IDisposable
    {
        private readonly Dictionary<int, Stack<NativeArray<byte>>> _pools
            = new Dictionary<int, Stack<NativeArray<byte>>>();

        private readonly object _lock = new object();
        private bool _disposed;

        /// <summary>
        /// 지정 크기의 NativeArray를 풀에서 대여합니다.
        /// 풀에 가용한 배열이 없으면 새로 할당합니다.
        /// </summary>
        /// <param name="size">바이트 크기</param>
        /// <returns>대여된 NativeArray</returns>
        public NativeArray<byte> Rent(int size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "크기는 1 이상이어야 합니다.");

            if (_disposed)
                throw new ObjectDisposedException(nameof(FramePool));

            lock (_lock)
            {
                if (_pools.TryGetValue(size, out var stack) && stack.Count > 0)
                    return stack.Pop();
            }

            // 풀 미스: 새로 할당
            return new NativeArray<byte>(size, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>
        /// 사용이 끝난 NativeArray를 풀에 반환합니다.
        /// 이미 Disposed된 풀에 반환하면 배열을 즉시 해제합니다.
        /// </summary>
        /// <param name="array">반환할 배열</param>
        public void Return(NativeArray<byte> array)
        {
            if (!array.IsCreated)
                return;

            if (_disposed)
            {
                // 풀이 이미 해제된 경우 배열 직접 해제
                array.Dispose();
                return;
            }

            lock (_lock)
            {
                int size = array.Length;
                if (!_pools.TryGetValue(size, out var stack))
                {
                    stack = new Stack<NativeArray<byte>>();
                    _pools[size] = stack;
                }
                stack.Push(array);
            }
        }

        /// <summary>
        /// 풀에 있는 모든 NativeArray를 해제합니다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            lock (_lock)
            {
                foreach (var stack in _pools.Values)
                {
                    while (stack.Count > 0)
                    {
                        var array = stack.Pop();
                        if (array.IsCreated)
                            array.Dispose();
                    }
                }
                _pools.Clear();
            }
        }
    }
}
