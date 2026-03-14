using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GaoZombie.BugBeacon.Editor
{
    /// <summary>
    /// BugBeacon 설정 에디터 윈도우.
    /// Window/BugBeacon/Settings 메뉴에서 열립니다.
    ///
    /// 단일 스크롤 윈도우 + 접이식(Foldout) 섹션 구조:
    ///   웹 연동       - 연동 상태 표시, 웹 대시보드 연동 버튼
    ///   캡처 설정     - 영상 프리셋, 해상도/FPS/비트레이트/버퍼, 스크린샷, 로그
    ///   리포트 설정   - 제목 접두어, 타임스탬프 형식, 메타데이터 토글
    ///   단축키        - 캡처 핫키 (Mac/Windows 플랫폼별)
    ///   크래시 복구   - 플러시 간격, 보관 정책
    ///   고급          - 디버그 로그, 팀 ID (디버그 시에만)
    ///
    /// SerializedObject 기반으로 변경 감지 및 Undo 지원.
    /// </summary>
    public class BugBeaconSettingsWindow : EditorWindow
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

        // ─── 개발자 모드 EditorPrefs 키 ─────────────────────────────────────────
        // EditorPrefs.SetBool("BugBeacon_DevMode", true) 으로 수동 활성화.
        // 고급 섹션은 개발자 모드일 때만 표시됩니다.
        private const string DEV_MODE_PREF_KEY = "BugBeacon_DevMode";

        // ─── 웹 로그인 플로우 상태 ───────────────────────────────────────────────

        /// <summary>웹 로그인 플로우 상태 열거형</summary>
        private enum WebLoginState
        {
            /// <summary>대기 (초기 상태)</summary>
            Idle,
            /// <summary>브라우저 열기 + 폴링 중</summary>
            Polling,
            /// <summary>로그인 완료</summary>
            Completed,
            /// <summary>에러 또는 타임아웃</summary>
            Failed,
        }

        /// <summary>현재 웹 로그인 플로우 상태</summary>
        private WebLoginState _webLoginState = WebLoginState.Idle;

        /// <summary>auth-unity-start에서 받은 connect_id</summary>
        private string _webLoginConnectId;

        /// <summary>폴링 취소 토큰 소스. 창 닫힐 때 취소.</summary>
        private CancellationTokenSource _pollingCts;

        /// <summary>마지막 에러 메시지</summary>
        private string _webLoginErrorMessage;

        // ─── Foldout 상태 ────────────────────────────────────────────────────────

        private bool _foldWeb = true;
        private bool _foldCapture = true;
        private bool _foldReport = true;
        private bool _foldHotkey = true;
        private bool _foldCrashRecovery = true;
        private bool _foldAdvanced = false;

        // ─── 내부 상태 ───────────────────────────────────────────────────────────

        private BugBeaconSettings _settings;
        private SerializedObject _serializedSettings;

        /// <summary>Supabase access_token 암호화 저장소</summary>
        private readonly SessionTokenStore _tokenStore = new SessionTokenStore();

        // 스크롤 포지션
        private Vector2 _scrollPos;

        // ─── 메뉴 등록 ───────────────────────────────────────────────────────────

        [MenuItem(BugBeaconEditorInfo.MenuRoot + "/Settings")]
        public static void OpenWindow()
        {
            var window = GetWindow<BugBeaconSettingsWindow>("BugBeacon Settings");
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
            // 창이 닫히거나 비활성화되면 폴링 즉시 중단
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        private void OnDestroy()
        {
            // 창 파괴 시 폴링 중단
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _pollingCts = null;
        }

        private void OnGUI()
        {
            if (_settings == null)
            {
                LoadOrCreateSettings();
                if (_settings == null)
                {
                    EditorGUILayout.HelpBox("BugBeaconSettings 에셋을 찾을 수 없습니다.", MessageType.Error);
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

            // 개발자 모드일 때만 고급 섹션 표시
            // 활성화: EditorPrefs.SetBool("BugBeacon_DevMode", true)
            if (EditorPrefs.GetBool(DEV_MODE_PREF_KEY, false))
            {
                DrawAdvancedSection();
            }

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
                EditorGUILayout.LabelField("BugBeacon 설정", EditorStyles.boldLabel);
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

            // 연동 상태 표시
            SerializedProperty isLinkedProp = _serializedSettings.FindProperty("isLinked");
            SerializedProperty workspaceNameProp = _serializedSettings.FindProperty("linkedWorkspaceName");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("연동 상태");
            if (isLinkedProp.boolValue)
            {
                var origColor = GUI.contentColor;
                GUI.contentColor = new Color(0.2f, 0.8f, 0.3f);
                string displayName = string.IsNullOrEmpty(workspaceNameProp.stringValue)
                    ? "연동됨"
                    : $"연동됨 ({workspaceNameProp.stringValue})";
                EditorGUILayout.LabelField($"● {displayName}");
                GUI.contentColor = origColor;
            }
            else
            {
                var origColor = GUI.contentColor;
                GUI.contentColor = new Color(0.9f, 0.3f, 0.3f);
                EditorGUILayout.LabelField("○ 미연동");
                GUI.contentColor = origColor;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            // 연동/해제 버튼
            if (isLinkedProp.boolValue)
            {
                // 연동됨 상태: 연동 해제 버튼
                if (GUILayout.Button("연동 해제", GUILayout.Width(100f), GUILayout.Height(28f)))
                {
                    if (EditorUtility.DisplayDialog(
                        "웹 연동 해제",
                        "웹 대시보드와의 연동을 해제하시겠습니까?",
                        "해제", "취소"))
                    {
                        isLinkedProp.boolValue = false;
                        workspaceNameProp.stringValue = "";
                        // tenantId는 서버 값이므로 연동 해제 시 초기화
                        SerializedProperty tenantIdProp = _serializedSettings.FindProperty("tenantId");
                        tenantIdProp.stringValue = "";
                        _webLoginState = WebLoginState.Idle;
                        _webLoginErrorMessage = null;
                        Debug.Log("[BugBeacon] 웹 대시보드 연동 해제됨");
                        Repaint();
                    }
                }
            }
            else
            {
                // 미연동 상태: 폴링 중인지 여부에 따라 버튼 다르게 표시
                if (_webLoginState == WebLoginState.Polling)
                {
                    // 폴링 중: 대기 메시지 + 취소 버튼
                    EditorGUILayout.HelpBox(
                        "브라우저에서 로그인을 완료해주세요...",
                        MessageType.Info);
                    EditorGUILayout.Space(4f);

                    if (GUILayout.Button("로그인 대기 중... (취소)", GUILayout.Height(28f)))
                    {
                        // 폴링 취소
                        _pollingCts?.Cancel();
                        _webLoginState = WebLoginState.Idle;
                        _webLoginErrorMessage = null;
                        Debug.Log("[BugBeacon] 웹 로그인 플로우 취소됨");
                        Repaint();
                    }
                }
                else
                {
                    // Idle / Failed 상태: 웹 로그인 버튼
                    if (GUILayout.Button("웹 로그인", GUILayout.Width(120f), GUILayout.Height(28f)))
                    {
                        // 이전 폴링 정리
                        _pollingCts?.Cancel();
                        _pollingCts?.Dispose();
                        _pollingCts = new CancellationTokenSource();

                        // 비동기 로그인 플로우 시작
                        _ = StartWebLoginFlowAsync(_pollingCts.Token);
                    }

                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(
                        "버튼을 클릭하면 브라우저가 열립니다.\n웹에서 로그인하면 자동으로 연동이 완료됩니다.",
                        MessageType.Info);
                }

                // 에러 메시지 표시 (Failed 상태)
                if (_webLoginState == WebLoginState.Failed && !string.IsNullOrEmpty(_webLoginErrorMessage))
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.HelpBox(_webLoginErrorMessage, MessageType.Error);
                }
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
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

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("다시 확인", GUILayout.Width(80f)))
            {
                FfmpegHelper.ClearCache();
            }

            if (GUILayout.Button("FFmpeg 다운로드 페이지", GUILayout.Width(160f)))
            {
                Application.OpenURL("https://ffmpeg.org/download.html");
            }

            EditorGUILayout.EndHorizontal();
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

            // 디버그 로그
            DrawSectionHeader("디버그");
            SerializedProperty debugLogProp = _serializedSettings.FindProperty("debugLog");
            EditorGUILayout.PropertyField(
                debugLogProp,
                new GUIContent("디버그 로그", "디버그 로그 출력 활성화"));

            // 팀 ID 관리 (디버그 모드에서만 표시, 읽기전용)
            if (debugLogProp.boolValue)
            {
                EditorGUILayout.Space(8f);
                DrawTeamIdentitySubSection();
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        private void DrawTeamIdentitySubSection()
        {
            DrawSectionHeader("팀 ID 관리 (읽기전용)");

            EditorGUILayout.HelpBox("디버그 모드에서만 표시됩니다. 값은 자동 생성됩니다.", MessageType.Info);
            EditorGUILayout.Space(4f);

            SerializedProperty tenantIdProp = _serializedSettings.FindProperty("tenantId");
            SerializedProperty userIdProp   = _serializedSettings.FindProperty("userId");

            // 읽기전용으로 표시
            EditorGUI.BeginDisabledGroup(true);

            // ── 팀 ID (tenantId) ──
            EditorGUILayout.LabelField("팀 ID (tenantId)", EditorStyles.boldLabel);
            EditorGUILayout.TextField(tenantIdProp.stringValue);

            EditorGUILayout.Space(4f);

            // ── 사용자 ID (userId) ──
            EditorGUILayout.LabelField("사용자 ID (userId)", EditorStyles.boldLabel);
            EditorGUILayout.TextField(userIdProp.stringValue);

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4f);

            // 복사 버튼은 활성 상태로 유지
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("팀 ID 복사", GUILayout.Width(100f)))
            {
                GUIUtility.systemCopyBuffer = tenantIdProp.stringValue;
            }
            if (GUILayout.Button("사용자 ID 복사", GUILayout.Width(120f)))
            {
                GUIUtility.systemCopyBuffer = userIdProp.stringValue;
            }
            EditorGUILayout.EndHorizontal();
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

        // ─── 웹 로그인 플로우 ────────────────────────────────────────────────────

        /// <summary>
        /// 웹 로그인 플로우를 시작합니다.
        /// 1) auth-unity-start POST → connect_id + login_url 수신
        /// 2) 브라우저 열기
        /// 3) auth-unity-status 폴링 시작
        /// </summary>
        private async Task StartWebLoginFlowAsync(CancellationToken ct)
        {
            _webLoginState = WebLoginState.Polling;
            _webLoginErrorMessage = null;
            EditorApplication.delayCall += Repaint;

            try
            {
                // ── 1. auth-unity-start POST ─────────────────────────────────
                string startUrl = BugBeaconSettings.WEB_DASHBOARD_URL + "/api/unity/auth/start";
                string deviceId = SystemInfo.deviceUniqueIdentifier;
                // JSON 특수문자 이스케이프 처리 (쌍따옴표, 백슬래시)
                string escapedDeviceId = deviceId
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");
                string requestBody = $"{{\"device_id\":\"{escapedDeviceId}\"}}";

                Debug.Log("[BugBeacon] 웹 로그인 플로우 시작: device_id=" + deviceId);

                string startResponseJson = await PostJsonAsync(startUrl, requestBody, ct);
                if (startResponseJson == null)
                {
                    SetWebLoginFailed("서버 연결에 실패했습니다. 인터넷 연결을 확인해주세요.");
                    return;
                }

                // ── 2. connect_id / login_url 파싱 ───────────────────────────
                string connectId = ParseJsonString(startResponseJson, "connect_id");
                string loginUrl  = ParseJsonString(startResponseJson, "login_url");

                if (string.IsNullOrEmpty(connectId) || string.IsNullOrEmpty(loginUrl))
                {
                    Debug.LogError("[BugBeacon] auth-unity-start 응답 파싱 실패: " + startResponseJson);
                    SetWebLoginFailed("서버 응답을 파싱할 수 없습니다. 잠시 후 다시 시도해주세요.");
                    return;
                }

                _webLoginConnectId = connectId;
                Debug.Log("[BugBeacon] connect_id 수신: " + connectId);

                // ── 3. 브라우저 열기 ─────────────────────────────────────────
                Application.OpenURL(loginUrl);
                Debug.Log("[BugBeacon] 브라우저 열기: " + loginUrl);
                EditorApplication.delayCall += Repaint;

                // ── 4. 폴링 시작 ─────────────────────────────────────────────
                await PollAuthStatusAsync(connectId, ct);
            }
            catch (OperationCanceledException)
            {
                // 취소: Idle 상태로 돌아감
                if (_webLoginState == WebLoginState.Polling)
                {
                    _webLoginState = WebLoginState.Idle;
                    EditorApplication.delayCall += Repaint;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[BugBeacon] 웹 로그인 플로우 예외: " + ex);
                SetWebLoginFailed("오류가 발생했습니다: " + ex.Message);
            }
        }

        /// <summary>
        /// auth-unity-status를 3초 간격으로 폴링합니다.
        /// 최대 10분(200회) 후 타임아웃.
        /// </summary>
        private async Task PollAuthStatusAsync(string connectId, CancellationToken ct)
        {
            const int maxAttempts = 200; // 3초 × 200 = 600초 = 10분
            string statusUrl = BugBeaconSettings.WEB_DASHBOARD_URL
                + "/api/unity/auth/status?connect_id=" + Uri.EscapeDataString(connectId);

            for (int i = 0; i < maxAttempts; i++)
            {
                // 3초 대기
                await Task.Delay(3000, ct);

                if (ct.IsCancellationRequested) return;

                // 상태 조회
                string responseJson = await GetJsonAsync(statusUrl, ct);
                if (responseJson == null)
                {
                    // 네트워크 오류 시 다음 폴링 회차에서 재시도 (연속 실패는 나중에 처리)
                    Debug.LogWarning("[BugBeacon] 폴링 응답 없음 (" + (i + 1) + "/" + maxAttempts + ")");
                    continue;
                }

                string status = ParseJsonString(responseJson, "status");
                Debug.Log("[BugBeacon] 폴링 상태: " + status + " (" + (i + 1) + "/" + maxAttempts + ")");

                if (status == "completed")
                {
                    // ── 로그인 완료: 토큰 + workspace 정보 저장 ───────────────
                    string workspaceId   = ParseJsonString(responseJson, "workspace_id");
                    string workspaceName = ParseJsonString(responseJson, "workspace_name");
                    string accessToken   = ParseJsonString(responseJson, "access_token");

                    // SerializedObject를 통해 Settings 업데이트 (Undo 지원)
                    _serializedSettings.Update();

                    SerializedProperty isLinkedProp      = _serializedSettings.FindProperty("isLinked");
                    SerializedProperty workspaceNameProp = _serializedSettings.FindProperty("linkedWorkspaceName");
                    SerializedProperty tenantIdProp      = _serializedSettings.FindProperty("tenantId");

                    isLinkedProp.boolValue          = true;
                    workspaceNameProp.stringValue   = workspaceName ?? "";
                    // 서버의 workspace_id를 tenantId에 저장 (H1 이슈 수정: 서버 값으로 동기화)
                    if (!string.IsNullOrEmpty(workspaceId))
                        tenantIdProp.stringValue = workspaceId;

                    _serializedSettings.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_settings);
                    AssetDatabase.SaveAssets();

                    // access_token을 SessionTokenStore에 암호화 저장
                    if (!string.IsNullOrEmpty(accessToken))
                    {
                        try
                        {
                            _tokenStore.SaveSupabase(accessToken);
                            Debug.Log("[BugBeacon] access_token 암호화 저장 완료 (길이: " + accessToken.Length + ")");
                        }
                        catch (Exception saveEx)
                        {
                            // 토큰 저장 실패 시 로그인 상태를 Failed로 전환
                            Debug.LogError("[BugBeacon] access_token 저장 실패: " + saveEx.Message);
                            SetWebLoginFailed("토큰 저장에 실패했습니다. 다시 시도해주세요.");
                            return;
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[BugBeacon] 서버 응답에 access_token이 없습니다.");
                    }

                    _webLoginState = WebLoginState.Completed;
                    _webLoginErrorMessage = null;

                    Debug.Log("[BugBeacon] 웹 로그인 완료. workspace: "
                        + workspaceName + " / tenantId: " + workspaceId);

                    EditorApplication.delayCall += Repaint;
                    return;
                }

                if (status == "expired")
                {
                    SetWebLoginFailed("로그인 세션이 만료되었습니다. 다시 시도해주세요.");
                    return;
                }

                // status == "pending": 계속 대기
                EditorApplication.delayCall += Repaint;
            }

            // 10분 후 타임아웃
            SetWebLoginFailed("로그인 대기 시간(10분)이 초과되었습니다. 다시 시도해주세요.");
        }

        /// <summary>
        /// 웹 로그인 실패 상태로 전환합니다.
        /// </summary>
        private void SetWebLoginFailed(string message)
        {
            _webLoginState = WebLoginState.Failed;
            _webLoginErrorMessage = message;
            Debug.LogWarning("[BugBeacon] 웹 로그인 실패: " + message);
            EditorApplication.delayCall += Repaint;
        }

        /// <summary>
        /// JSON POST 요청을 비동기로 보냅니다.
        /// 성공 시 응답 바디 문자열 반환, 실패 시 null 반환.
        /// </summary>
        private async Task<string> PostJsonAsync(string url, string jsonBody, CancellationToken ct)
        {
            using var www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            www.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            // 네트워크 응답 무한 대기 방지: 15초 타임아웃
            www.timeout = 15;

            var op = www.SendWebRequest();

            // UnityWebRequest를 Task로 래핑
            while (!op.isDone)
            {
                if (ct.IsCancellationRequested)
                {
                    www.Abort();
                    return null;
                }
                await Task.Yield();
            }

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[BugBeacon] POST 실패 (" + url + "): " + www.error);
                return null;
            }

            return www.downloadHandler.text;
        }

        /// <summary>
        /// JSON GET 요청을 비동기로 보냅니다.
        /// 성공 시 응답 바디 문자열 반환, 실패 시 null 반환.
        /// </summary>
        private async Task<string> GetJsonAsync(string url, CancellationToken ct)
        {
            using var www = UnityWebRequest.Get(url);
            www.SetRequestHeader("Accept", "application/json");
            // 네트워크 응답 무한 대기 방지: 15초 타임아웃
            www.timeout = 15;

            var op = www.SendWebRequest();

            while (!op.isDone)
            {
                if (ct.IsCancellationRequested)
                {
                    www.Abort();
                    return null;
                }
                await Task.Yield();
            }

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[BugBeacon] GET 실패 (" + url + "): " + www.error);
                return null;
            }

            return www.downloadHandler.text;
        }

        /// <summary>
        /// JSON 문자열에서 특정 키의 값을 단순 파싱합니다.
        /// JsonUtility 없이 문자열 처리로 동작 (에디터 전용, 성능 비중요).
        /// </summary>
        private static string ParseJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            // "key":"value" 또는 "key": "value" 패턴 탐색
            string searchKey = "\"" + key + "\"";
            int keyIndex = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIndex < 0) return null;

            int colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return null;

            int quoteStart = json.IndexOf('"', colonIndex + 1);
            if (quoteStart < 0) return null;

            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return null;

            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        // ─── 헬퍼 메서드 ────────────────────────────────────────────────────────

        /// <summary>
        /// BugBeaconSettings 에셋을 로드하거나 찾을 수 없으면 생성합니다.
        /// </summary>
        private void LoadOrCreateSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:BugBeaconSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<BugBeaconSettings>(path);
            }

            if (_settings == null)
            {
                const string ResourcesPath = "Assets/Resources";
                const string AssetPath     = ResourcesPath + "/BugBeaconSettings.asset";

                if (!AssetDatabase.IsValidFolder(ResourcesPath))
                    AssetDatabase.CreateFolder("Assets", "Resources");

                _settings = CreateInstance<BugBeaconSettings>();
                AssetDatabase.CreateAsset(_settings, AssetPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[BugBeacon] BugBeaconSettings 에셋 생성: " + AssetPath);
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
            Debug.Log("[BugBeacon] 설정 저장 완료");
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
