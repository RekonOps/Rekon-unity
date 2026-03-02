using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace RekonOps.BugOneTouch.Editor
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
        // ─── 플랫폼별 메인 키 목록 ────────────────────────────────────────────────

        /// <summary>
        /// Mac 추천 메인 키 목록.
        /// Function 키(F1~F12)는 macOS 시스템 기능에 할당될 수 있으므로 제외.
        /// </summary>
        private static readonly KeyCode[] s_MacKeys =
        {
            // 알파벳 A-Z
            KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E,
            KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.I, KeyCode.J,
            KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N, KeyCode.O,
            KeyCode.P, KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T,
            KeyCode.U, KeyCode.V, KeyCode.W, KeyCode.X, KeyCode.Y, KeyCode.Z,
            // 숫자 0-9
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
            KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
            KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            // 특수
            KeyCode.BackQuote, KeyCode.Backslash, KeyCode.Slash,
            KeyCode.Minus, KeyCode.Equals, KeyCode.LeftBracket, KeyCode.RightBracket,
        };

        /// <summary>
        /// Windows 추천 메인 키 목록.
        /// Function 키(F1~F12)가 핫키로 자주 사용되므로 포함.
        /// </summary>
        private static readonly KeyCode[] s_WindowsKeys =
        {
            // Function 키
            KeyCode.F1,  KeyCode.F2,  KeyCode.F3,  KeyCode.F4,
            KeyCode.F5,  KeyCode.F6,  KeyCode.F7,  KeyCode.F8,
            KeyCode.F9,  KeyCode.F10, KeyCode.F11, KeyCode.F12,
            // 알파벳 A-Z
            KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E,
            KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.I, KeyCode.J,
            KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N, KeyCode.O,
            KeyCode.P, KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T,
            KeyCode.U, KeyCode.V, KeyCode.W, KeyCode.X, KeyCode.Y, KeyCode.Z,
            // 숫자 0-9
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
            KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
            KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            // 특수
            KeyCode.BackQuote, KeyCode.Minus, KeyCode.Equals,
            KeyCode.LeftBracket, KeyCode.RightBracket,
            KeyCode.Backslash, KeyCode.Slash,
            KeyCode.Pause, KeyCode.ScrollLock,
        };

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

        // JiraMetadataService - 프로젝트/이슈타입/필드 동적 조회
        private JiraMetadataService _metadataService;
        private bool _isLoadingProjects;
        private bool _isLoadingIssueTypes;
        private bool _isLoadingFields;
        private string _metadataError = "";

        private bool _isLoadingSpecialFields;   // myself/assignee/sprint/epic 로딩 상태
        private bool _showHiddenFields;          // 숨겨진 필드 foldout 상태

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

            // JiraMetadataService 초기화
            InitializeMetadataService();
        }

        /// <summary>
        /// JiraMetadataService를 초기화합니다.
        /// _settings가 null이 아닌 시점에서 호출해야 합니다.
        /// authBrokerUrl이 비어 있을 경우 폴백 URL을 사용합니다.
        /// </summary>
        private void InitializeMetadataService()
        {
            if (_settings == null) return;

            try
            {
                string brokerUrl = string.IsNullOrEmpty(_settings.authBrokerUrl)
                    ? "http://localhost"
                    : _settings.authBrokerUrl;

                var tokenStore = new SessionTokenStore();
                var reAuthHandler = new ReAuthHandler(tokenStore);
                var brokerClient = new AuthBrokerClient(brokerUrl, tokenStore);
                var tokenManager = new TokenRefreshManager(brokerClient, tokenStore, reAuthHandler);
                var apiClient = new JiraApiClient(tokenManager);
                _metadataService = new JiraMetadataService(apiClient);
            }
            catch (Exception ex)
            {
                _metadataError = $"메타데이터 서비스 초기화 실패: {ex.Message}";
                Debug.LogWarning($"[BugOneTouch] JiraMetadataService 초기화 실패: {ex.Message}");
            }
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

            // 메인 키 선택 (플랫폼별 추천 키 드롭다운)
            KeyCode[] keyList = isMac ? s_MacKeys : s_WindowsKeys;
            string[] displayNames = new string[keyList.Length];
            for (int ki = 0; ki < keyList.Length; ki++)
                displayNames[ki] = GetKeyDisplayName(keyList[ki]);

            int currentIndex = Array.IndexOf(keyList, (KeyCode)hotkey.intValue);
            // 목록에 없는 키(이전 설정값 등)면 첫 번째 항목으로 대체
            if (currentIndex < 0) currentIndex = 0;

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(
                new GUIContent("메인 키", "버그 캡처를 트리거하는 기본 키"),
                currentIndex,
                displayNames);
            if (EditorGUI.EndChangeCheck())
                hotkey.intValue = (int)keyList[newIndex];
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

        /// <summary>
        /// 드롭다운에 표시할 키 이름을 반환합니다.
        /// Alpha0~Alpha9는 숫자 문자로, 특수 키는 기호로 표시합니다.
        /// </summary>
        private static string GetKeyDisplayName(KeyCode key)
        {
            return key switch
            {
                KeyCode.BackQuote    => "`  (Backtick)",
                KeyCode.Backslash    => "\\  (Backslash)",
                KeyCode.Slash        => "/  (Slash)",
                KeyCode.Minus        => "-  (Minus)",
                KeyCode.Equals       => "=  (Equals)",
                KeyCode.LeftBracket  => "[  (Left Bracket)",
                KeyCode.RightBracket => "]  (Right Bracket)",
                KeyCode.Pause        => "Pause",
                KeyCode.ScrollLock   => "ScrollLock",
                >= KeyCode.Alpha0 and <= KeyCode.Alpha9
                    => ((int)key - (int)KeyCode.Alpha0).ToString(),
                _ => key.ToString(),
            };
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

            EditorGUILayout.Space(12f);

            // FFmpeg 설치 상태 섹션
            DrawFfmpegStatusSection();
        }

        // ─── FFmpeg 상태 섹션 ──────────────────────────────────────────────────────

        /// <summary>
        /// FFmpeg 설치 상태를 표시하는 UI 섹션을 그립니다.
        /// FfmpegHelper API를 통해 설치 여부와 버전 정보를 확인합니다.
        /// </summary>
        private void DrawFfmpegStatusSection()
        {
            DrawSectionHeader("FFmpeg 상태");

            bool isInstalled = FfmpegHelper.IsInstalled();

            if (isInstalled)
            {
                string versionInfo = FfmpegHelper.GetVersionInfo();
                EditorGUILayout.LabelField("상태", $"✓ 설치됨 ({versionInfo})");
            }
            else
            {
                EditorGUILayout.LabelField("상태", "✗ 미설치");

                EditorGUILayout.Space(4f);

                EditorGUILayout.HelpBox(
                    "FFmpeg가 없어도 플러그인은 정상 동작합니다.\n" +
                    "단, 영상 녹화가 MP4 대신 raw 프레임으로 저장됩니다.",
                    MessageType.Info);

                EditorGUILayout.Space(4f);

                EditorGUILayout.LabelField("설치 방법", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel("macOS:   brew install ffmpeg", EditorStyles.helpBox, GUILayout.Height(18f));
                EditorGUILayout.SelectableLabel("Windows: choco install ffmpeg", EditorStyles.helpBox, GUILayout.Height(18f));
                EditorGUILayout.SelectableLabel("또는     https://ffmpeg.org/download.html", EditorStyles.helpBox, GUILayout.Height(18f));
            }

            EditorGUILayout.Space(4f);

            if (GUILayout.Button("다시 확인", GUILayout.Width(80f)))
            {
                FfmpegHelper.ClearCache();
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
            // 1. 기존 JiraConnectionPanel (유지)
            _jiraPanel?.OnGUI();
            EditorGUILayout.Space(8f);

            // 2. 프로젝트 설정
            DrawSectionHeader("프로젝트 설정");
            DrawProjectSelector();
            EditorGUILayout.Space(4f);
            DrawIssueTypeSelector();
            EditorGUILayout.Space(8f);

            // 3. 필드 기본값 설정 (필드가 로드된 경우만)
            if (_settings.cachedFieldIds != null && _settings.cachedFieldIds.Length > 0)
            {
                DrawSectionHeader("필드 기본값 설정");
                EditorGUILayout.HelpBox(
                    "* 표시는 필수 필드입니다. summary와 description은 버그 리포트 폼에서 입력합니다.\n[H] 버튼으로 불필요한 필드를 숨길 수 있습니다.",
                    MessageType.Info);
                DrawFieldDefaults(true);   // 필수 필드
                EditorGUILayout.Space(4f);
                DrawFieldDefaults(false);  // 선택 필드 (숨기지 않은 것만)
                EditorGUILayout.Space(8f);

                // 숨겨진 필드 Foldout
                DrawHiddenFieldsFoldout();
            }

            // 4. 에러 메시지
            if (!string.IsNullOrEmpty(_metadataError))
                EditorGUILayout.HelpBox(_metadataError, MessageType.Error);

            // "기본 라벨" 섹션 제거됨 — labels 필드 안으로 통합
        }

        // ─── 프로젝트 선택 ────────────────────────────────────────────────────────

        private void DrawProjectSelector()
        {
            EditorGUILayout.BeginHorizontal();

            // 조회 버튼
            bool hasProjects = _settings.cachedProjectKeys?.Length > 0;
            string btnLabel = _isLoadingProjects ? "조회 중..." : (hasProjects ? "새로고침" : "프로젝트 조회");

            using (new EditorGUI.DisabledScope(_isLoadingProjects))
            {
                if (GUILayout.Button(btnLabel, GUILayout.Width(120f)))
                {
                    _metadataError = "";
                    FetchProjects();
                }
            }

            EditorGUILayout.EndHorizontal();

            // 드롭다운 (캐시가 있을 때)
            if (hasProjects)
            {
                // "PROJ - My Project" 형태
                string[] displayNames = new string[_settings.cachedProjectKeys.Length];
                for (int i = 0; i < displayNames.Length; i++)
                    displayNames[i] = $"{_settings.cachedProjectKeys[i]} - {_settings.cachedProjectNames[i]}";

                int currentIndex = Array.IndexOf(_settings.cachedProjectKeys, _settings.jiraProjectKey);
                if (currentIndex < 0) currentIndex = 0;

                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUILayout.Popup(
                    new GUIContent("프로젝트"), currentIndex, displayNames);
                if (EditorGUI.EndChangeCheck())
                {
                    _settings.jiraProjectKey = _settings.cachedProjectKeys[newIndex];
                    EditorUtility.SetDirty(_settings);
                    // 프로젝트 변경 → 이슈타입 새로 조회
                    FetchIssueTypes(_settings.cachedProjectKeys[newIndex]);
                    // 프로젝트 변경 → 특수 필드 새로 조회
                    FetchSpecialFields(_settings.cachedProjectKeys[newIndex]);
                }
            }
            else if (!_isLoadingProjects)
            {
                // 캐시 없으면 수동 입력 폴백
                var projectKeyProp = _serializedSettings.FindProperty("jiraProjectKey");
                EditorGUILayout.PropertyField(projectKeyProp, new GUIContent("프로젝트 키 (직접 입력)"));
            }
        }

        // ─── 이슈 타입 선택 ───────────────────────────────────────────────────────

        private void DrawIssueTypeSelector()
        {
            if (_settings.cachedIssueTypeNames?.Length > 0)
            {
                int currentIndex = Array.IndexOf(_settings.cachedIssueTypeNames, _settings.jiraDefaultIssueType);
                if (currentIndex < 0)
                {
                    // "Bug" 디폴트 찾기
                    currentIndex = Array.IndexOf(_settings.cachedIssueTypeNames, "Bug");
                    if (currentIndex < 0) currentIndex = 0;
                }

                string loadingText = _isLoadingIssueTypes ? " (로딩 중...)" : "";
                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUILayout.Popup(
                    new GUIContent("기본 이슈 타입" + loadingText),
                    currentIndex, _settings.cachedIssueTypeNames);
                if (EditorGUI.EndChangeCheck())
                {
                    _settings.jiraDefaultIssueType = _settings.cachedIssueTypeNames[newIndex];
                    if (newIndex < _settings.cachedIssueTypeIds.Length)
                        _settings.jiraSelectedIssueTypeId = _settings.cachedIssueTypeIds[newIndex];
                    EditorUtility.SetDirty(_settings);
                    // 이슈타입 변경 → 필드 새로 조회
                    FetchFields(_settings.jiraProjectKey, _settings.jiraSelectedIssueTypeId);
                }
            }
            else
            {
                // 폴백: 하드코딩
                var issueTypeProp = _serializedSettings.FindProperty("jiraDefaultIssueType");
                string[] fallbackTypes = { "Bug", "Task", "Story", "Epic", "Sub-task" };
                int currentIndex = Array.IndexOf(fallbackTypes, issueTypeProp.stringValue);
                if (currentIndex < 0) currentIndex = 0;
                int newIndex = EditorGUILayout.Popup(new GUIContent("기본 이슈 타입"), currentIndex, fallbackTypes);
                if (newIndex != currentIndex)
                {
                    issueTypeProp.stringValue = fallbackTypes[newIndex];
                    _serializedSettings.ApplyModifiedProperties();
                }
            }
        }

        // ─── 필드 기본값 ──────────────────────────────────────────────────────────

        private void DrawFieldDefaults(bool requiredOnly)
        {
            string groupLabel = requiredOnly ? "필수 필드" : "선택 필드";

            bool hasFields = false;
            for (int i = 0; i < _settings.cachedFieldIds.Length; i++)
            {
                if (_settings.cachedFieldRequired[i] != requiredOnly) continue;
                string fid = _settings.cachedFieldIds[i];
                if (fid == "summary" || fid == "description" || fid == "issuetype" || fid == "project") continue;
                // 선택 필드에서 숨긴 필드는 건너뜀
                if (!requiredOnly && _settings.IsFieldHidden(fid)) continue;
                hasFields = true;
                break;
            }
            if (!hasFields) return;

            EditorGUILayout.LabelField(groupLabel, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            for (int i = 0; i < _settings.cachedFieldIds.Length; i++)
            {
                if (_settings.cachedFieldRequired[i] != requiredOnly) continue;

                string fieldId   = _settings.cachedFieldIds[i];
                string fieldName = _settings.cachedFieldNames[i];

                if (fieldId == "summary" || fieldId == "description" || fieldId == "issuetype" || fieldId == "project") continue;
                // 선택 필드에서 숨긴 필드는 건너뜀
                if (!requiredOnly && _settings.IsFieldHidden(fieldId)) continue;

                string labelText = requiredOnly ? $"{fieldName} *" : fieldName;

                // labels 필드는 PropertyField로 별도 표시 (horizontal 바깥)
                if (fieldId == "labels")
                {
                    if (!requiredOnly)
                    {
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button("H", EditorStyles.miniButton, GUILayout.Width(20f)))
                        {
                            _settings.ToggleFieldHidden(fieldId);
                            EditorUtility.SetDirty(_settings);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    SerializedProperty defaultLabels = _serializedSettings.FindProperty("defaultLabels");
                    EditorGUILayout.PropertyField(defaultLabels,
                        new GUIContent(labelText, "이슈 생성 시 추가되는 라벨 목록"),
                        includeChildren: true);
                    continue;
                }

                EditorGUILayout.BeginHorizontal();

                // 선택 필드만 숨김 버튼 표시
                if (!requiredOnly)
                {
                    if (GUILayout.Button("H", EditorStyles.miniButton, GUILayout.Width(20f)))
                    {
                        _settings.ToggleFieldHidden(fieldId);
                        EditorUtility.SetDirty(_settings);
                    }
                }

                // 특수 필드별 커스텀 UI
                DrawFieldUI(fieldId, labelText);

                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;
        }

        // ─── 필드 UI ──────────────────────────────────────────────────────────────

        private void DrawFieldUI(string fieldId, string labelText)
        {
            switch (fieldId)
            {
                case "reporter":
                    // 현재 계정 자동 표시 (읽기전용)
                    string reporterName = string.IsNullOrEmpty(_settings.currentUserDisplayName)
                        ? "(조회 필요)" : _settings.currentUserDisplayName;
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.TextField(new GUIContent(labelText), reporterName);
                    EditorGUI.EndDisabledGroup();
                    break;

                case "assignee":
                    DrawCachedDropdown(labelText, fieldId,
                        _settings.cachedAssigneeIds, _settings.cachedAssigneeNames);
                    break;

                case "customfield_10020": // Sprint (일반적인 Sprint 필드 ID)
                case "sprint":
                    DrawCachedDropdown(labelText, fieldId,
                        _settings.cachedSprintIds, _settings.cachedSprintNames);
                    break;

                case "parent":
                    DrawCachedDropdown(labelText, fieldId,
                        _settings.cachedEpicKeys, _settings.cachedEpicNames);
                    break;

                case "issuelinks":
                    DrawCachedDropdown(labelText, fieldId,
                        _settings.cachedIssueKeys, _settings.cachedIssueNames);
                    break;

                default:
                    // allowedValues가 있으면 드롭다운, 없으면 텍스트
                    string[] allowed   = _settings.GetFieldAllowedValues(fieldId);
                    string   currentVal = _settings.GetFieldDefault(fieldId);

                    if (allowed.Length > 0)
                    {
                        int currentIdx = Array.IndexOf(allowed, currentVal);
                        if (currentIdx < 0) currentIdx = 0;
                        EditorGUI.BeginChangeCheck();
                        int newIdx = EditorGUILayout.Popup(new GUIContent(labelText), currentIdx, allowed);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _settings.SetFieldDefault(fieldId, allowed[newIdx]);
                            EditorUtility.SetDirty(_settings);
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        string newVal = EditorGUILayout.TextField(new GUIContent(labelText), currentVal);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _settings.SetFieldDefault(fieldId, newVal);
                            EditorUtility.SetDirty(_settings);
                        }
                    }
                    break;
            }
        }

        // ─── 캐시 드롭다운 헬퍼 ──────────────────────────────────────────────────

        private void DrawCachedDropdown(string label, string fieldId, string[] ids, string[] names)
        {
            if (names == null || names.Length == 0)
            {
                EditorGUILayout.TextField(new GUIContent(label), _settings.GetFieldDefault(fieldId));
                return;
            }

            // "(없음)" 옵션 추가
            string[] displayNames = new string[names.Length + 1];
            displayNames[0] = "(없음)";
            for (int i = 0; i < names.Length; i++)
                displayNames[i + 1] = names[i];

            string currentVal = _settings.GetFieldDefault(fieldId);
            int currentIdx = 0;
            if (!string.IsNullOrEmpty(currentVal) && ids != null)
            {
                int found = Array.IndexOf(ids, currentVal);
                if (found >= 0) currentIdx = found + 1;
            }

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(new GUIContent(label), currentIdx, displayNames);
            if (EditorGUI.EndChangeCheck())
            {
                string newVal = newIdx == 0 ? "" : (ids != null && newIdx - 1 < ids.Length ? ids[newIdx - 1] : "");
                _settings.SetFieldDefault(fieldId, newVal);
                EditorUtility.SetDirty(_settings);
            }
        }

        // ─── 숨겨진 필드 Foldout ─────────────────────────────────────────────────

        private void DrawHiddenFieldsFoldout()
        {
            int hiddenCount = _settings.hiddenFieldIds?.Length ?? 0;
            if (hiddenCount == 0) return;

            _showHiddenFields = EditorGUILayout.Foldout(_showHiddenFields,
                $"숨겨진 필드 ({hiddenCount}개)", true);

            if (!_showHiddenFields) return;

            EditorGUI.indentLevel++;
            for (int h = 0; h < _settings.hiddenFieldIds.Length; h++)
            {
                string hiddenId   = _settings.hiddenFieldIds[h];
                // cachedFieldNames에서 이름 찾기
                string hiddenName = hiddenId;
                for (int i = 0; i < _settings.cachedFieldIds.Length; i++)
                {
                    if (_settings.cachedFieldIds[i] == hiddenId)
                    {
                        hiddenName = _settings.cachedFieldNames[i];
                        break;
                    }
                }

                EditorGUILayout.BeginHorizontal();
                // 표시(Show) 버튼
                if (GUILayout.Button("S", EditorStyles.miniButton, GUILayout.Width(20f)))
                {
                    _settings.ToggleFieldHidden(hiddenId);
                    EditorUtility.SetDirty(_settings);
                }
                EditorGUILayout.LabelField(hiddenName, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        // ─── 비동기 메타데이터 조회 ───────────────────────────────────────────────

        private void FetchProjects()
        {
            if (_metadataService == null || _isLoadingProjects) return;
            _isLoadingProjects = true;
            _ = FetchProjectsAsync();
        }

        private async Task FetchProjectsAsync()
        {
            try
            {
                var projects = await _metadataService.GetProjectsAsync();
                EditorApplication.delayCall += () =>
                {
                    _settings.cachedProjectKeys  = new string[projects.Length];
                    _settings.cachedProjectNames = new string[projects.Length];
                    for (int i = 0; i < projects.Length; i++)
                    {
                        _settings.cachedProjectKeys[i]  = projects[i].key;
                        _settings.cachedProjectNames[i] = projects[i].name;
                    }
                    EditorUtility.SetDirty(_settings);
                    _isLoadingProjects = false;

                    // 현재 선택된 프로젝트가 목록에 있으면 이슈타입도 조회
                    if (!string.IsNullOrEmpty(_settings.jiraProjectKey))
                    {
                        FetchIssueTypes(_settings.jiraProjectKey);
                        // 특수 필드 조회 (myself, assignee, boards→sprints, epics, issues)
                        FetchSpecialFields(_settings.jiraProjectKey);
                    }
                    else if (projects.Length > 0)
                    {
                        _settings.jiraProjectKey = projects[0].key;
                        EditorUtility.SetDirty(_settings);
                        FetchIssueTypes(projects[0].key);
                        // 특수 필드 조회 (myself, assignee, boards→sprints, epics, issues)
                        FetchSpecialFields(projects[0].key);
                    }

                    Repaint();
                };
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                EditorApplication.delayCall += () =>
                {
                    _metadataError     = $"프로젝트 조회 실패: {ex.Message}";
                    _isLoadingProjects = false;
                    Repaint();
                };
            }
        }

        private void FetchIssueTypes(string projectKey)
        {
            if (_metadataService == null || _isLoadingIssueTypes || string.IsNullOrEmpty(projectKey)) return;
            _isLoadingIssueTypes = true;
            Debug.Log($"[BugOneTouch] 이슈 타입 조회 시작: projectKey={projectKey}");
            _ = FetchIssueTypesAsync(projectKey);
        }

        private async Task FetchIssueTypesAsync(string projectKey)
        {
            try
            {
                var issueTypes = await _metadataService.GetIssueTypesAsync(projectKey);
                Debug.Log($"[BugOneTouch] 이슈 타입 조회 API 응답 수신: {issueTypes.Length}개");
                EditorApplication.delayCall += () =>
                {
                    _settings.cachedIssueTypeIds   = new string[issueTypes.Length];
                    _settings.cachedIssueTypeNames = new string[issueTypes.Length];
                    for (int i = 0; i < issueTypes.Length; i++)
                    {
                        _settings.cachedIssueTypeIds[i]   = issueTypes[i].id;
                        _settings.cachedIssueTypeNames[i] = issueTypes[i].name;
                    }

                    // Bug 디폴트 선택
                    if (issueTypes.Length > 0)
                    {
                        int bugIdx = Array.IndexOf(_settings.cachedIssueTypeNames, "Bug");
                        if (bugIdx < 0 || bugIdx >= _settings.cachedIssueTypeNames.Length) bugIdx = 0;
                        _settings.jiraDefaultIssueType    = _settings.cachedIssueTypeNames[bugIdx];
                        _settings.jiraSelectedIssueTypeId = _settings.cachedIssueTypeIds[bugIdx];
                        Debug.Log($"[BugOneTouch] 이슈 타입 선택: name={_settings.jiraDefaultIssueType}, id={_settings.jiraSelectedIssueTypeId}");
                    }
                    else
                    {
                        _settings.jiraDefaultIssueType    = string.Empty;
                        _settings.jiraSelectedIssueTypeId = string.Empty;
                        Debug.LogWarning($"[BugOneTouch] 이슈 타입 목록이 비어 있어 필드 조회를 건너뜁니다.");
                    }
                    EditorUtility.SetDirty(_settings);
                    _isLoadingIssueTypes = false;

                    // 필드 조회 시작
                    if (!string.IsNullOrEmpty(_settings.jiraSelectedIssueTypeId))
                        FetchFields(projectKey, _settings.jiraSelectedIssueTypeId);

                    Repaint();
                };
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 이슈 타입 조회 예외 발생: {ex.GetType().Name}: {ex.Message}");
                EditorApplication.delayCall += () =>
                {
                    _metadataError       = $"이슈 타입 조회 실패: {ex.Message}";
                    _isLoadingIssueTypes = false;
                    Repaint();
                };
            }
        }

        private void FetchSpecialFields(string projectKey)
        {
            if (_metadataService == null || _isLoadingSpecialFields || string.IsNullOrEmpty(projectKey)) return;
            _isLoadingSpecialFields = true;
            _ = FetchSpecialFieldsAsync(projectKey);
        }

        private async Task FetchSpecialFieldsAsync(string projectKey)
        {
            try
            {
                // 병렬로 여러 API 호출
                var myselfTask     = _metadataService.GetMyselfAsync();
                var assignableTask = _metadataService.GetAssignableUsersAsync(projectKey);
                var boardsTask     = _metadataService.GetBoardsAsync(projectKey);
                var epicsTask      = _metadataService.SearchIssuesAsync(projectKey, "Epic");
                var issuesTask     = _metadataService.SearchIssuesAsync(projectKey);

                // myself
                try
                {
                    var myself = await myselfTask;
                    EditorApplication.delayCall += () =>
                    {
                        _settings.currentUserAccountId   = myself?.accountId   ?? "";
                        _settings.currentUserDisplayName = myself?.displayName  ?? "";
                        EditorUtility.SetDirty(_settings);
                        Repaint();
                    };
                }
                catch (Exception ex) { Debug.LogWarning($"[BugOneTouch] myself 조회 실패: {ex.Message}"); }

                // assignable users
                try
                {
                    var users = await assignableTask;
                    EditorApplication.delayCall += () =>
                    {
                        _settings.cachedAssigneeIds   = new string[users.Length];
                        _settings.cachedAssigneeNames = new string[users.Length];
                        for (int i = 0; i < users.Length; i++)
                        {
                            _settings.cachedAssigneeIds[i]   = users[i].accountId ?? "";
                            _settings.cachedAssigneeNames[i] = users[i].displayName ?? users[i].emailAddress ?? "";
                        }
                        EditorUtility.SetDirty(_settings);
                        Repaint();
                    };
                }
                catch (Exception ex) { Debug.LogWarning($"[BugOneTouch] assignable users 조회 실패: {ex.Message}"); }

                // boards → sprints
                try
                {
                    var boards = await boardsTask;
                    if (boards.Length > 0)
                    {
                        var sprints = await _metadataService.GetSprintsAsync(boards[0].id);
                        EditorApplication.delayCall += () =>
                        {
                            _settings.cachedSprintIds   = new string[sprints.Length];
                            _settings.cachedSprintNames = new string[sprints.Length];
                            for (int i = 0; i < sprints.Length; i++)
                            {
                                _settings.cachedSprintIds[i]   = sprints[i].id.ToString();
                                _settings.cachedSprintNames[i] = $"{sprints[i].name} ({sprints[i].state})";
                            }
                            EditorUtility.SetDirty(_settings);
                            Repaint();
                        };
                    }
                }
                catch (Exception ex) { Debug.LogWarning($"[BugOneTouch] sprints 조회 실패: {ex.Message}"); }

                // epics
                try
                {
                    var epics = await epicsTask;
                    EditorApplication.delayCall += () =>
                    {
                        _settings.cachedEpicKeys  = new string[epics.Length];
                        _settings.cachedEpicNames = new string[epics.Length];
                        for (int i = 0; i < epics.Length; i++)
                        {
                            _settings.cachedEpicKeys[i]  = epics[i].key ?? "";
                            _settings.cachedEpicNames[i] = $"{epics[i].key} - {epics[i].fields?.summary ?? ""}";
                        }
                        EditorUtility.SetDirty(_settings);
                        Repaint();
                    };
                }
                catch (Exception ex) { Debug.LogWarning($"[BugOneTouch] epics 조회 실패: {ex.Message}"); }

                // all issues (연결용)
                try
                {
                    var issues = await issuesTask;
                    EditorApplication.delayCall += () =>
                    {
                        _settings.cachedIssueKeys  = new string[issues.Length];
                        _settings.cachedIssueNames = new string[issues.Length];
                        for (int i = 0; i < issues.Length; i++)
                        {
                            _settings.cachedIssueKeys[i]  = issues[i].key ?? "";
                            _settings.cachedIssueNames[i] = $"{issues[i].key} - {issues[i].fields?.summary ?? ""}";
                        }
                        EditorUtility.SetDirty(_settings);
                        Repaint();
                    };
                }
                catch (Exception ex) { Debug.LogWarning($"[BugOneTouch] issues 조회 실패: {ex.Message}"); }
            }
            finally
            {
                EditorApplication.delayCall += () =>
                {
                    _isLoadingSpecialFields = false;
                    Repaint();
                };
            }
        }

        private void FetchFields(string projectKey, string issueTypeId)
        {
            if (_metadataService == null || _isLoadingFields) return;
            if (string.IsNullOrEmpty(projectKey) || string.IsNullOrEmpty(issueTypeId))
            {
                Debug.LogWarning($"[BugOneTouch] FetchFields 조기 반환: projectKey='{projectKey}', issueTypeId='{issueTypeId}'");
                return;
            }
            _isLoadingFields = true;
            Debug.Log($"[BugOneTouch] 필드 조회 시작: projectKey={projectKey}, issueTypeId={issueTypeId}");
            _ = FetchFieldsAsync(projectKey, issueTypeId);
        }

        private async Task FetchFieldsAsync(string projectKey, string issueTypeId)
        {
            try
            {
                var fields = await _metadataService.GetFieldsAsync(projectKey, issueTypeId);
                Debug.Log($"[BugOneTouch] 필드 조회 API 응답 수신: {fields.Length}개 필드");
                EditorApplication.delayCall += () =>
                {
                    if (fields.Length == 0)
                    {
                        Debug.LogWarning($"[BugOneTouch] 필드 조회 결과가 비어 있습니다. projectKey={projectKey}, issueTypeId={issueTypeId}");
                        _metadataError = $"필드 목록이 비어 있습니다. (프로젝트: {projectKey}, 이슈타입ID: {issueTypeId})\nJira API 응답을 확인하세요.";
                        _isLoadingFields = false;
                        Repaint();
                        return;
                    }

                    _settings.cachedFieldIds      = new string[fields.Length];
                    _settings.cachedFieldNames    = new string[fields.Length];
                    _settings.cachedFieldRequired = new bool[fields.Length];
                    _settings.cachedFieldTypes    = new string[fields.Length];

                    for (int i = 0; i < fields.Length; i++)
                    {
                        _settings.cachedFieldIds[i]      = fields[i].fieldId;
                        _settings.cachedFieldNames[i]    = fields[i].name;
                        _settings.cachedFieldRequired[i] = fields[i].required;
                        _settings.cachedFieldTypes[i]    = fields[i].schemaType;

                        // allowedValues 캐시
                        if (fields[i].allowedValues != null && fields[i].allowedValues.Length > 0)
                        {
                            string[] names = new string[fields[i].allowedValues.Length];
                            for (int j = 0; j < fields[i].allowedValues.Length; j++)
                                names[j] = !string.IsNullOrEmpty(fields[i].allowedValues[j].name)
                                    ? fields[i].allowedValues[j].name
                                    : fields[i].allowedValues[j].value ?? fields[i].allowedValues[j].id;
                            _settings.SetFieldAllowedValues(fields[i].fieldId, names);
                        }
                    }

                    EditorUtility.SetDirty(_settings);
                    _isLoadingFields = false;
                    Debug.Log($"[BugOneTouch] 필드 조회 완료: {fields.Length}개 필드 캐시됨");
                    Repaint();
                };
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 필드 조회 예외 발생: {ex.GetType().Name}: {ex.Message}");
                EditorApplication.delayCall += () =>
                {
                    _metadataError   = $"필드 조회 실패: {ex.Message}";
                    _isLoadingFields = false;
                    Repaint();
                };
            }
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
