using UnityEngine;

namespace RekonOps.Rekon
{
    public static class RekonSettingsProvider
    {
        private static RekonSettings _instance;
        private const string DefaultSettingsPath = "RekonSettings";

        public static RekonSettings Settings
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<RekonSettings>(DefaultSettingsPath);
                    if (_instance == null)
                    {
                        _instance = ScriptableObject.CreateInstance<RekonSettings>();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Domain Reload OFF 환경에서 Play Mode 재진입 시 캐시를 초기화합니다.
        /// RekonBootstrap.ResetStaticState()에서 호출됩니다.
        /// </summary>
        public static void ResetCache()
        {
            _instance = null;
        }
    }
}
