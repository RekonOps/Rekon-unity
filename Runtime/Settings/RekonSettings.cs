using System;
using UnityEngine;

namespace RekonOps.Rekon
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

    [CreateAssetMenu(fileName = "RekonSettings", menuName = "Rekon/Settings")]
    public class RekonSettings : ScriptableObject
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
            // Mac: ⌘ + Shift + S
            screenshotHotkey           = KeyCode.S;
            screenshotHotkeyCtrlOrCmd  = true;
            screenshotHotkeyShift      = true;
            screenshotHotkeyAlt        = false;
#else
            captureHotkey    = KeyCode.F12;
            hotkeyCtrlOrCmd  = true;
            hotkeyShift      = true;
            hotkeyAlt        = false;
            // Windows: Ctrl + Shift + F11
            screenshotHotkey           = KeyCode.F11;
            screenshotHotkeyCtrlOrCmd  = true;
            screenshotHotkeyShift      = true;
            screenshotHotkeyAlt        = false;
#endif
        }

        [Header("Screenshot Hotkey")]
        [Tooltip("스크린샷 캡처 핫키")]
        public KeyCode screenshotHotkey = KeyCode.S;

        [Tooltip("스크린샷 핫키: Ctrl/Cmd 필요 여부")]
        public bool screenshotHotkeyCtrlOrCmd = true;

        [Tooltip("스크린샷 핫키: Shift 필요 여부")]
        public bool screenshotHotkeyShift = true;

        [Tooltip("스크린샷 핫키: Alt 필요 여부")]
        public bool screenshotHotkeyAlt = false;

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
        [Range(10, 180)]
        public int videoBufferSeconds = 60;

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
        /// <summary>웹 대시보드 기본 URL (Scripting Define Symbol로 전환)</summary>
        /// <remarks>
        /// REKON_LOCAL: http://localhost:3000 (로컬 개발)
        /// REKON_DEV: https://rekon.vercel.app (Vercel dev 배포)
        /// 없음: https://app.rekonops.dev (prod)
        /// </remarks>
#if REKON_LOCAL
        public const string WEB_DASHBOARD_URL = "http://localhost:3000";
#elif REKON_DEV
        public const string WEB_DASHBOARD_URL = "https://rekon.vercel.app";
#else
        public const string WEB_DASHBOARD_URL = "https://www.rekonops.dev";
#endif

        [Header("웹 연동")]
        [Tooltip("웹 대시보드와 연동 여부")]
        public bool isLinked = false;

        [Tooltip("연동된 워크스페이스 이름")]
        public string linkedWorkspaceName = "";

        [Header("Supabase")]
        // 아래 두 필드는 Web 프록시 도입으로 더 이상 사용되지 않습니다.
        // Unity 플러그인은 WEB_DASHBOARD_URL 상수를 통해 Web API를 직접 호출합니다.
        [Tooltip("Supabase 프로젝트 URL (예: https://xxxxx.supabase.co) — Web 프록시로 대체됨")]
        [System.Obsolete("Web 프록시로 대체됨. RekonSettings.WEB_DASHBOARD_URL 상수를 사용하세요.")]
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

        // ─── 플랜별 동적 제한값 (런타임 적용, 직렬화 제외) ──────────────────────
        // validate-license 응답 후 LicenseValidator가 채워줍니다.
        // ScriptableObject에는 저장되지 않으며, 에디터 세션 동안만 유지됩니다.

        /// <summary>플랜이 허용하는 최대 버퍼 시간(초). 라이선스 검증 후 갱신됩니다.</summary>
        [NonSerialized] public int maxAllowedBufferSeconds = 180;

        /// <summary>플랜이 허용하는 최대 스크린샷 개수. 라이선스 검증 후 갱신됩니다.</summary>
        [NonSerialized] public int maxAllowedScreenshotCount = 3;

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
