using System;
using UnityEditor;
using UnityEngine;

namespace GaoZombie.BugOneTouch.Editor
{
    /// <summary>
    /// Bug-OneTouch 설정 에디터 윈도우.
    /// Window/Bug-OneTouch/Settings 메뉴에서 열립니다.
    ///
    /// 탭 구성:
    ///   General        - 핫키, 스크린샷, 로그 설정
    ///   Video          - 영상 녹화 설정
    ///   Crash Recovery - 크래시 복구 관련 플러시 간격 및 보관 설정
    ///   Advanced       - 번들 한도, 디스크 용량, Auth Broker URL
    ///   Jira           - Jira OAuth 연결 패널
    ///
    /// SerializedObject 기반으로 변경 감지 및 Undo 지원.
    /// </summary>
    public class BugOneTouchSettingsWindow : EditorWindow
    {
        // ─── 탭 정의 ──────────────────────────────────────────────────────────────

        private static readonly string[] TabLabels =
        {
            "General",
            "Video",
            "Crash Recovery",
            "Advanced",
            "Jira",
        };

        private const int TabGeneral       = 0;
        private const int TabVideo         = 1;
        private const int TabCrashRecovery = 2;
        private const int TabAdvanced      = 3;
        private const int TabJira          = 4;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private int _selectedTab = 0;
        private BugOneTouchSettings _settings;
        private SerializedObject _serializedSettings;

        // JiraConnectionPanel은 Jira 탭에서 사용
        private JiraConnectionPanel _jiraPanel;

        // 스크롤 포지션 (각 탭별)
        private Vector2 _scrollPos;

        // ─── 메뉴 등록 ─────────────────────────────────────────────────────────────

        [MenuItem(BugOneTouchEditorInfo.MenuRoot + "/Settings")]
        public static void OpenWindow()
        {
            var window = GetWindow<BugOneTouchSettingsWindow>("Bug-OneTouch Settings");
            window.minSize = new Vector2(420f, 500f);
            window.Show();
        }

        // ─── 생명주기 ─────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            // BugOneTouchSettings 에셋 로드 또는 생성
            LoadOrCreateSettings();

            // JiraConnectionPanel 초기화
            _jiraPanel = new JiraConnectionPanel();
            _jiraPanel.Initialize(_settings);
        }

        private void OnDisable()
        {
            _jiraPanel?.Cleanup();
        }

        private void OnGUI()
        {
            if (_settings == null)
            {
                LoadOrCreateSettings();
                if (_settings == null)
                {
                    EditorGUILayout.HelpBox("BugOneTouchSettings 에셋을 찾을 수 없습니다.", MessageType.Error);
                    return;
                }
            }

            // SerializedObject 업데이트
            _serializedSettings.Update();

            DrawHeader();
            DrawTabs();
            DrawTabContent();
            DrawFooter();

            // 변경 사항 적용
            if (_serializedSettings.ApplyModifiedProperties())
            {
                // 변경 감지 시 에셋 더티 마킹
                EditorUtility.SetDirty(_settings);
            }
        }

        // ─── 헤더 ─────────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Bug-OneTouch 설정", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
            }

            // 에셋 경로 표시
            string assetPath = AssetDatabase.GetAssetPath(_settings);
            if (!string.IsNullOrEmpty(assetPath))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(assetPath, EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                }
            }

            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        // ─── 탭 버튼 ──────────────────────────────────────────────────────────────

        private void DrawTabs()
        {
            _selectedTab = GUILayout.Toolbar(_selectedTab, TabLabels);
            EditorGUILayout.Space(4f);
        }

        // ─── 탭 내용 ──────────────────────────────────────────────────────────────

        private void DrawTabContent()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case TabGeneral:
                    DrawGeneralTab();
                    break;
                case TabVideo:
                    DrawVideoTab();
                    break;
                case TabCrashRecovery:
                    DrawCrashRecoveryTab();
                    break;
                case TabAdvanced:
                    DrawAdvancedTab();
                    break;
                case TabJira:
                    DrawJiraTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── General 탭 ───────────────────────────────────────────────────────────

        private void DrawGeneralTab()
        {
            EditorGUILayout.LabelField("일반 설정", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            // 캡처 핫키
            DrawHotkeySection();

            EditorGUILayout.Space(8f);

            // 스크린샷 설정
            DrawSectionHeader("스크린샷");
            SerializedProperty downscale = _serializedSettings.FindProperty("screenshotDownscale");
            EditorGUILayout.IntSlider(
                downscale,
                1, 4,
                new GUIContent("다운스케일 배율", "1 = 원본 해상도, 2 = 절반, 4 = 1/4 크기"));

            EditorGUILayout.Space(8f);

            // 로그 설정
            DrawSectionHeader("로그");
            SerializedProperty logBufferSize = _serializedSettings.FindProperty("logBufferSize");
            EditorGUILayout.IntSlider(
                logBufferSize,
                100, 5000,
                new GUIContent("로그 버퍼 크기", "링 버퍼에 보관할 최대 로그 라인 수"));

            SerializedProperty maskingRulesPath = _serializedSettings.FindProperty("maskingRulesPath");
            EditorGUILayout.PropertyField(
                maskingRulesPath,
                new GUIContent("마스킹 규칙 경로", "민감 정보 마스킹 규칙 JSON 파일 경로 (비워두면 기본 규칙 사용)"));
        }

        private void DrawHotkeySection()
        {
            DrawSectionHeader("캡처 핫키");

            bool isMac = Application.platform == RuntimePlatform.OSXEditor;

            // 현재 조합 미리보기 표시
            string preview = BuildHotkeyPreview(isMac);
            EditorGUILayout.HelpBox($"현재 단축키: {preview}", MessageType.Info);

            // 수식키 토글 (플랫폼별 레이블)
            SerializedProperty ctrlCmd = _serializedSettings.FindProperty("hotkeyCtrlOrCmd");
            SerializedProperty shift   = _serializedSettings.FindProperty("hotkeyShift");
            SerializedProperty alt     = _serializedSettings.FindProperty("hotkeyAlt");
            SerializedProperty hotkey  = _serializedSettings.FindProperty("captureHotkey");

            EditorGUILayout.BeginHorizontal();

            // EditorGUI.PropertyField 방식으로 변경 사항이 SerializedObject를 통해 올바르게 추적되도록 수정
            EditorGUI.BeginChangeCheck();
            bool newCtrlCmd = EditorGUILayout.ToggleLeft(
                isMac ? "⌘ Cmd" : "Ctrl", ctrlCmd.boolValue, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
                ctrlCmd.boolValue = newCtrlCmd;

            EditorGUI.BeginChangeCheck();
            bool newShift = EditorGUILayout.ToggleLeft(
                isMac ? "⇧ Shift" : "Shift", shift.boolValue, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
                shift.boolValue = newShift;

            EditorGUI.BeginChangeCheck();
            bool newAlt = EditorGUILayout.ToggleLeft(
                isMac ? "⌥ Option" : "Alt", alt.boolValue, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
                alt.boolValue = newAlt;

            EditorGUILayout.EndHorizontal();

            // 메인 키 선택
            EditorGUILayout.PropertyField(hotkey, new GUIContent("메인 키", "버그 캡처를 트리거하는 기본 키"));
        }

        private string BuildHotkeyPreview(bool isMac)
        {
            SerializedProperty ctrlCmd = _serializedSettings.FindProperty("hotkeyCtrlOrCmd");
            SerializedProperty shift   = _serializedSettings.FindProperty("hotkeyShift");
            SerializedProperty alt     = _serializedSettings.FindProperty("hotkeyAlt");
            SerializedProperty hotkey  = _serializedSettings.FindProperty("captureHotkey");

            var parts = new System.Collections.Generic.List<string>();

            if (ctrlCmd.boolValue) parts.Add(isMac ? "⌘" : "Ctrl");
            if (shift.boolValue)   parts.Add(isMac ? "⇧" : "Shift");
            if (alt.boolValue)     parts.Add(isMac ? "⌥" : "Alt");
            // enumValueIndex 대신 intValue를 사용해야 실제 KeyCode 정수값으로 올바르게 변환됨
            // enumValueIndex는 enum 배열에서의 순서(0,1,2...)를 반환하므로 KeyCode 값과 다름
            parts.Add(((KeyCode)hotkey.intValue).ToString());

            return string.Join(" + ", parts);
        }

        // ─── Video 탭 ─────────────────────────────────────────────────────────────

        private void DrawVideoTab()
        {
            EditorGUILayout.LabelField("영상 설정", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            SerializedProperty videoEnabled = _serializedSettings.FindProperty("videoEnabled");
            EditorGUILayout.PropertyField(
                videoEnabled,
                new GUIContent("영상 캡처 활성화", "Play Mode에서 영상 링 버퍼 녹화 활성화"));

            EditorGUILayout.Space(4f);

            // 영상 비활성 시 나머지 필드 비활성화
            bool enabled = videoEnabled.boolValue;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                DrawSectionHeader("해상도");
                SerializedProperty videoWidth  = _serializedSettings.FindProperty("videoWidth");
                SerializedProperty videoHeight = _serializedSettings.FindProperty("videoHeight");

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("해상도 (W x H)");
                    videoWidth.intValue  = EditorGUILayout.IntField(videoWidth.intValue);
                    EditorGUILayout.LabelField("x", GUILayout.Width(14f));
                    videoHeight.intValue = EditorGUILayout.IntField(videoHeight.intValue);
                }

                // 해상도 값 클램프
                videoWidth.intValue  = Mathf.Clamp(videoWidth.intValue,  320, 7680);
                videoHeight.intValue = Mathf.Clamp(videoHeight.intValue, 240, 4320);

                EditorGUILayout.Space(8f);

                DrawSectionHeader("녹화 품질");
                SerializedProperty videoFps = _serializedSettings.FindProperty("videoFps");
                EditorGUILayout.IntSlider(
                    videoFps,
                    15, 60,
                    new GUIContent("FPS", "초당 프레임 수 (15~60)"));

                SerializedProperty bufferSeconds = _serializedSettings.FindProperty("videoBufferSeconds");
                EditorGUILayout.IntSlider(
                    bufferSeconds,
                    10, 120,
                    new GUIContent("버퍼 시간 (초)", "링 버퍼에 보관하는 영상 길이 (10~120초)"));

                SerializedProperty bitrate = _serializedSettings.FindProperty("videoBitrateMbps");
                EditorGUILayout.Slider(
                    bitrate,
                    2f, 20f,
                    new GUIContent("비트레이트 (Mbps)", "목표 영상 비트레이트 (2~20Mbps)"));
            }
        }

        // ─── Crash Recovery 탭 ────────────────────────────────────────────────────

        private void DrawCrashRecoveryTab()
        {
            EditorGUILayout.LabelField("크래시 복구 설정", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                "플러시 간격이 짧을수록 크래시 시 데이터 손실이 줄어들지만, 디스크 I/O가 증가합니다.",
                MessageType.Info);

            EditorGUILayout.Space(4f);

            DrawSectionHeader("플러시 간격");

            SerializedProperty logFlush = _serializedSettings.FindProperty("logFlushInterval");
            EditorGUILayout.Slider(
                logFlush,
                1f, 30f,
                new GUIContent("로그 플러시 간격 (초)", "로그 링 버퍼를 디스크에 저장하는 주기"));

            SerializedProperty stateFlush = _serializedSettings.FindProperty("stateFlushInterval");
            EditorGUILayout.Slider(
                stateFlush,
                1f, 60f,
                new GUIContent("상태 플러시 간격 (초)", "게임 상태 스냅샷을 디스크에 저장하는 주기"));

            SerializedProperty videoFlush = _serializedSettings.FindProperty("videoFlushInterval");
            EditorGUILayout.Slider(
                videoFlush,
                10f, 120f,
                new GUIContent("영상 플러시 간격 (초)", "영상 링 버퍼를 디스크에 저장하는 주기"));

            EditorGUILayout.Space(8f);

            DrawSectionHeader("보관 정책");

            SerializedProperty maxCrash = _serializedSettings.FindProperty("maxCrashBundles");
            EditorGUILayout.IntSlider(
                maxCrash,
                1, 50,
                new GUIContent("최대 크래시 번들 수", "디스크에 보관할 크래시 번들 최대 개수"));

            SerializedProperty retentionDays = _serializedSettings.FindProperty("crashBundleRetentionDays");
            EditorGUILayout.IntSlider(
                retentionDays,
                1, 365,
                new GUIContent("보관 일수", "크래시 번들 보관 최대 기간 (일)"));
        }

        // ─── Advanced 탭 ──────────────────────────────────────────────────────────

        private void DrawAdvancedTab()
        {
            EditorGUILayout.LabelField("고급 설정", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            EditorGUILayout.HelpBox(
                "잘못 변경 시 플러그인이 정상 동작하지 않을 수 있습니다.",
                MessageType.Warning);

            EditorGUILayout.Space(4f);

            DrawSectionHeader("번들 한도");

            SerializedProperty maxBundles = _serializedSettings.FindProperty("maxBundles");
            EditorGUILayout.IntSlider(
                maxBundles,
                10, 1000,
                new GUIContent("최대 번들 수", "디스크에 보관할 번들 최대 개수 (10~1000)"));

            SerializedProperty maxDisk = _serializedSettings.FindProperty("maxDiskUsageMB");
            EditorGUILayout.IntSlider(
                maxDisk,
                500, 20000,
                new GUIContent("최대 디스크 용량 (MB)", "번들 전체에 허용되는 최대 디스크 사용량 (500~20000MB)"));

            EditorGUILayout.Space(8f);

            DrawSectionHeader("Auth Broker");

            SerializedProperty brokerUrl = _serializedSettings.FindProperty("authBrokerUrl");
            EditorGUILayout.PropertyField(
                brokerUrl,
                new GUIContent("Auth Broker URL", "Supabase Edge Functions 기본 URL"));

            EditorGUILayout.Space(4f);

            if (GUILayout.Button("URL 형식 검증", GUILayout.Width(140f)))
            {
                ValidateAuthBrokerUrl(brokerUrl.stringValue);
            }
        }

        // ─── Jira 탭 ──────────────────────────────────────────────────────────────

        private void DrawJiraTab()
        {
            EditorGUILayout.LabelField("Jira 연결 설정", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            // JiraConnectionPanel이 Jira 탭 전체를 렌더링
            _jiraPanel?.OnGUI();

            EditorGUILayout.Space(8f);

            // 기본 라벨 설정
            DrawSectionHeader("기본 라벨");
            SerializedProperty defaultLabels = _serializedSettings.FindProperty("defaultLabels");
            EditorGUILayout.PropertyField(
                defaultLabels,
                new GUIContent("기본 Jira 라벨", "이슈 생성 시 자동으로 추가되는 라벨 목록"),
                includeChildren: true);
        }

        // ─── 푸터 ─────────────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            GUILayout.FlexibleSpace();
            DrawSeparator();
            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                // 되돌리기 버튼
                if (GUILayout.Button("되돌리기", GUILayout.Width(80f)))
                {
                    _serializedSettings.Update();
                    // Undo 히스토리에서 복원
                    Undo.RevertAllDownToGroup(Undo.GetCurrentGroup());
                }

                GUILayout.Space(8f);

                // 적용 버튼
                if (GUILayout.Button("적용", GUILayout.Width(80f)))
                {
                    ApplySettings();
                }
            }

            EditorGUILayout.Space(6f);
        }

        // ─── 헬퍼 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// BugOneTouchSettings 에셋을 로드하거나 찾을 수 없으면 생성합니다.
        /// </summary>
        private void LoadOrCreateSettings()
        {
            // Assets/Resources 에서 먼저 검색
            string[] guids = AssetDatabase.FindAssets("t:BugOneTouchSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<BugOneTouchSettings>(path);
            }

            if (_settings == null)
            {
                // Resources 폴더에 기본 에셋 생성
                const string ResourcesPath = "Assets/Resources";
                const string AssetPath     = ResourcesPath + "/BugOneTouchSettings.asset";

                if (!AssetDatabase.IsValidFolder(ResourcesPath))
                    AssetDatabase.CreateFolder("Assets", "Resources");

                _settings = CreateInstance<BugOneTouchSettings>();
                AssetDatabase.CreateAsset(_settings, AssetPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[BugOneTouch] BugOneTouchSettings 에셋 생성: " + AssetPath);
            }

            _serializedSettings = new SerializedObject(_settings);
        }

        /// <summary>
        /// 변경 사항을 적용하고 에셋을 저장합니다.
        /// </summary>
        private void ApplySettings()
        {
            _serializedSettings.ApplyModifiedProperties();
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[BugOneTouch] 설정 저장 완료");
        }

        /// <summary>
        /// Auth Broker URL 형식을 검증합니다.
        /// </summary>
        private static void ValidateAuthBrokerUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                EditorUtility.DisplayDialog("URL 검증", "Auth Broker URL이 비어 있습니다.", "확인");
                return;
            }

            bool valid = Uri.TryCreate(url, UriKind.Absolute, out Uri result)
                         && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);

            string message = valid
                ? $"URL 형식이 유효합니다.\n{url}"
                : $"올바르지 않은 URL 형식입니다.\n{url}\n\n예시: https://xxx.supabase.co/functions/v1";

            EditorUtility.DisplayDialog("URL 검증", message, "확인");
        }

        /// <summary>
        /// 섹션 구분 헤더를 그립니다.
        /// </summary>
        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        /// <summary>
        /// 수평 구분선을 그립니다.
        /// </summary>
        private static void DrawSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        }
    }
}
