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
    }
}
