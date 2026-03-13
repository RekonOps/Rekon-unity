using System;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 영상 프리셋 종류.
    /// </summary>
    public enum VideoPreset
    {
        /// <summary>권장 설정 (1280x720, 30fps, 2Mbps, 30초)</summary>
        Recommended = 0,
        /// <summary>고화질 (1920x1080, 60fps, 5Mbps, 60초)</summary>
        HighQuality = 1,
        /// <summary>경량 (854x480, 15fps, 1Mbps, 15초)</summary>
        Lightweight = 2,
        /// <summary>커스텀 (사용자 직접 설정)</summary>
        Custom = 3,
    }

    [CreateAssetMenu(fileName = "BugOneTouchSettings", menuName = "Bug-OneTouch/Settings")]
    public class BugOneTouchSettings : ScriptableObject
    {
        [Header("Hotkey")]
        [Tooltip("Key to trigger bug capture")]
        public KeyCode captureHotkey = KeyCode.B;

        [Tooltip("Require Ctrl (Windows) / Cmd (Mac)")]
        public bool hotkeyCtrlOrCmd = true;

        [Tooltip("Require Shift")]
        public bool hotkeyShift = true;

        [Tooltip("Require Alt (Windows) / Option (Mac)")]
        public bool hotkeyAlt = false;

        /// <summary>
        /// ScriptableObject Reset 시 플랫폼별 기본 핫키를 설정합니다.
        /// Mac: ⌘ + Shift + B, Windows: Ctrl + Shift + F12
        /// </summary>
        private void Reset()
        {
#if UNITY_EDITOR_OSX
            captureHotkey    = KeyCode.B;
            hotkeyCtrlOrCmd  = true;
            hotkeyShift      = true;
            hotkeyAlt        = false;
#else
            captureHotkey    = KeyCode.F12;
            hotkeyCtrlOrCmd  = true;
            hotkeyShift      = true;
            hotkeyAlt        = false;
#endif
        }

        [Header("Screenshot")]
        [Tooltip("Downscale factor (1 = original resolution)")]
        [Range(1, 4)]
        public int screenshotDownscale = 1;

        [Header("Video")]
        [Tooltip("영상 프리셋 (권장/고화질/경량/커스텀)")]
        public VideoPreset videoPreset = VideoPreset.Recommended;

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
        public int videoBufferSeconds = 30;

        [Tooltip("Target bitrate in Mbps")]
        [Range(1, 20)]
        public float videoBitrateMbps = 2f;

        [Header("Log")]
        [Tooltip("Maximum log lines in ring buffer")]
        [Range(100, 5000)]
        public int logBufferSize = 500;

        [Header("Debug")]
        [Tooltip("디버그 로그 활성화")]
        public bool debugLog = false;

        [Header("Report")]
        [Tooltip("버그 리포트 제목 접두어")]
        public string reportTitlePrefix = "Bug";

        [Tooltip("타임스탬프 형식 인덱스 (0: yyMMdd_HHmm, 1: yyyy-MM-dd HH:mm, 2: MMdd_HHmmss)")]
        [Range(0, 2)]
        public int timestampFormat = 0;

        [Tooltip("메타데이터에 Unity 버전 포함")]
        public bool collectUnityVersion = true;

        [Tooltip("메타데이터에 씬 이름 포함")]
        public bool collectSceneName = true;

        [Tooltip("메타데이터에 빌드 플랫폼 포함")]
        public bool collectPlatform = true;

        [Tooltip("메타데이터에 화면 해상도 포함")]
        public bool collectResolution = true;

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

        [Header("Team Identity")]
        [Tooltip("팀 식별자 (UUID). 같은 팀의 모든 멤버가 동일한 값을 사용합니다. 비어있으면 자동 생성됩니다.")]
        public string tenantId = "";

        [Tooltip("사용자 식별자 (UUID). 각 사용자별 고유 값입니다. 비어있으면 자동 생성됩니다.")]
        public string userId = "";

        // ─── 웹 대시보드 연동 ────────────────────────────────────────────────
        /// <summary>웹 대시보드 기본 URL</summary>
        public const string WEB_DASHBOARD_URL = "https://app.bug-onetouch.com";

        [Header("웹 연동")]
        [Tooltip("웹 대시보드와 연동 여부")]
        public bool isLinked = false;

        [Tooltip("연동된 워크스페이스 이름")]
        public string linkedWorkspaceName = "";

        [Header("Supabase")]
        // 아래 두 필드는 Web 프록시 도입으로 더 이상 사용되지 않습니다.
        // Unity 플러그인은 WEB_DASHBOARD_URL 상수를 통해 Web API를 직접 호출합니다.
        [Tooltip("Supabase 프로젝트 URL (예: https://xxxxx.supabase.co) — Web 프록시로 대체됨")]
        [System.Obsolete("Web 프록시로 대체됨. BugOneTouchSettings.WEB_DASHBOARD_URL 상수를 사용하세요.")]
        [UnityEngine.HideInInspector]
        public string supabaseUrl = "";

        [Tooltip("Supabase Anon Key — Web 프록시로 대체됨")]
        [System.Obsolete("Web 프록시로 대체됨. apikey 헤더는 Web 서버에서 관리합니다.")]
        [UnityEngine.HideInInspector]
        public string supabaseAnonKey = "";

        // webDashboardUrl 인스턴스 필드 제거됨 — WEB_DASHBOARD_URL 상수를 사용할 것

        [Tooltip("라이선스 키 (워크스페이스 설정 페이지에서 복사)")]
        public string licenseKey = "";

        [Header("Auth Broker")]
        [Tooltip("Auth Broker base URL")]
        public string authBrokerUrl = "https://your-project.supabase.co/functions/v1";

        [Header("Jira (Runtime)")]
        // 아래 Jira 필드들은 웹 대시보드 연동 방식으로 전환됨.
        // Jira 설정은 웹 대시보드(https://app.bug-onetouch.com) > 워크스페이스 설정에서 관리합니다.
        [Tooltip("Jira 사이트 기본 URL — 웹 대시보드에서 관리됨")]
        [System.Obsolete("Jira 연동은 웹 대시보드에서 관리됩니다. 이 필드는 더 이상 사용되지 않습니다.")]
        [UnityEngine.HideInInspector]
        public string jiraSiteUrl = "";

        [Tooltip("Jira 프로젝트 키 — 웹 대시보드에서 관리됨")]
        [System.Obsolete("Jira 연동은 웹 대시보드에서 관리됩니다. 이 필드는 더 이상 사용되지 않습니다.")]
        [UnityEngine.HideInInspector]
        public string jiraProjectKey = "";

        [Tooltip("기본 Jira 라벨 — 웹 대시보드에서 관리됨")]
        [System.Obsolete("Jira 연동은 웹 대시보드에서 관리됩니다. 이 필드는 더 이상 사용되지 않습니다.")]
        [UnityEngine.HideInInspector]
        public string[] defaultLabels = new string[0];

        // 첨부파일 크기 제한 캐시 (Jira 서버에서 조회한 값)
        /// <summary>Jira 서버에서 조회한 첨부파일 최대 크기(바이트). 0이면 미조회 상태.</summary>
        [HideInInspector] public long cachedAttachmentSizeLimitBytes = 0;

        /// <summary>
        /// tenantId와 userId가 비어있을 경우 UUID를 자동 생성합니다.
        /// </summary>
        public void EnsureIdentityIds()
        {
            bool changed = false;
            if (string.IsNullOrEmpty(tenantId))
            {
                tenantId = System.Guid.NewGuid().ToString();
                changed = true;
            }
            if (string.IsNullOrEmpty(userId))
            {
                userId = System.Guid.NewGuid().ToString();
                changed = true;
            }
#if UNITY_EDITOR
            if (changed)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

    }
}
