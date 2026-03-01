using System;
using System.Collections.Generic;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// IContextProvider 구현체를 등록/해제하고, 전체 컨텍스트 데이터를 수집하는 레지스트리.
    ///
    /// 스레드 안전성:
    ///   Register/Unregister/CollectAll 모두 lock으로 보호됩니다.
    ///
    /// 우선순위:
    ///   Key 충돌 시 나중에 등록된 프로바이더의 값이 앞의 값을 덮어씁니다.
    /// </summary>
    public class ContextProviderRegistry
    {
        private readonly List<IContextProvider> _providers = new List<IContextProvider>();
        private readonly object _lock = new object();

        /// <summary>
        /// 현재 등록된 프로바이더 수 (테스트용)
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                    return _providers.Count;
            }
        }

        /// <summary>
        /// 컨텍스트 프로바이더를 등록합니다.
        /// 이미 등록된 프로바이더는 중복 등록되지 않습니다.
        /// </summary>
        /// <param name="provider">등록할 프로바이더</param>
        public void Register(IContextProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            lock (_lock)
            {
                if (!_providers.Contains(provider))
                    _providers.Add(provider);
            }
        }

        /// <summary>
        /// 컨텍스트 프로바이더 등록을 해제합니다.
        /// </summary>
        /// <param name="provider">해제할 프로바이더</param>
        public void Unregister(IContextProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            lock (_lock)
            {
                _providers.Remove(provider);
            }
        }

        /// <summary>
        /// 등록된 모든 프로바이더에서 K/V 데이터를 수집하여 병합합니다.
        /// 예외가 발생한 프로바이더는 건너뜁니다.
        /// </summary>
        /// <returns>병합된 K/V 딕셔너리 (등록 순서 기준, 나중 등록 우선)</returns>
        public Dictionary<string, string> CollectAll()
        {
            var result = new Dictionary<string, string>();

            List<IContextProvider> snapshot;
            lock (_lock)
                snapshot = new List<IContextProvider>(_providers);

            foreach (var provider in snapshot)
            {
                try
                {
                    var context = provider.GetContext();
                    if (context == null)
                        continue;

                    foreach (var kvp in context)
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BugOneTouch] ContextProvider '{provider.GetType().Name}' 오류: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// 모든 프로바이더 등록을 해제합니다.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
                _providers.Clear();
        }
    }
}
