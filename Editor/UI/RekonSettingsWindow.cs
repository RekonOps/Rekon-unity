using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace RekonOps.Rekon.Editor
{
    /// <summary>
    /// Rekon 설정 에디터 윈도우.
    /// Window/Rekon/Settings 메뉴에서 열립니다.
    ///
    /// 단일 스크롤 윈도우 + 접이식(Foldout) 섹션 구조:
    ///   웹 연동       - 연동 상태 표시, 웹 대시보드 연동 버튼
    ///   캡처 설정     - 영상 프리셋, 해상도/FPS/비트레이트/버퍼, 스크린샷, 로그
    ///   리포트 설정   - 제목 접두어, 타임스탬프 형식, 메타데이터 토글
    ///   단축키        - 캡처 핫키 (Mac/Windows 플랫폼별)
    ///   고급          - 디버그 로그, 팀 ID (디버그 시에만)
    ///
    /// SerializedObject 기반으로 변경 감지 및 Undo 지원.
    /// </summary>
    public class RekonSettingsWindow : EditorWindow
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
        // EditorPrefs.SetBool("Rekon_DevMode", true) 으로 수동 활성화.
        // 고급 섹션은 개발자 모드일 때만 표시됩니다.
        private const string DEV_MODE_PREF_KEY = "Rekon_DevMode";

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

        // #33: 폴링 시작 시각 (남은 시간 계산용)
        private float _pollingStartTime;

        // #34: 연동 완료 배너 표시 종료 시각
        private double _completedMessageUntil = 0;

        // ─── Foldout 상태 ────────────────────────────────────────────────────────

        private bool _foldWeb = true;
        private bool _foldCodec = true;
        private bool _foldCapture = true;
        private bool _foldReport = true;
        private bool _foldHotkey = true;
        private bool _foldAdvanced = false;

        // ─── 내부 상태 ───────────────────────────────────────────────────────────

        private RekonSettings _settings;
        private SerializedObject _serializedSettings;

        /// <summary>Supabase access_token 암호화 저장소</summary>
        private readonly SessionTokenStore _tokenStore = new SessionTokenStore();

        /// <summary>라이선스 검증 클라이언트 (웹 로그인 완료 후 초기화)</summary>
        private LicenseValidator _licenseValidator;

        // 스크롤 포지션
        private Vector2 _scrollPos;

        // ─── 메뉴 등록 ───────────────────────────────────────────────────────────

        [MenuItem(RekonEditorInfo.MenuRoot + "/Settings")]
        public static void OpenWindow()
        {
            var window = GetWindow<RekonSettingsWindow>("Rekon Settings");
            window.minSize = new Vector2(420f, 500f);
            window.Show();
        }

        // ─── 생명주기 ───────────────────────────────────────────────────────────

        private void OnEnable()
        {
            LoadOrCreateSettings();
            RestorePlanLimitsFromCache();
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
                    EditorGUILayout.HelpBox("RekonSettings 에셋을 찾을 수 없습니다.", MessageType.Error);
                    return;
                }
            }

            // SerializedObject 업데이트
            _serializedSettings.Update();

            DrawHeader();

            // 전체 스크롤뷰
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            BeginSectionBox();
            DrawWebSection();
            EndSectionBox();

            BeginSectionBox();
            DrawCodecStatusSection();
            EndSectionBox();

            // #31: 미연동 시 하위 섹션 비활성화
            bool isLinked = _serializedSettings.FindProperty("isLinked").boolValue;
            using (new EditorGUI.DisabledScope(!isLinked))
            {
                if (!isLinked)
                {
                    EditorGUILayout.HelpBox(
                        "웹 대시보드 연동 후 아래 설정을 사용할 수 있습니다.",
                        MessageType.Info);
                }

                BeginSectionBox();
                DrawCaptureSection();
                EndSectionBox();

                BeginSectionBox();
                DrawReportSection();
                EndSectionBox();

                BeginSectionBox();
                DrawHotkeySection();
                EndSectionBox();
            }

            // 개발자 모드일 때만 고급 섹션 표시
            // 활성화: EditorPrefs.SetBool("Rekon_DevMode", true)
            if (EditorPrefs.GetBool(DEV_MODE_PREF_KEY, false))
            {
                DrawAdvancedSection();
            }

            EditorGUILayout.EndScrollView();

            DrawFooter();

            // #35: 자동 저장 — 변경 감지 시 즉시 Dirty 마킹
            if (_serializedSettings.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(_settings);
            }
        }

        // ─── 섹션 박스 헬퍼 ─────────────────────────────────────────────────────

        /// <summary>섹션 박스 시작 (helpBox 스타일로 시각적 구분)</summary>
        private static void BeginSectionBox()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(4f);
        }

        /// <summary>섹션 박스 종료</summary>
        private static void EndSectionBox()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        // ─── 헤더 ───────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.Space(6f);

            // #38: 헤더 왼쪽 타이틀 + 오른쪽 버전 표시
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Rekon 설정", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("v0.1.5", EditorStyles.miniLabel, GUILayout.Width(40f));
            }

            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        // ─── 웹 연동 섹션 ───────────────────────────────────────────────────────

        private void DrawWebSection()
        {
            _foldWeb = EditorGUILayout.Foldout(_foldWeb, "웹 연동", true, EditorStyles.foldoutHeader);
            if (!_foldWeb) return;

            EditorGUI.indentLevel++;

            SerializedProperty isLinkedProp      = _serializedSettings.FindProperty("isLinked");
            SerializedProperty workspaceNameProp = _serializedSettings.FindProperty("linkedWorkspaceName");
            bool linked          = isLinkedProp.boolValue;
            string workspaceName = workspaceNameProp.stringValue;

            // #32: 연동 상태 배지 (배경 박스)
            DrawConnectionStatusBadge(linked, workspaceName);

            EditorGUILayout.Space(4f);

            // #39: 첫 실행 온보딩 배너 (미연동 시)
            if (!linked)
            {
                EditorGUILayout.HelpBox(
                    "시작하기: 아래 '웹 연동' 섹션에서 웹 로그인 버튼을 클릭하세요.",
                    MessageType.Info);
            }

            // #34: 연동 완료 5초 성공 배너
            if (EditorApplication.timeSinceStartup < _completedMessageUntil)
            {
                string displayName = string.IsNullOrEmpty(workspaceName) ? "워크스페이스" : workspaceName;
                EditorGUILayout.HelpBox(
                    $"연동 완료! '{displayName}' 워크스페이스에 연결되었습니다.",
                    MessageType.Info);
                Repaint();
            }

            EditorGUILayout.Space(4f);

            // 연동/해제 버튼
            if (linked)
            {
                // 연동됨 상태: 연동 해제 버튼
                float btnWidth = Mathf.Clamp(position.width * 0.3f, 80f, 160f); // #44
                if (GUILayout.Button("연동 해제", GUILayout.Width(btnWidth), GUILayout.Height(28f)))
                {
                    // #42: "취소"를 기본(OK), "연동 해제"를 보조(Cancel)
                    bool cancelled = EditorUtility.DisplayDialog(
                        "웹 연동 해제",
                        "연동을 해제하면 버그 리포트가 전송되지 않습니다.\n재연동하려면 다시 웹 로그인이 필요합니다.",
                        "취소",
                        "연동 해제"
                    );
                    if (!cancelled)
                    {
                        isLinkedProp.boolValue = false;
                        workspaceNameProp.stringValue = "";
                        // tenantId는 서버 값이므로 연동 해제 시 초기화
                        SerializedProperty tenantIdProp = _serializedSettings.FindProperty("tenantId");
                        tenantIdProp.stringValue = "";
                        _webLoginState = WebLoginState.Idle;
                        _webLoginErrorMessage = null;
                        Debug.Log("[Rekon] 웹 대시보드 연동 해제됨");
                        Repaint();
                    }
                }
            }
            else
            {
                // 미연동 상태: 폴링 중인지 여부에 따라 버튼 다르게 표시
                if (_webLoginState == WebLoginState.Polling)
                {
                    // #33: 폴링 중 남은 시간 + 프로그레스 바
                    float elapsed   = (float)EditorApplication.timeSinceStartup - _pollingStartTime;
                    float remaining = 600f - elapsed;
                    int remainMin   = Mathf.Max(0, (int)(remaining / 60f));
                    int remainSec   = Mathf.Max(0, (int)(remaining % 60f));

                    EditorGUILayout.HelpBox(
                        $"브라우저에서 로그인을 완료해주세요.\n남은 시간: {remainMin}분 {remainSec:00}초",
                        MessageType.Info);

                    float progress = Mathf.Clamp01(elapsed / 600f);
                    Rect barRect = EditorGUILayout.GetControlRect(false, 4f);
                    EditorGUI.ProgressBar(barRect, progress, "");

                    EditorGUILayout.Space(4f);

                    if (GUILayout.Button("로그인 대기 중... (취소)", GUILayout.Height(28f)))
                    {
                        // 폴링 취소
                        _pollingCts?.Cancel();
                        _webLoginState = WebLoginState.Idle;
                        _webLoginErrorMessage = null;
                        Debug.Log("[Rekon] 웹 로그인 플로우 취소됨");
                        Repaint();
                    }
                }
                else
                {
                    // Idle / Failed 상태: 웹 로그인 버튼
                    float btnWidth = Mathf.Clamp(position.width * 0.3f, 80f, 160f); // #44
                    if (GUILayout.Button("웹 로그인", GUILayout.Width(btnWidth), GUILayout.Height(28f)))
                    {
                        // 이전 폴링 정리
                        _pollingCts?.Cancel();
                        _pollingCts?.Dispose();
                        _pollingCts = new CancellationTokenSource();

                        // #33: 폴링 시작 시각 기록
                        _pollingStartTime = (float)EditorApplication.timeSinceStartup;

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

        /// <summary>
        /// #32: 연동 상태를 배경 박스로 시각화합니다.
        /// </summary>
        private void DrawConnectionStatusBadge(bool isLinked, string workspaceName)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 36f);
            Color bgColor = isLinked
                ? new Color(0.15f, 0.35f, 0.15f, 0.5f)
                : new Color(0.35f, 0.15f, 0.15f, 0.5f);
            EditorGUI.DrawRect(rect, bgColor);

            string label = isLinked
                ? $"● 연동됨  |  {workspaceName}"
                : "○ 미연동  —  웹 로그인이 필요합니다";

            GUI.Label(rect, label, new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 12,
            });
        }

        // ─── 코덱 상태 섹션 ─────────────────────────────────────────────────────

        private void DrawCodecStatusSection()
        {
            _foldCodec = EditorGUILayout.Foldout(_foldCodec, "코덱 상태", true, EditorStyles.foldoutHeader);
            if (!_foldCodec) return;

            EditorGUI.indentLevel++;

            bool isInstalled = FfmpegHelper.IsInstalled();

            // 상태 배지 (연동 상태 배지와 동일 스타일)
            Rect rect = EditorGUILayout.GetControlRect(false, 36f);
            Color bgColor = isInstalled
                ? new Color(0.15f, 0.35f, 0.15f, 0.5f)
                : new Color(0.35f, 0.15f, 0.15f, 0.5f);
            EditorGUI.DrawRect(rect, bgColor);

            string label = isInstalled
                ? "● 코덱 설치됨  |  FFmpeg"
                : "○ 코덱 미설치  —  영상 캡처 불가";

            GUI.Label(rect, label, new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 12,
            });

            EditorGUILayout.Space(4f);

            if (isInstalled)
            {
                // GPU 인코더 정보 표시
                string gpuEncoder = FfmpegHelper.GetGpuEncoder();
                EditorGUILayout.LabelField("GPU 인코더",
                    !string.IsNullOrEmpty(gpuEncoder) ? $"✓ {gpuEncoder}" : "없음 (libx264 CPU 사용)",
                    EditorStyles.miniLabel);
            }
            else
            {
                // 미설치 시 버튼
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("다시 확인", GUILayout.Height(24f)))
                        FfmpegHelper.ClearCache();
                    if (GUILayout.Button("FFmpeg 다운로드 페이지", GUILayout.Height(24f)))
                        Application.OpenURL("https://ffmpeg.org/download.html");
                }
            }

            EditorGUI.indentLevel--;
        }

        // ─── 캡처 설정 섹션 ─────────────────────────────────────────────────────

        private void DrawCaptureSection()
        {
            _foldCapture = EditorGUILayout.Foldout(_foldCapture, "캡처 설정", true, EditorStyles.foldoutHeader);
            if (!_foldCapture) return;

            EditorGUI.indentLevel++;

            // ── 영상 캡처 설정 박스 ───────────────────────────────────────────
            BeginSectionBox();
            DrawVideoPresetSubSection();
            EndSectionBox();

            EditorGUILayout.Space(4f);

            // ── 스크린샷 캡처 설정 박스 ──────────────────────────────────────
            BeginSectionBox();
            DrawSectionHeader("스크린샷 캡처 설정");
            SerializedProperty downscale = _serializedSettings.FindProperty("screenshotDownscale");
            EditorGUILayout.IntSlider(
                downscale,
                1, 4,
                new GUIContent("다운스케일 배율", "1 = 원본 해상도, 2 = 절반, 4 = 1/4 크기"));
            EndSectionBox();

            EditorGUILayout.Space(4f);

            // ── 로그 설정 박스 ───────────────────────────────────────────────
            BeginSectionBox();
            DrawSectionHeader("로그");
            SerializedProperty logBufferSize = _serializedSettings.FindProperty("logBufferSize");
            EditorGUILayout.IntSlider(
                logBufferSize,
                100, 5000,
                new GUIContent("로그 버퍼 크기", "링 버퍼에 보관할 최대 로그 라인 수"));
            EndSectionBox();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        private void DrawVideoPresetSubSection()
        {
            DrawSectionHeader("영상 캡처 설정");

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
                        10, 180,
                        new GUIContent("버퍼 시간 (초)", "링 버퍼에 보관하는 영상 길이 (10~180초)"));
                }
            }

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

                // GPU 인코더 감지 결과 표시
                string gpuEncoder = FfmpegHelper.GetGpuEncoder();
                if (!string.IsNullOrEmpty(gpuEncoder))
                    EditorGUILayout.LabelField("GPU 인코더", $"✓ {gpuEncoder}");
                else
                    EditorGUILayout.LabelField("GPU 인코더", "없음 (libx264 CPU 사용)");
            }
            else
            {
                EditorGUILayout.LabelField("상태", "✗ 미설치");

                EditorGUILayout.Space(4f);

                EditorGUILayout.HelpBox(
                    "FFmpeg가 설치되어 있지 않습니다.\n" +
                    "영상 캡처가 비활성화됩니다. 스크린샷과 로그는 정상 동작합니다.\n\n" +
                    "영상 캡처를 사용하려면 아래 방법으로 FFmpeg를 설치하세요.",
                    MessageType.Warning);

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
            _foldHotkey = EditorGUILayout.Foldout(_foldHotkey, "단축키", true, EditorStyles.foldoutHeader);
            if (!_foldHotkey) return;

            EditorGUI.indentLevel++;

            bool isMac = Application.platform == RuntimePlatform.OSXEditor;

            // 수식키 토글
            SerializedProperty ctrlCmd = _serializedSettings.FindProperty("hotkeyCtrlOrCmd");
            SerializedProperty shift   = _serializedSettings.FindProperty("hotkeyShift");
            SerializedProperty alt     = _serializedSettings.FindProperty("hotkeyAlt");
            SerializedProperty hotkey  = _serializedSettings.FindProperty("captureHotkey");

            // #45: 수식키 토글 너비 동적 계산 (3등분)
            float toggleWidth = (EditorGUIUtility.currentViewWidth - 60f) / 3f;

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            bool newCtrlCmd = EditorGUILayout.ToggleLeft(
                isMac ? "⌘ Cmd" : "Ctrl", ctrlCmd.boolValue, GUILayout.Width(toggleWidth));
            if (EditorGUI.EndChangeCheck())
                ctrlCmd.boolValue = newCtrlCmd;

            EditorGUI.BeginChangeCheck();
            bool newShift = EditorGUILayout.ToggleLeft(
                isMac ? "⇧ Shift" : "Shift", shift.boolValue, GUILayout.Width(toggleWidth));
            if (EditorGUI.EndChangeCheck())
                shift.boolValue = newShift;

            EditorGUI.BeginChangeCheck();
            bool newAlt = EditorGUILayout.ToggleLeft(
                isMac ? "⌥ Option" : "Alt", alt.boolValue, GUILayout.Width(toggleWidth));
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

            // #41: 단축키 프리뷰를 선택 UI 아래로
            // #46: BuildHotkeyPreview에 파라미터로 전달 (FindProperty 중복 호출 제거)
            string preview = BuildHotkeyPreview(isMac, ctrlCmd, shift, alt, hotkey);
            EditorGUILayout.HelpBox($"현재 단축키: {preview}", MessageType.Info);

            // #40: 수식키 없음 경고
            bool hasModifier = ctrlCmd.boolValue || shift.boolValue || alt.boolValue;
            if (!hasModifier)
            {
                EditorGUILayout.HelpBox(
                    "수식키 없이 설정하면 플레이 모드에서 오작동할 수 있습니다.",
                    MessageType.Warning);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            DrawSeparator();
        }

        // #46: FindProperty 중복 호출 제거 — 프로퍼티를 파라미터로 전달
        private static string BuildHotkeyPreview(
            bool isMac,
            SerializedProperty ctrlCmd,
            SerializedProperty shift,
            SerializedProperty alt,
            SerializedProperty hotkey)
        {
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
            // #43: GUILayout.FlexibleSpace() 제거 → 고정 여백으로 대체
            EditorGUILayout.Space(8f);
            DrawSeparator();
            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                // 되돌리기 버튼 (#35: 적용 버튼 제거, 자동 저장으로 대체)
                if (GUILayout.Button("되돌리기", GUILayout.Width(80f)))
                {
                    _serializedSettings.Update();
                    Undo.RevertAllDownToGroup(Undo.GetCurrentGroup());
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
                string startUrl = RekonSettings.WEB_DASHBOARD_URL + "/api/unity/auth/start";
                string deviceId = SystemInfo.deviceUniqueIdentifier;
                // JSON 특수문자 이스케이프 처리 (쌍따옴표, 백슬래시)
                string escapedDeviceId = deviceId
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");
                string requestBody = $"{{\"device_id\":\"{escapedDeviceId}\"}}";

                Debug.Log("[Rekon] 웹 로그인 플로우 시작: device_id=" + deviceId);

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
                    Debug.LogError("[Rekon] auth-unity-start 응답 파싱 실패: " + startResponseJson);
                    SetWebLoginFailed("서버 응답을 파싱할 수 없습니다. 잠시 후 다시 시도해주세요.");
                    return;
                }

                _webLoginConnectId = connectId;
                Debug.Log("[Rekon] connect_id 수신: " + connectId);

                // ── 3. 브라우저 열기 ─────────────────────────────────────────
                Application.OpenURL(loginUrl);
                Debug.Log("[Rekon] 브라우저 열기: " + loginUrl);
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
                Debug.LogError("[Rekon] 웹 로그인 플로우 예외: " + ex);
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
            string statusUrl = RekonSettings.WEB_DASHBOARD_URL
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
                    Debug.LogWarning("[Rekon] 폴링 응답 없음 (" + (i + 1) + "/" + maxAttempts + ")");
                    continue;
                }

                string status = ParseJsonString(responseJson, "status");
                Debug.Log("[Rekon] 폴링 상태: " + status + " (" + (i + 1) + "/" + maxAttempts + ")");

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
                            Debug.Log("[Rekon] access_token 암호화 저장 완료 (길이: " + accessToken.Length + ")");
                        }
                        catch (Exception saveEx)
                        {
                            // 토큰 저장 실패 시 로그인 상태를 Failed로 전환
                            Debug.LogError("[Rekon] access_token 저장 실패: " + saveEx.Message);
                            SetWebLoginFailed("토큰 저장에 실패했습니다. 다시 시도해주세요.");
                            return;
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[Rekon] 서버 응답에 access_token이 없습니다.");
                    }

                    _webLoginState = WebLoginState.Completed;
                    _webLoginErrorMessage = null;

                    // #34: 연동 완료 5초 성공 배너 타이머 시작
                    _completedMessageUntil = EditorApplication.timeSinceStartup + 5.0;

                    Debug.Log("[Rekon] 웹 로그인 완료. workspace: "
                        + workspaceName + " / tenantId: " + workspaceId);

                    // 웹 로그인 완료 직후 라이선스 검증 → 플랜 제한값 적용
                    _ = ValidateLicenseAndApplyLimitsAsync(ct);

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
        /// 에디터 창이 열릴 때 캐시된 라이선스에서 플랜 제한값을 즉시 복원합니다.
        /// 네트워크 없이도 이전 세션에서 검증된 값이 UI에 반영됩니다.
        /// </summary>
        private void RestorePlanLimitsFromCache()
        {
            if (_settings == null) return;

            string brokerUrl = _settings.authBrokerUrl;
            if (string.IsNullOrEmpty(brokerUrl)) return;

            try
            {
                if (_licenseValidator == null)
                    _licenseValidator = new LicenseValidator(brokerUrl, "", _tokenStore);

                var cached = _licenseValidator.GetCachedLicense();
                if (cached != null && cached.Valid)
                {
                    _settings.maxAllowedBufferSeconds   = cached.MaxBufferSeconds;
                    _settings.maxAllowedScreenshotCount = cached.MaxScreenshotCount;
                    Debug.Log($"[Rekon] 캐시에서 플랜 제한값 복원: maxBuffer={cached.MaxBufferSeconds}초, " +
                              $"maxScreenshot={cached.MaxScreenshotCount}개");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] 캐시 플랜 제한값 복원 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 라이선스를 검증하고 플랜 제한값을 RekonSettings에 적용합니다.
        /// 웹 로그인 완료 후 비동기로 호출됩니다.
        /// </summary>
        private async Task ValidateLicenseAndApplyLimitsAsync(CancellationToken ct)
        {
            if (_settings == null) return;

            // authBrokerUrl이 설정되어 있어야 함
            string brokerUrl = _settings.authBrokerUrl;
            if (string.IsNullOrEmpty(brokerUrl))
            {
                Debug.LogWarning("[Rekon] 라이선스 검증 건너뜀: authBrokerUrl이 비어있습니다.");
                return;
            }

            try
            {
                // LicenseValidator 생성 (또는 재사용)
                if (_licenseValidator == null)
                    _licenseValidator = new LicenseValidator(brokerUrl, "", _tokenStore);

                // licenseKey/userId가 없어도 JWT(access_token)만으로 서버에서 자동 조회합니다.
                var licenseInfo = await _licenseValidator.ValidateAsync(
                    _settings.licenseKey, _settings.userId, ct);

                if (licenseInfo != null && licenseInfo.Valid)
                {
                    // 플랜 제한값을 settings에 반영
                    _settings.maxAllowedBufferSeconds  = licenseInfo.MaxBufferSeconds;
                    _settings.maxAllowedScreenshotCount = licenseInfo.MaxScreenshotCount;

                    Debug.Log($"[Rekon] 플랜 제한값 적용: plan={licenseInfo.Plan}, " +
                              $"maxBuffer={licenseInfo.MaxBufferSeconds}초, " +
                              $"maxScreenshot={licenseInfo.MaxScreenshotCount}개, " +
                              $"maxSeats={licenseInfo.MaxSeats}명");

                    EditorApplication.delayCall += Repaint;
                }
            }
            catch (OperationCanceledException)
            {
                // 창 닫힘 등으로 취소 → 무시
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] 라이선스 검증 실패 (플랜 제한값 미적용): {ex.Message}");
            }
        }

        /// <summary>
        /// 웹 로그인 실패 상태로 전환합니다.
        /// </summary>
        private void SetWebLoginFailed(string message)
        {
            _webLoginState = WebLoginState.Failed;
            _webLoginErrorMessage = message;
            Debug.LogWarning("[Rekon] 웹 로그인 실패: " + message);
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
                Debug.LogWarning("[Rekon] POST 실패 (" + url + "): " + www.error);
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
                Debug.LogWarning("[Rekon] GET 실패 (" + url + "): " + www.error);
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
        /// RekonSettings 에셋을 로드하거나 찾을 수 없으면 생성합니다.
        /// </summary>
        private void LoadOrCreateSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:RekonSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _settings = AssetDatabase.LoadAssetAtPath<RekonSettings>(path);
            }

            if (_settings == null)
            {
                const string ResourcesPath = "Assets/Resources";
                const string AssetPath     = ResourcesPath + "/RekonSettings.asset";

                if (!AssetDatabase.IsValidFolder(ResourcesPath))
                    AssetDatabase.CreateFolder("Assets", "Resources");

                _settings = CreateInstance<RekonSettings>();
                AssetDatabase.CreateAsset(_settings, AssetPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[Rekon] RekonSettings 에셋 생성: " + AssetPath);
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
            Debug.Log("[Rekon] 설정 저장 완료");
        }

        /// <summary>
        /// 섹션 구분 헤더를 그립니다.
        /// #37: 타이틀 아래 40% 너비 언더라인 표시
        /// </summary>
        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            Rect lastRect = GUILayoutUtility.GetLastRect();
            float lineWidth = lastRect.width * 0.4f;
            Rect lineRect = new Rect(lastRect.x, lastRect.yMax, lineWidth, 1f);
            EditorGUI.DrawRect(lineRect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            GUILayout.Space(2f);
            EditorGUILayout.Space(4f);
        }

        /// <summary>
        /// 캡처 설정 내 서브섹션 사이의 얇은 구분선을 그립니다.
        /// </summary>
        private static void DrawSubSectionSeparator()
        {
            EditorGUILayout.Space(6f);
            Rect lineRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(lineRect, new Color(0.3f, 0.3f, 0.3f, 0.3f));
            EditorGUILayout.Space(6f);
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
