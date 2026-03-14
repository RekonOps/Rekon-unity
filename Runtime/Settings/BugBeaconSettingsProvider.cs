using UnityEngine;

namespace RekonOps.BugBeacon
{
    public static class BugBeaconSettingsProvider
    {
        private static BugBeaconSettings _instance;
        private const string DefaultSettingsPath = "BugBeaconSettings";

        public static BugBeaconSettings Settings
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<BugBeaconSettings>(DefaultSettingsPath);
                    if (_instance == null)
                    {
                        _instance = ScriptableObject.CreateInstance<BugBeaconSettings>();
                    }
                }
                return _instance;
            }
        }
    }
}
