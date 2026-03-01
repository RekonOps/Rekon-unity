using UnityEngine;

namespace RekonOps.BugOneTouch
{
    [CreateAssetMenu(fileName = "BugOneTouchSettings", menuName = "Bug-OneTouch/Settings")]
    public class BugOneTouchSettings : ScriptableObject
    {
        [Header("Hotkey")]
        [Tooltip("Key to trigger bug capture")]
        public KeyCode captureHotkey = KeyCode.F12;

        [Header("Screenshot")]
        [Tooltip("Downscale factor (1 = original resolution)")]
        [Range(1, 4)]
        public int screenshotDownscale = 1;

        [Header("Video")]
        [Tooltip("Enable video ring buffer")]
        public bool videoEnabled = true;

        [Tooltip("Video resolution width")]
        public int videoWidth = 1280;

        [Tooltip("Video resolution height")]
        public int videoHeight = 720;

        [Tooltip("Video frames per second")]
        [Range(15, 60)]
        public int videoFps = 30;

        [Tooltip("Video buffer duration in seconds")]
        [Range(10, 120)]
        public int videoBufferSeconds = 60;

        [Tooltip("Target bitrate in Mbps")]
        [Range(2, 20)]
        public float videoBitrateMbps = 10f;

        [Header("Log")]
        [Tooltip("Maximum log lines in ring buffer")]
        [Range(100, 5000)]
        public int logBufferSize = 500;

        [Tooltip("Path to custom masking rules JSON")]
        public string maskingRulesPath = "";

        [Header("Crash Recovery")]
        [Tooltip("Log flush interval in seconds")]
        [Range(1, 30)]
        public float logFlushInterval = 5f;

        [Tooltip("State flush interval in seconds")]
        [Range(1, 60)]
        public float stateFlushInterval = 10f;

        [Tooltip("Video flush interval in seconds")]
        [Range(10, 120)]
        public float videoFlushInterval = 30f;

        [Tooltip("Maximum crash bundles to keep")]
        [Range(1, 50)]
        public int maxCrashBundles = 10;

        [Tooltip("Crash bundle retention days")]
        [Range(1, 365)]
        public int crashBundleRetentionDays = 30;

        [Header("Bundle")]
        [Tooltip("Maximum regular bundles")]
        [Range(10, 1000)]
        public int maxBundles = 200;

        [Tooltip("Maximum disk usage in MB")]
        [Range(500, 20000)]
        public int maxDiskUsageMB = 5120;

        [Header("Auth Broker")]
        [Tooltip("Auth Broker base URL")]
        public string authBrokerUrl = "https://your-project.supabase.co/functions/v1";

        [Header("Jira")]
        [Tooltip("Jira 사이트 기본 URL (예: https://yourcompany.atlassian.net)")]
        public string jiraSiteUrl = "";

        [Tooltip("Default Jira labels")]
        public string[] defaultLabels = new string[] { "bug-onetouch-unity", "unity" };
    }
}
