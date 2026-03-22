using System;
using System.Collections.Generic;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 게임 코드에서 버그 리포트에 포함할 커스텀 K/V 데이터를 관리하는 정적 API.
    ///
    /// RekonContext 자체도 IContextProvider를 구현하여
    /// ContextProviderRegistry에 자동 등록됩니다.
    ///
    /// 사용 예:
    ///   RekonContext.Add("current_level", "5");
    ///   RekonContext.Add("player_hp", playerHp.ToString());
    ///   RekonContext.Remove("current_level");
    ///   RekonContext.Clear();
    /// </summary>
    public static class RekonContext
    {
        private static readonly Dictionary<string, string> s_Context = new Dictionary<string, string>();
        private static readonly object s_Lock = new object();

        /// <summary>
        /// 현재 등록된 항목 수 (테스트용)
        /// </summary>
        public static int Count
        {
            get
            {
                lock (s_Lock)
                    return s_Context.Count;
            }
        }

        /// <summary>
        /// 컨텍스트 데이터를 추가하거나 업데이트합니다.
        /// </summary>
        /// <param name="key">키 (null이면 무시)</param>
        /// <param name="value">값 (null이면 빈 문자열로 저장)</param>
        public static void Add(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key), "키는 null 또는 빈 문자열이 될 수 없습니다.");

            lock (s_Lock)
                s_Context[key] = value ?? string.Empty;
        }

        /// <summary>
        /// 지정된 키의 컨텍스트 데이터를 제거합니다.
        /// 키가 없으면 아무 동작도 하지 않습니다.
        /// </summary>
        /// <param name="key">제거할 키</param>
        public static void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            lock (s_Lock)
                s_Context.Remove(key);
        }

        /// <summary>
        /// 모든 컨텍스트 데이터를 제거합니다.
        /// </summary>
        public static void Clear()
        {
            lock (s_Lock)
                s_Context.Clear();
        }

        /// <summary>
        /// 현재 컨텍스트 데이터의 복사본을 반환합니다.
        /// </summary>
        public static Dictionary<string, string> GetSnapshot()
        {
            lock (s_Lock)
                return new Dictionary<string, string>(s_Context);
        }

        /// <summary>
        /// IContextProvider 계약을 충족하는 내부 프로바이더 구현체를 반환합니다.
        /// ContextProviderRegistry에 등록할 때 사용합니다.
        /// </summary>
        public static IContextProvider AsProvider() => new StaticContextProvider();

        // ──────────────────────────────────────────────────────────────
        // 내부 프로바이더 구현
        // ──────────────────────────────────────────────────────────────

        private class StaticContextProvider : IContextProvider
        {
            public Dictionary<string, string> GetContext() => RekonContext.GetSnapshot();
        }
    }
}
