using System;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace RekonOps.BugOneTouch.Editor
{
    /// <summary>
    /// Bug-OneTouch 설정 에디터 윈도우.
    /// Window/Bug-OneTouch/Settings 메뉴에서 열립니다.
    ///
    /// 단일 스크롤 윈도우 + 접이식(Foldout) 섹션 구조:
    ///   웹 연동       - Supabase URL/AnonKey/LicenseKey, 연결 상태, 웹 로그인
    ///   캡처 설정     - 영상 프리셋, 해상도/FPS/비트레이트/버퍼, 스크린샷, 로그
    ///   리포트 설정   - 제목 접두어, 타임스탬프 형식, 메타데이터 토글
    ///   단축키        - 캡처 핫키 (Mac/Windows 플랫폼별)
    ///   크래시 복구   - 플러시 간격, 보관 정책
    ///   고급          - 번들 한도, 디스크 용량, Auth Broker, 팀 ID, 디버그
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

        // ─── 영상 프리셋 레이블 ─────────────────────────────────────────────────

        private static readonly string[] s_PresetLabels = { "권장", "고화질", "경량", "커스텀" };

        private static readonly string[] s_TimestampLabels =
        {
            "yyMMdd_HHmm",
            "yyyy-MM-dd HH:mm",
            "MMdd_HHmmss",
        };

        // ─── Foldout 상태 ────────────────────────────────────────────────────────

        private bool _foldWeb = true;
        private bool _foldCapture = true;
        private bool _foldReport = true;
        private bool _foldHotkey = true;
        private bool _foldCrashRecovery = true;
        private bool _foldAdvanced = false;

        // ─── 내부 상태 ───────────────────────────────────────────────────────────

        private BugOneTouchSettings _settings;
        private SerializedObject _serializedSettings;

        // Supabase 웹 로그인 관련 상태
        private bool _isWebLoginInProgress;
        private string _webLoginStatus = "";
        private SupabaseAuthClient _editorSupabaseAuth;
        private SessionTokenStore _editorTokenStore;
        private CancellationTokenSource _loginCts;
        private string _cachedSupabaseUrl;

        // 스크롤 포지션
        private Vector2 _scrollPos;

        // ─── 메뉴 등록 ───────────────────────────────────────────────────────────

        [MenuItem(BugOneTouchEditorInfo.MenuRoot + "/Settings")]
        public static void OpenWindow()
        {
            var window = GetWindow<BugOneTouchSettingsWindow>("Bug-OneTouch Settings");
            window.minSize = new Vector2(420f, 500f);
            window.Show();
        }

        // ─── 생명주기 ───────────────────────────────────────────────────────────

        private void OnEnable()
        {
            LoadOrCreateSettings();
        }

        private void OnDisable()
        {
            _loginCts?.Cancel();
            _loginCts?.Dispose();
            _loginCts = null;
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

            // 전체 스크롤뷰
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawWebSection();
            DrawCaptureSection();
            DrawReportSection();
            DrawHotkeySection();
            DrawCrashRecoverySection();
            DrawAdvancedSection();

            EditorGUILayout.EndScrollView();

            DrawFooter();

            // 변경 사항 적용
            _serializedSettings.ApplyModifiedProperties();
        }

        // ─── 헤더 ───────────────────────────────────────────────────────────────

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

        // ─── 웹 연동 섹션 ───────────────────────────────────────────────────────

        private void DrawWebSection()
        {
            EditorGUILayout.Space(4f);
            _foldWeb = EditorGUILayout.Foldout(_foldWeb, "웹 연동", true, EditorStyles.foldoutHeader);
            if (!_foldWeb) return;

            EditorGUI.indentLevel++;

            // Supabase URL
            SerializedProperty supabaseUrlProp = _serializedSettings.FindProperty("supabaseUrl");
            EditorGUILayout.PropertyField(
                supabaseUrlProp,
                new GUIContent("API URL", "Supabase 프로젝트 URL (예: https://xxxxx.supabase.co)"));

            SerializedProperty supabaseAnonKeyProp = _serializedSettings.FindProperty("supabaseAnonKey");
            EditorGUILayout.PropertyField(
                supabaseAnonKeyProp,
                new GUIContent("Anon Key", "Supabase Anon Key"));

            // 라이선스 키 (마스킹)
            SerializedProperty licenseKeyProp = _serializedSettings.FindProperty("licenseKey");
            EditorGUI.BeginChangeCheck();
            string maskedValue = EditorGUILayout.PasswordField(
                new GUIContent("라이선스 키", "워크스페이스 설정 페이지에서 복사한 라이선스 키"),
                licenseKeyProp.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                licenseKeyProp.stringValue = maskedValue;
            }

            EditorGUILayout.Space(4f);

            // 연결 상태 및 웹 로그인
            DrawWebLoginSubSection();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        private void DrawWebLoginSubSection()
        {
            bool hasSupabaseUrl = !string.IsNullOrEmpty(_settings.supabaseUrl)
                && !string.IsNullOrEmpty(_settings.supabaseAnonKey);

            if (!hasSupabaseUrl)
            {
                EditorGUILayout.HelpBox(
                    "API URL과 Anon Key를 먼저 설정하세요.",
                    MessageType.Info);
                return;
            }

            // 로그인 상태 표시
            EnsureEditorSupabaseAuth();
            bool isLoggedIn = _editorTokenStore != null && !_editorTokenStore.IsSupabaseExpired();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("연결 상태");
            if (isLoggedIn)
            {
                var origColor = GUI.contentColor;
                GUI.contentColor = new Color(0.2f, 0.8f, 0.3f);
                EditorGUILayout.LabelField("● 연결됨");
                GUI.contentColor = origColor;
            }
            else
            {
                var origColor = GUI.contentColor;
                GUI.contentColor = new Color(0.9f, 0.3f, 0.3f);
                EditorGUILayout.LabelField("● 미연결");
                GUI.contentColor = origColor;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();

            // 웹 로그인 버튼
            using (new EditorGUI.DisabledScope(_isWebLoginInProgress))
            {
                string loginLabel = _isWebLoginInProgress ? "로그인 중..." : "웹 로그인";
                if (GUILayout.Button(loginLabel, GUILayout.Width(120f), GUILayout.Height(28f)))
                {
                    StartWebLogin();
                }
            }

            // 로그아웃 버튼
            using (new EditorGUI.DisabledScope(!isLoggedIn || _isWebLoginInProgress))
            {
                if (GUILayout.Button("로그아웃", GUILayout.Width(80f), GUILayout.Height(28f)))
                {
                    _editorTokenStore?.ClearSupabase();
                    _webLoginStatus = "로그아웃 완료";
                    Debug.Log("[BugOneTouch] Supabase 에디터 로그아웃 완료");
                    Repaint();
                }
            }

            EditorGUILayout.EndHorizontal();

            // 상태 메시지 표시
            if (!string.IsNullOrEmpty(_webLoginStatus))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(_webLoginStatus, MessageType.Info);
            }
        }

        // ─── 캡처 설정 섹션 ─────────────────────────────────────────────────────

        private void DrawCaptureSection()
        {
            EditorGUILayout.Space(4f);
            _foldCapture = EditorGUILayout.Foldout(_foldCapture, "캡처 설정", true, EditorStyles.foldoutHeader);
            if (!_foldCapture) return;

            EditorGUI.indentLevel++;

            // 영상 프리셋
            DrawVideoPresetSubSection();

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

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        private void DrawVideoPresetSubSection()
        {
            DrawSectionHeader("영상");

            SerializedProperty videoEnabled = _serializedSettings.FindProperty("videoEnabled");
            EditorGUILayout.PropertyField(
                videoEnabled,
                new GUIContent("영상 캡처 활성화", "Play Mode에서 영상 링 버퍼 녹화 활성화"));

            bool enabled = videoEnabled.boolValue;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                // 프리셋 드롭다운
                SerializedProperty presetProp = _serializedSettings.FindProperty("videoPreset");
                EditorGUI.BeginChangeCheck();
                int newPreset = EditorGUILayout.Popup(
                    new GUIContent("영상 프리셋", "프리셋 선택 시 해상도/FPS/비트레이트/버퍼가 자동 설정됩니다"),
                    presetProp.intValue,
                    s_PresetLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    presetProp.intValue = newPreset;
                    ApplyVideoPreset((VideoPreset)newPreset);
                }

                EditorGUILayout.Space(4f);

                // 커스텀이 아닐 때는 읽기 전용으로 표시, 커스텀일 때만 편집 가능
                bool isCustom = (VideoPreset)presetProp.intValue == VideoPreset.Custom;

                using (new EditorGUI.DisabledScope(!isCustom))
                {
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

                    SerializedProperty videoFps = _serializedSettings.FindProperty("videoFps");
                    EditorGUILayout.IntSlider(
                        videoFps,
                        15, 60,
                        new GUIContent("FPS", "초당 프레임 수 (15~60)"));

                    SerializedProperty bitrate = _serializedSettings.FindProperty("videoBitrateMbps");
                    EditorGUILayout.Slider(
                        bitrate,
                        1f, 20f,
                        new GUIContent("비트레이트 (Mbps)", "목표 영상 비트레이트 (1~20Mbps)"));

                    SerializedProperty bufferSeconds = _serializedSettings.FindProperty("videoBufferSeconds");
                    EditorGUILayout.IntSlider(
                        bufferSeconds,
                        10, 120,
                        new GUIContent("버퍼 시간 (초)", "링 버퍼에 보관하는 영상 길이 (10~120초)"));
                }
            }

            EditorGUILayout.Space(8f);

            // FFmpeg 상태
            DrawFfmpegStatusSubSection();
        }

        /// <summary>
        /// 영상 프리셋 값을 Settings 필드에 적용합니다.
        /// </summary>
        private void ApplyVideoPreset(VideoPreset preset)
        {
            SerializedProperty w   = _serializedSettings.FindProperty("videoWidth");
            SerializedProperty h   = _serializedSettings.FindProperty("videoHeight");
            SerializedProperty fps = _serializedSettings.FindProperty("videoFps");
            SerializedProperty br  = _serializedSettings.FindProperty("videoBitrateMbps");
            SerializedProperty buf = _serializedSettings.FindProperty("videoBufferSeconds");

            switch (preset)
            {
                case VideoPreset.Recommended:
                    w.intValue   = 1280;
                    h.intValue   = 720;
                    fps.intValue = 30;
                    br.floatValue = 2f;
                    buf.intValue = 30;
                    break;
                case VideoPreset.HighQuality:
                    w.intValue   = 1920;
                    h.intValue   = 1080;
                    fps.intValue = 60;
                    br.floatValue = 5f;
                    buf.intValue = 60;
                    break;
                case VideoPreset.Lightweight:
                    w.intValue   = 854;
                    h.intValue   = 480;
                    fps.intValue = 15;
                    br.floatValue = 1f;
                    buf.intValue = 15;
                    break;
                case VideoPreset.Custom:
                    // 현재 값 유지
                    break;
            }
        }

        /// <summary>
        /// FFmpeg 설치 상태를 표시하는 UI 서브 섹션.
        /// </summary>
        private void DrawFfmpegStatusSubSection()
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

        // ─── 리포트 설정 섹션 ───────────────────────────────────────────────────

        private void DrawReportSection()
        {
            EditorGUILayout.Space(4f);
            _foldReport = EditorGUILayout.Foldout(_foldReport, "리포트 설정", true, EditorStyles.foldoutHeader);
            if (!_foldReport) return;

            EditorGUI.indentLevel++;

            // 접두어
            SerializedProperty prefixProp = _serializedSettings.FindProperty("reportTitlePrefix");
            EditorGUILayout.PropertyField(
                prefixProp,
                new GUIContent("제목 접두어", "버그 리포트 제목에 붙는 접두어 (예: Bug, Issue)"));

            // 타임스탬프 형식
            SerializedProperty tsFmtProp = _serializedSettings.FindProperty("timestampFormat");
            tsFmtProp.intValue = EditorGUILayout.Popup(
                new GUIContent("타임스탬프 형식", "리포트 제목에 사용할 시간 형식"),
                tsFmtProp.intValue,
                s_TimestampLabels);

            EditorGUILayout.Space(8f);

            // 메타데이터 수집 토글
            DrawSectionHeader("메타데이터 수집");

            SerializedProperty unityVer = _serializedSettings.FindProperty("collectUnityVersion");
            EditorGUILayout.PropertyField(unityVer, new GUIContent("Unity 버전"));

            SerializedProperty sceneName = _serializedSettings.FindProperty("collectSceneName");
            EditorGUILayout.PropertyField(sceneName, new GUIContent("씬 이름"));

            SerializedProperty platform = _serializedSettings.FindProperty("collectPlatform");
            EditorGUILayout.PropertyField(platform, new GUIContent("플랫폼"));

            SerializedProperty resolution = _serializedSettings.FindProperty("collectResolution");
            EditorGUILayout.PropertyField(resolution, new GUIContent("해상도"));

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        // ─── 단축키 섹션 ────────────────────────────────────────────────────────

        private void DrawHotkeySection()
        {
            EditorGUILayout.Space(4f);
            _foldHotkey = EditorGUILayout.Foldout(_foldHotkey, "단축키", true, EditorStyles.foldoutHeader);
            if (!_foldHotkey) return;

            EditorGUI.indentLevel++;

            bool isMac = Application.platform == RuntimePlatform.OSXEditor;

            // 현재 조합 미리보기
            string preview = BuildHotkeyPreview(isMac);
            EditorGUILayout.HelpBox($"현재 단축키: {preview}", MessageType.Info);

            // 수식키 토글
            SerializedProperty ctrlCmd = _serializedSettings.FindProperty("hotkeyCtrlOrCmd");
            SerializedProperty shift   = _serializedSettings.FindProperty("hotkeyShift");
            SerializedProperty alt     = _serializedSettings.FindProperty("hotkeyAlt");
            SerializedProperty hotkey  = _serializedSettings.FindProperty("captureHotkey");

            EditorGUILayout.BeginHorizontal();

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
            if (currentIndex < 0) currentIndex = 0;

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(
                new GUIContent("메인 키", "버그 캡처를 트리거하는 기본 키"),
                currentIndex,
                displayNames);
            if (EditorGUI.EndChangeCheck())
                hotkey.intValue = (int)keyList[newIndex];

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
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
            parts.Add(((KeyCode)hotkey.intValue).ToString());

            return string.Join(" + ", parts);
        }

        /// <summary>
        /// 드롭다운에 표시할 키 이름을 반환합니다.
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

        // ─── 크래시 복구 섹션 ───────────────────────────────────────────────────

        private void DrawCrashRecoverySection()
        {
            EditorGUILayout.Space(4f);
            _foldCrashRecovery = EditorGUILayout.Foldout(_foldCrashRecovery, "크래시 복구", true, EditorStyles.foldoutHeader);
            if (!_foldCrashRecovery) return;

            EditorGUI.indentLevel++;

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

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        // ─── 고급 섹션 ──────────────────────────────────────────────────────────

        private void DrawAdvancedSection()
        {
            EditorGUILayout.Space(4f);
            _foldAdvanced = EditorGUILayout.Foldout(_foldAdvanced, "고급", true, EditorStyles.foldoutHeader);
            if (!_foldAdvanced) return;

            EditorGUI.indentLevel++;

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

            EditorGUILayout.Space(8f);

            // 팀 ID 관리
            DrawTeamIdentitySubSection();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        private void DrawTeamIdentitySubSection()
        {
            DrawSectionHeader("팀 ID 관리");

            EditorGUILayout.HelpBox("같은 팀의 멤버는 동일한 팀 ID를 사용하세요.", MessageType.Info);
            EditorGUILayout.Space(4f);

            SerializedProperty tenantIdProp = _serializedSettings.FindProperty("tenantId");
            SerializedProperty userIdProp   = _serializedSettings.FindProperty("userId");

            // ── 팀 ID (tenantId) ──
            EditorGUILayout.LabelField("팀 ID (tenantId)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string newTenantId = EditorGUILayout.TextField(tenantIdProp.stringValue);
                if (EditorGUI.EndChangeCheck())
                {
                    if (string.IsNullOrEmpty(newTenantId) || System.Guid.TryParse(newTenantId, out _))
                    {
                        tenantIdProp.stringValue = newTenantId;
                        _serializedSettings.ApplyModifiedProperties();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("형식 오류", "팀 ID는 UUID 형식이어야 합니다.\n예: 550e8400-e29b-41d4-a716-446655440000", "확인");
                    }
                }

                if (GUILayout.Button("복사", GUILayout.Width(50f)))
                {
                    GUIUtility.systemCopyBuffer = tenantIdProp.stringValue;
                }

                if (GUILayout.Button("새로 생성", GUILayout.Width(80f)))
                {
                    tenantIdProp.stringValue = System.Guid.NewGuid().ToString();
                    _serializedSettings.ApplyModifiedProperties();
                }
            }

            EditorGUILayout.Space(6f);

            // ── 사용자 ID (userId) ──
            EditorGUILayout.LabelField("사용자 ID (userId)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string newUserId = EditorGUILayout.TextField(userIdProp.stringValue);
                if (EditorGUI.EndChangeCheck())
                {
                    if (string.IsNullOrEmpty(newUserId) || System.Guid.TryParse(newUserId, out _))
                    {
                        userIdProp.stringValue = newUserId;
                        _serializedSettings.ApplyModifiedProperties();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("형식 오류", "사용자 ID는 UUID 형식이어야 합니다.\n예: 550e8400-e29b-41d4-a716-446655440000", "확인");
                    }
                }

                if (GUILayout.Button("복사", GUILayout.Width(50f)))
                {
                    GUIUtility.systemCopyBuffer = userIdProp.stringValue;
                }

                if (GUILayout.Button("새로 생성", GUILayout.Width(80f)))
                {
                    userIdProp.stringValue = System.Guid.NewGuid().ToString();
                    _serializedSettings.ApplyModifiedProperties();
                }
            }

            EditorGUILayout.Space(4f);

            if (GUILayout.Button("빈 ID 자동 생성 (EnsureIdentityIds)", GUILayout.Height(24f)))
            {
                _settings.EnsureIdentityIds();
                _serializedSettings.Update();
            }
        }

        // ─── 푸터 ───────────────────────────────────────────────────────────────

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

        // ─── Supabase 인증 헬퍼 ─────────────────────────────────────────────────

        /// <summary>
        /// 에디터용 SupabaseAuthClient 인스턴스를 생성합니다 (지연 초기화).
        /// </summary>
        private void EnsureEditorSupabaseAuth()
        {
            if (_editorTokenStore == null)
                _editorTokenStore = new SessionTokenStore();

            // settings의 URL이 변경되면 클라이언트 재생성
            if (_editorSupabaseAuth != null && _settings.supabaseUrl != _cachedSupabaseUrl)
            {
                Debug.Log("[BugOneTouch] Supabase URL 변경 감지. 클라이언트를 재생성합니다.");
                _editorSupabaseAuth = null;
            }

            if (_editorSupabaseAuth == null
                && !string.IsNullOrEmpty(_settings.supabaseUrl)
                && !string.IsNullOrEmpty(_settings.supabaseAnonKey))
            {
                try
                {
                    _editorSupabaseAuth = new SupabaseAuthClient(
                        _settings.supabaseUrl, _settings.supabaseAnonKey, _editorTokenStore);
                    _cachedSupabaseUrl = _settings.supabaseUrl;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BugOneTouch] SupabaseAuthClient 초기화 실패: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 웹 브라우저를 통한 Supabase 로그인을 시작합니다.
        /// </summary>
        private async void StartWebLogin()
        {
            if (_isWebLoginInProgress) return;

            EnsureEditorSupabaseAuth();
            if (_editorSupabaseAuth == null)
            {
                _webLoginStatus = "SupabaseAuthClient 초기화에 실패했습니다. URL과 Anon Key를 확인하세요.";
                Repaint();
                return;
            }

            // 이전 로그인 시도 취소 및 새 CancellationTokenSource 생성
            _loginCts?.Cancel();
            _loginCts?.Dispose();
            _loginCts = new CancellationTokenSource();
            var ct = _loginCts.Token;

            _isWebLoginInProgress = true;
            _webLoginStatus = "브라우저에서 로그인을 완료해주세요...";
            Repaint();

            try
            {
                string deviceId = SystemInfo.deviceUniqueIdentifier;
                var result = await _editorSupabaseAuth.StartWebLoginAsync(deviceId, ct);
                _webLoginStatus = $"인증 완료! 워크스페이스: {result.WorkspaceName}";
                Debug.Log($"[BugOneTouch] 에디터 Supabase 웹 로그인 성공: {result.WorkspaceName}");
            }
            catch (OperationCanceledException)
            {
                _webLoginStatus = "로그인이 취소되었습니다.";
                Debug.Log("[BugOneTouch] 에디터 Supabase 웹 로그인 취소됨");
            }
            catch (TimeoutException)
            {
                _webLoginStatus = "로그인 시간이 초과되었습니다. 다시 시도해주세요.";
            }
            catch (Exception ex)
            {
                _webLoginStatus = $"로그인 실패: {ex.Message}";
                Debug.LogError($"[BugOneTouch] 에디터 Supabase 웹 로그인 실패: {ex.Message}");
            }
            finally
            {
                _isWebLoginInProgress = false;
                Repaint();
            }
        }

        // ─── 헬퍼 메서드 ────────────────────────────────────────────────────────

        /// <summary>
        /// BugOneTouchSettings 에셋을 로드하거나 찾을 수 없으면 생성합니다.
        /// </summary>
        private void LoadOrCreateSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:BugOneTouchSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<BugOneTouchSettings>(path);
            }

            if (_settings == null)
            {
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
