using UnityEngine;

namespace RekonOps.BugOneTouch
{
    public static class BugOneTouchSettingsProvider
    {
        private static BugOneTouchSettings _instance;
        private const string DefaultSettingsPath = "BugOneTouchSettings";

        public static BugOneTouchSettings Settings
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<BugOneTouchSettings>(DefaultSettingsPath);
                    if (_instance == null)
                    {
                        _instance = ScriptableObject.CreateInstance<BugOneTouchSettings>();
                    }
                }
                return _instance;
            }
        }
    }
}
