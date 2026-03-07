using System;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
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
        [Tooltip("Enable video ring buffer")]
        public bool videoEnabled = true;

        [Tooltip("Video resolution width")]
        public int videoWidth = 1280;

        [Tooltip("Video resolution height")]
        public int videoHeight = 720;

        [Tooltip("Video frames per second")]
        [Range(15, 60)]
        public int videoFps = 15;

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

        [Header("Team Identity")]
        [Tooltip("팀 식별자 (UUID). 같은 팀의 모든 멤버가 동일한 값을 사용합니다. 비어있으면 자동 생성됩니다.")]
        public string tenantId = "";

        [Tooltip("사용자 식별자 (UUID). 각 사용자별 고유 값입니다. 비어있으면 자동 생성됩니다.")]
        public string userId = "";

        [Header("Supabase")]
        [Tooltip("Supabase 프로젝트 URL (예: https://xxxxx.supabase.co)")]
        public string supabaseUrl = "";

        [Tooltip("Supabase Anon Key")]
        public string supabaseAnonKey = "";

        [Tooltip("라이선스 키 (워크스페이스 설정 페이지에서 복사)")]
        public string licenseKey = "";

        [Header("Auth Broker")]
        [Tooltip("Auth Broker base URL")]
        public string authBrokerUrl = "https://your-project.supabase.co/functions/v1";

        [Header("Jira")]
        [Tooltip("Jira 사이트 기본 URL (예: https://yourcompany.atlassian.net)")]
        public string jiraSiteUrl = "";

        [Tooltip("Jira 프로젝트 키 (예: PROJ, BUG)")]
        public string jiraProjectKey = "";

        [Tooltip("기본 이슈 타입")]
        public string jiraDefaultIssueType = "Bug";

        [Tooltip("기본 우선순위")]
        public string jiraDefaultPriority = "Medium";

        [Tooltip("기본 Jira 라벨")]
        public string[] defaultLabels = new string[0];

        [Header("Jira 메타데이터 캐시")]
        [HideInInspector] public string[] cachedProjectKeys = new string[0];
        [HideInInspector] public string[] cachedProjectNames = new string[0];

        [HideInInspector] public string[] cachedIssueTypeIds = new string[0];
        [HideInInspector] public string[] cachedIssueTypeNames = new string[0];

        [HideInInspector] public string[] cachedFieldIds = new string[0];
        [HideInInspector] public string[] cachedFieldNames = new string[0];
        [HideInInspector] public bool[] cachedFieldRequired = new bool[0];
        [HideInInspector] public string[] cachedFieldTypes = new string[0];

        // 필드별 allowedValues 캐시 (fieldId별로 콤마 구분 문자열)
        // 예: fieldDefaultAllowedKeys = ["priority"], fieldDefaultAllowedValues = ["Highest,High,Medium,Low,Lowest"]
        [HideInInspector] public string[] cachedFieldAllowedKeys = new string[0];
        [HideInInspector] public string[] cachedFieldAllowedValues = new string[0];

        // 필드 기본값 (병렬 배열 = Dictionary 대체)
        [HideInInspector] public string[] fieldDefaultKeys = new string[0];
        [HideInInspector] public string[] fieldDefaultValues = new string[0];

        // 특수 필드 캐시 (담당자, 스프린트, 에픽, 이슈, 현재 사용자)
        [HideInInspector] public string[] cachedAssigneeIds = new string[0];
        [HideInInspector] public string[] cachedAssigneeNames = new string[0];
        [HideInInspector] public string[] cachedSprintIds = new string[0];
        [HideInInspector] public string[] cachedSprintNames = new string[0];
        [HideInInspector] public string[] cachedEpicKeys = new string[0];
        [HideInInspector] public string[] cachedEpicNames = new string[0];
        [HideInInspector] public string[] cachedIssueKeys = new string[0];
        [HideInInspector] public string[] cachedIssueNames = new string[0];
        [HideInInspector] public string currentUserAccountId = "";
        [HideInInspector] public string currentUserDisplayName = "";

        // 선택된 이슈타입 ID (이슈 타입 name → id 매핑용)
        [HideInInspector] public string jiraSelectedIssueTypeId = "";

        // 첨부파일 크기 제한 캐시 (Jira 서버에서 조회한 값)
        /// <summary>Jira 서버에서 조회한 첨부파일 최대 크기(바이트). 0이면 미조회 상태.</summary>
        [HideInInspector] public long cachedAttachmentSizeLimitBytes = 0;
        /// <summary>첨부파일 크기 제한을 마지막으로 조회한 일시 (ISO 8601 문자열).</summary>
        [HideInInspector] public string cachedAttachmentSizeLimitFetchedAt = "";

        [HideInInspector] public string[] hiddenFieldIds = new string[0];

        /// <summary>
        /// 특정 필드의 기본값을 조회합니다.
        /// </summary>
        public string GetFieldDefault(string fieldId)
        {
            if (fieldDefaultKeys == null) return "";
            for (int i = 0; i < fieldDefaultKeys.Length; i++)
            {
                if (fieldDefaultKeys[i] == fieldId)
                    return i < fieldDefaultValues.Length ? fieldDefaultValues[i] : "";
            }
            return "";
        }

        /// <summary>
        /// 특정 필드의 기본값을 설정합니다.
        /// </summary>
        public void SetFieldDefault(string fieldId, string value)
        {
            if (fieldDefaultKeys == null)
            {
                fieldDefaultKeys = new string[0];
                fieldDefaultValues = new string[0];
            }

            for (int i = 0; i < fieldDefaultKeys.Length; i++)
            {
                if (fieldDefaultKeys[i] == fieldId)
                {
                    if (i < fieldDefaultValues.Length)
                        fieldDefaultValues[i] = value;
                    return;
                }
            }

            // 새 항목 추가 (배열 확장)
            fieldDefaultKeys = AppendToArray(fieldDefaultKeys, fieldId);
            fieldDefaultValues = AppendToArray(fieldDefaultValues, value);
        }

        /// <summary>
        /// 특정 필드가 숨김 상태인지 확인합니다.
        /// </summary>
        public bool IsFieldHidden(string fieldId)
        {
            if (hiddenFieldIds == null) return false;
            for (int i = 0; i < hiddenFieldIds.Length; i++)
            {
                if (hiddenFieldIds[i] == fieldId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 특정 필드의 숨김 상태를 토글합니다.
        /// </summary>
        public void ToggleFieldHidden(string fieldId)
        {
            if (IsFieldHidden(fieldId))
            {
                // 제거
                var list = new System.Collections.Generic.List<string>(hiddenFieldIds);
                list.Remove(fieldId);
                hiddenFieldIds = list.ToArray();
            }
            else
            {
                // 추가
                hiddenFieldIds = AppendToArray(hiddenFieldIds, fieldId);
            }
        }

        /// <summary>
        /// 특정 필드의 allowedValues를 조회합니다 (콤마 구분 → 배열).
        /// </summary>
        public string[] GetFieldAllowedValues(string fieldId)
        {
            if (cachedFieldAllowedKeys == null) return new string[0];
            for (int i = 0; i < cachedFieldAllowedKeys.Length; i++)
            {
                if (cachedFieldAllowedKeys[i] == fieldId && i < cachedFieldAllowedValues.Length)
                {
                    string csv = cachedFieldAllowedValues[i];
                    if (string.IsNullOrEmpty(csv)) return new string[0];
                    return csv.Split(',');
                }
            }
            return new string[0];
        }

        /// <summary>
        /// 특정 필드의 allowedValues를 저장합니다 (배열 → 콤마 구분).
        /// </summary>
        public void SetFieldAllowedValues(string fieldId, string[] values)
        {
            if (cachedFieldAllowedKeys == null)
            {
                cachedFieldAllowedKeys = new string[0];
                cachedFieldAllowedValues = new string[0];
            }

            string csv = values != null ? string.Join(",", values) : "";

            for (int i = 0; i < cachedFieldAllowedKeys.Length; i++)
            {
                if (cachedFieldAllowedKeys[i] == fieldId)
                {
                    if (i < cachedFieldAllowedValues.Length)
                        cachedFieldAllowedValues[i] = csv;
                    return;
                }
            }

            cachedFieldAllowedKeys = AppendToArray(cachedFieldAllowedKeys, fieldId);
            cachedFieldAllowedValues = AppendToArray(cachedFieldAllowedValues, csv);
        }

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

        private static T[] AppendToArray<T>(T[] arr, T value)
        {
            if (arr == null) arr = new T[0];
            var newArr = new T[arr.Length + 1];
            Array.Copy(arr, newArr, arr.Length);
            newArr[arr.Length] = value;
            return newArr;
        }
    }
}
