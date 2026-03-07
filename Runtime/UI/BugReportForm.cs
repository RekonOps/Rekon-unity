using System;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 캡처 완료 후 화면 중앙에 표시되는 버그 리포트 입력 폼.
    ///
    /// 기능:
    ///   - 제목(필수), 설명, 재현 단계 입력
    ///   - 영상 포함 토글
    ///   - "Jira에 제출" 버튼: JiraSubmissionService로 제출
    ///   - "로컬 저장" 버튼: BundleWriter로만 저장 (Pending 상태)
    ///   - 제출 중 프로그레스 표시
    ///   - 결과: 이슈 키 표시 또는 오류 메시지
    ///
    /// IMGUI (OnGUI) 기반. UI Toolkit / uGUI 의존 없음.
    /// </summary>
    [DisallowMultipleComponent]
    public class BugReportForm : MonoBehaviour
    {
        // ─── 폼 상태 열거형 ───────────────────────────────────────────────────────

        private enum FormState
        {
            /// <summary>폼이 숨겨진 상태</summary>
            Hidden,

            /// <summary>사용자 입력 대기 중</summary>
            Editing,

            /// <summary>Jira 제출 또는 저장 진행 중</summary>
            Submitting,

            /// <summary>제출/저장 완료 결과 표시</summary>
            Result,
        }

        // ─── 폼 크기 상수 ─────────────────────────────────────────────────────────

        private const float FormWidth      = 520f;
        private const float FormHeight     = 440f;
        private const float FieldLabelWidth = 80f;
        private const int   TextAreaLines  = 4;
        private const float LineHeight     = 18f;
        private const float Padding        = 12f;

        // ─── 입력 필드 ────────────────────────────────────────────────────────────

        private string _title            = "";
        private string _description      = "";
        private string _stepsToReproduce = "";
        private bool   _includeVideo     = true;

        // ─── 이슈 타입 / 우선순위 ─────────────────────────────────────────────────

        private static readonly string[] FallbackIssueTypes = { "Bug", "Task", "Story", "Epic", "Sub-task" };
        private static readonly string[] FallbackPriorities  = { "Highest", "High", "Medium", "Low", "Lowest" };

        private string[] _activeIssueTypes;
        private string[] _activePriorities;

        private string _issueType      = "Bug";
        private string _priority       = "Medium";
        private int    _issueTypeIndex = 0;
        private int    _priorityIndex  = 2;

        // ─── 상태 ─────────────────────────────────────────────────────────────────

        private FormState _state = FormState.Hidden;

        /// <summary>캡처 결과 (ShowForm으로 주입)</summary>
        private CaptureResult _captureResult;

        /// <summary>번들 매니페스트 (BundleWriter 완료 후)</summary>
        private BundleManifest _bundle;

        // ─── 제출 상태 ────────────────────────────────────────────────────────────

        private float  _submitProgress  = 0f;
        private string _submitStageText = "";
        private string _resultIssueKey  = "";
        private string _resultMessage   = "";
        private bool   _resultSuccess   = false;
        private string _resultIssueUrl  = "";
        private string _resultWebUrl    = "";

        // ─── 제출 취소 토큰 ───────────────────────────────────────────────────────

        private CancellationTokenSource _cancelSource;

        // ─── 의존성 ───────────────────────────────────────────────────────────────

        private JiraSubmissionService _jiraService;
        private BundleWriter          _bundleWriter;
        private BundleRepository      _bundleRepository;
        private BugOneTouchSettings   _settings;
        private ReportSubmitter       _reportSubmitter;
        private LicenseValidator      _licenseValidator;
        private SupabaseAuthClient    _supabaseAuthClient;

        // ─── GUI 스타일 캐시 ──────────────────────────────────────────────────────

        private GUIStyle _windowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _textAreaStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _errorStyle;
        private GUIStyle _successStyle;
        private bool     _stylesInitialized = false;

        // 스크롤 위치
        private Vector2 _scrollPos;

        // ─── 정적 팩토리 ──────────────────────────────────────────────────────────

        /// <summary>
        /// BugReportForm 인스턴스를 반환하거나 없으면 생성합니다.
        /// DontDestroyOnLoad로 씬 전환 시에도 유지됩니다.
        /// </summary>
        public static BugReportForm EnsureInstance()
        {
            BugReportForm existing = FindObjectOfType<BugReportForm>();
            if (existing != null) return existing;

            GameObject go = new GameObject("[BugOneTouch] BugReportForm");
            DontDestroyOnLoad(go);
            return go.AddComponent<BugReportForm>();
        }

        // ─── 초기화 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 의존성을 주입합니다. 인스턴스 생성 직후 호출해야 합니다.
        /// </summary>
        public void SetDependencies(
            JiraSubmissionService jiraService,
            BundleWriter bundleWriter,
            BundleRepository bundleRepository,
            BugOneTouchSettings settings,
            ReportSubmitter reportSubmitter = null,
            LicenseValidator licenseValidator = null,
            SupabaseAuthClient supabaseAuthClient = null)
        {
            _jiraService        = jiraService;
            _bundleWriter       = bundleWriter;
            _bundleRepository   = bundleRepository;
            _settings           = settings;
            _reportSubmitter    = reportSubmitter;
            _licenseValidator   = licenseValidator;
            _supabaseAuthClient = supabaseAuthClient;
        }

        // ─── 공개 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 버그 리포트 폼을 표시합니다.
        /// 캡처 완료 이벤트 핸들러에서 호출하세요.
        /// </summary>
        /// <param name="captureResult">캡처 파이프라인 결과</param>
        public void ShowForm(CaptureResult captureResult)
        {
            if (_state != FormState.Hidden && _state != FormState.Result)
            {
                Debug.LogWarning("[BugOneTouch] BugReportForm: 이미 폼이 표시 중입니다.");
                return;
            }

            _captureResult       = captureResult;
            _title               = "";
            _description         = "";
            _stepsToReproduce    = "";
            _includeVideo        = true;
            _submitProgress      = 0f;
            _submitStageText     = "";
            _resultIssueKey      = "";
            _resultMessage       = "";
            _resultSuccess       = false;
            _resultIssueUrl      = "";
            _resultWebUrl        = "";
            _bundle              = null;
            _scrollPos           = Vector2.zero;

            // 동적 이슈타입 로드 (Settings 캐시 → 폴백)
            if (_settings != null && _settings.cachedIssueTypeNames != null && _settings.cachedIssueTypeNames.Length > 0)
                _activeIssueTypes = _settings.cachedIssueTypeNames;
            else
                _activeIssueTypes = FallbackIssueTypes;

            // 동적 우선순위 로드 (Settings allowedValues 캐시 → 폴백)
            string[] cachedPriorities = _settings?.GetFieldAllowedValues("priority");
            if (cachedPriorities != null && cachedPriorities.Length > 0)
                _activePriorities = cachedPriorities;
            else
                _activePriorities = FallbackPriorities;

            // 기본값 설정
            if (_settings != null)
            {
                _issueType = _settings.jiraDefaultIssueType;
                _priority  = _settings.jiraDefaultPriority;
            }

            _issueTypeIndex = Array.IndexOf(_activeIssueTypes, _issueType);
            if (_issueTypeIndex < 0) _issueTypeIndex = 0;
            _priorityIndex  = Array.IndexOf(_activePriorities, _priority);
            if (_priorityIndex < 0) _priorityIndex  = 2;

            _state = FormState.Editing;

            Debug.Log("[BugOneTouch] 버그 리포트 폼 표시");
        }

        /// <summary>
        /// 폼을 숨깁니다.
        /// </summary>
        public void HideForm()
        {
            _cancelSource?.Cancel();
            _state = FormState.Hidden;
        }

        // ─── Unity 생명주기 ───────────────────────────────────────────────────────

        private void OnDestroy()
        {
            // 플레이모드 종료 시 Cancel() 호출이 JiraApiClient의 CancellationToken 콜백을 즉시 실행하며,
            // 이 시점에 Unity 네이티브 오브젝트가 이미 파괴되어 있을 수 있습니다.
            // JiraApiClient 측에서도 try-catch로 보호하고 있으나,
            // 여기서도 AggregateException 등 예외를 안전하게 처리합니다.
            try
            {
                _cancelSource?.Cancel();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] OnDestroy Cancel 예외 무시: {ex.Message}");
            }
            finally
            {
                _cancelSource?.Dispose();
                _cancelSource = null;
            }
        }

        private void OnGUI()
        {
            if (_state == FormState.Hidden) return;

            // 스타일 지연 초기화
            if (!_stylesInitialized)
            {
                InitializeStyles();
                _stylesInitialized = true;
            }

            // 화면 중앙 좌표 계산
            float x = (Screen.width  - FormWidth)  * 0.5f;
            float y = (Screen.height - FormHeight)  * 0.5f;
            Rect windowRect = new Rect(x, y, FormWidth, FormHeight);

            // 반투명 배경 (화면 전체 딤)
            DrawDimBackground();

            // 폼 윈도우 렌더링
            GUI.Window(1001, windowRect, DrawFormWindow, "버그 리포트", _windowStyle);
        }

        // ─── 폼 윈도우 내용 ───────────────────────────────────────────────────────

        private void DrawFormWindow(int id)
        {
            switch (_state)
            {
                case FormState.Editing:
                    DrawEditingContent();
                    break;
                case FormState.Submitting:
                    DrawSubmittingContent();
                    break;
                case FormState.Result:
                    DrawResultContent();
                    break;
            }
        }

        // ─── 편집 화면 ────────────────────────────────────────────────────────────

        private void DrawEditingContent()
        {
            float contentHeight = FormHeight - 40f; // 타이틀 바 높이 제외
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(contentHeight - 60f));

            // 제목 (필수)
            DrawFieldLabel("제목 *");
            _title = GUILayout.TextField(_title, _textFieldStyle);

            GUILayout.Space(8f);

            // 이슈 타입 / 우선순위 선택 (좌우 화살표 순환)
            GUILayout.BeginHorizontal();

            GUILayout.Label("이슈 타입:", _labelStyle, GUILayout.Width(70f));
            if (GUILayout.Button("<", _buttonStyle, GUILayout.Width(25f)))
                _issueTypeIndex = (_issueTypeIndex - 1 + _activeIssueTypes.Length) % _activeIssueTypes.Length;
            GUILayout.Label(_activeIssueTypes[_issueTypeIndex], GUI.skin.box, GUILayout.MinWidth(80f));
            if (GUILayout.Button(">", _buttonStyle, GUILayout.Width(25f)))
                _issueTypeIndex = (_issueTypeIndex + 1) % _activeIssueTypes.Length;

            GUILayout.Space(16f);

            GUILayout.Label("우선순위:", _labelStyle, GUILayout.Width(60f));
            if (GUILayout.Button("<", _buttonStyle, GUILayout.Width(25f)))
                _priorityIndex = (_priorityIndex - 1 + _activePriorities.Length) % _activePriorities.Length;
            GUILayout.Label(_activePriorities[_priorityIndex], GUI.skin.box, GUILayout.MinWidth(70f));
            if (GUILayout.Button(">", _buttonStyle, GUILayout.Width(25f)))
                _priorityIndex = (_priorityIndex + 1) % _activePriorities.Length;

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            // 설명
            DrawFieldLabel("설명");
            _description = GUILayout.TextArea(
                _description,
                _textAreaStyle,
                GUILayout.Height(LineHeight * TextAreaLines));

            GUILayout.Space(8f);

            // 재현 단계
            DrawFieldLabel("재현 단계");
            _stepsToReproduce = GUILayout.TextArea(
                _stepsToReproduce,
                _textAreaStyle,
                GUILayout.Height(LineHeight * TextAreaLines));

            GUILayout.Space(8f);

            // 영상 포함 토글
            _includeVideo = GUILayout.Toggle(_includeVideo, "  영상 포함 (링 버퍼 60초)", GUILayout.Height(22f));

            GUILayout.EndScrollView();

            DrawSeparator();

            bool hasTitle = !string.IsNullOrWhiteSpace(_title);
            bool isWebLoggedIn = _supabaseAuthClient != null && _supabaseAuthClient.IsLoggedIn;
            bool canJira = hasTitle && isWebLoggedIn
                           && (_licenseValidator == null || _licenseValidator.CanSubmitToJira())
                           && _jiraService != null;

            // 버튼 영역
            GUILayout.BeginHorizontal();

            // 취소 버튼
            if (GUILayout.Button("취소", _buttonStyle, GUILayout.Width(60f), GUILayout.Height(30f)))
            {
                HideForm();
            }

            GUILayout.FlexibleSpace();

            // 로컬 저장 버튼
            if (GUILayout.Button("로컬 저장", _buttonStyle, GUILayout.Width(80f), GUILayout.Height(30f)))
            {
                _ = SaveLocalAsync();
            }

            GUILayout.Space(4f);

            // 웹 저장 버튼
            if (_reportSubmitter != null)
            {
                bool canWeb = hasTitle && isWebLoggedIn;
                using (new GUIDisabledScope(!canWeb))
                {
                    Color origBg = GUI.backgroundColor;
                    GUI.backgroundColor = canWeb ? new Color(0.2f, 0.7f, 0.5f) : Color.gray;
                    if (GUILayout.Button("웹 저장", _buttonStyle, GUILayout.Width(80f), GUILayout.Height(30f)))
                    {
                        _ = SaveToWebAsync();
                    }
                    GUI.backgroundColor = origBg;
                }
            }

            GUILayout.Space(4f);

            // Jira 등록 버튼
            using (new GUIDisabledScope(!canJira))
            {
                Color original = GUI.backgroundColor;
                GUI.backgroundColor = canJira ? new Color(0.2f, 0.6f, 0.9f) : Color.gray;
                if (GUILayout.Button("Jira 등록", _buttonStyle, GUILayout.Width(90f), GUILayout.Height(30f)))
                {
                    if (_reportSubmitter != null)
                        _ = SubmitToJiraWithWebAsync();
                    else
                        _ = SubmitToJiraAsync();
                }
                GUI.backgroundColor = original;
            }

            GUILayout.EndHorizontal();

            // 안내 텍스트
            if (!hasTitle)
                GUILayout.Label("* 제목은 필수입니다.", _errorStyle);
            else if (!isWebLoggedIn && _reportSubmitter != null)
                GUILayout.Label("웹 로그인이 필요합니다. (Settings > Supabase)", _labelStyle);
            else if (_licenseValidator != null && !_licenseValidator.CanSubmitToJira())
                GUILayout.Label("Jira 등록은 유료 플랜이 필요합니다.", _labelStyle);
        }

        // ─── 제출 중 화면 ─────────────────────────────────────────────────────────

        private void DrawSubmittingContent()
        {
            GUILayout.FlexibleSpace();

            // 진행 표시
            GUILayout.Label(_submitStageText, _labelStyle);
            GUILayout.Space(8f);

            Rect barRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            DrawProgressBar(barRect, _submitProgress);

            GUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("취소", _buttonStyle, GUILayout.Width(80f), GUILayout.Height(28f)))
            {
                _cancelSource?.Cancel();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
        }

        // ─── 결과 화면 ────────────────────────────────────────────────────────────

        private void DrawResultContent()
        {
            GUILayout.FlexibleSpace();

            if (_resultSuccess)
            {
                bool isJiraIssue = !string.IsNullOrEmpty(_resultIssueKey) && _resultIssueKey.Contains("-");
                bool isWebSaved = !string.IsNullOrEmpty(_resultWebUrl);

                if (isJiraIssue)
                {
                    GUILayout.Label("Jira 이슈가 생성되었습니다.", _successStyle);
                    GUILayout.Space(8f);
                    GUILayout.Label($"이슈 키: {_resultIssueKey}", _titleStyle);
                }
                else if (isWebSaved)
                {
                    GUILayout.Label("버그 리포트가 웹에 저장되었습니다.", _successStyle);
                    GUILayout.Space(8f);
                    if (!string.IsNullOrEmpty(_resultMessage))
                        GUILayout.Label(_resultMessage, _labelStyle);
                }
                else
                {
                    GUILayout.Label("버그 리포트가 저장되었습니다.", _successStyle);
                    GUILayout.Space(8f);
                    if (!string.IsNullOrEmpty(_resultMessage))
                        GUILayout.Label(_resultMessage, _titleStyle);
                }
            }
            else
            {
                GUILayout.Label("오류가 발생했습니다.", _errorStyle);
                GUILayout.Space(8f);
                GUILayout.Label(_resultMessage, _labelStyle);
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();

            if (!string.IsNullOrEmpty(_resultWebUrl))
            {
                if (GUILayout.Button("웹에서 보기", GUILayout.Height(35)))
                {
                    Application.OpenURL(_resultWebUrl);
                }
            }

            if (!string.IsNullOrEmpty(_resultIssueUrl))
            {
                if (GUILayout.Button("이슈로 이동", GUILayout.Height(35)))
                {
                    Application.OpenURL(_resultIssueUrl);
                }
            }

            if (GUILayout.Button("닫기", _buttonStyle, GUILayout.Height(35)))
            {
                HideForm();
            }

            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
        }

        // ─── 비동기 제출 로직 ─────────────────────────────────────────────────────

        /// <summary>
        /// Jira에 버그 이슈를 제출합니다.
        /// </summary>
        private async Task SubmitToJiraAsync()
        {
            if (_jiraService == null)
            {
                ShowResult(false, "", "JiraSubmissionService가 설정되지 않았습니다.");
                return;
            }

            _state          = FormState.Submitting;
            _submitProgress = 0f;
            _submitStageText = "번들 저장 중...";

            _cancelSource?.Cancel();
            _cancelSource?.Dispose();
            _cancelSource = new CancellationTokenSource();

            try
            {
                // 1단계: 번들 저장 (아직 저장되지 않은 경우)
                if (_bundle == null && _bundleWriter != null && _captureResult != null)
                {
                    _bundle = await _bundleWriter.WriteAsync(_captureResult);
                    // 사용자 입력 반영
                    _bundle.title       = _title;
                    _bundle.description = _description;
                }

                _submitProgress  = 0.2f;
                _submitStageText = "Jira 이슈 제출 중...";

                // 2단계: Jira 제출 요청 구성
                var request  = new JiraSubmissionService.SubmissionRequest
                {
                    BundleId = _bundle?.id ?? "unknown",
                    IssueRequest = new JiraIssueCreator.CreateIssueRequest
                    {
                        ProjectKey  = _settings?.jiraProjectKey ?? "",
                        Summary     = _title,
                        Description = BuildDescription(),
                        IssueType   = _activeIssueTypes[_issueTypeIndex],
                        Priority    = _activePriorities[_priorityIndex],
                    },
                };

                // 첨부파일 목록 구성
                if (_captureResult != null)
                {
                    BuildAttachments(request, _captureResult);
                }

                // 3단계: Jira 제출 이벤트 구독
                _jiraService.OnProgressChanged += HandleSubmitProgress;
                JiraSubmissionService.SubmissionResult result;
                try
                {
                    result = await _jiraService.SubmitAsync(request, _cancelSource.Token);
                }
                finally
                {
                    _jiraService.OnProgressChanged -= HandleSubmitProgress;
                }

                if (result.Success)
                {
                    // integrations.jira 갱신 (하위 호환을 위해 jira_issue_key도 함께 설정)
                    if (_bundle != null)
                    {
                        _bundle.jira_issue_key = result.IssueKey;
                        if (_bundle.integrations == null)
                            _bundle.integrations = new BundleIntegrations();
                        _bundle.integrations.jira.issueKey  = result.IssueKey;
                        _bundle.integrations.jira.connected = true;
                    }

                    ShowResult(true, result.IssueKey, "", result.IssueUrl);
                    Debug.Log($"[BugOneTouch] Jira 이슈 생성 완료: {result.IssueKey}");
                }
                else
                {
                    ShowResult(false, "", result.ErrorMessage ?? "알 수 없는 오류");
                }
            }
            catch (System.OperationCanceledException)
            {
                _state           = FormState.Editing;
                _submitProgress  = 0f;
                _submitStageText = "";
                Debug.Log("[BugOneTouch] Jira 제출 취소됨");
            }
            catch (System.Exception ex)
            {
                ShowResult(false, "", $"제출 실패: {ex.Message}");
                Debug.LogError($"[BugOneTouch] Jira 제출 오류: {ex}");
            }
        }

        /// <summary>
        /// 캡처 결과를 번들로 로컬 저장합니다 (Jira 제출 없이).
        /// </summary>
        private async Task SaveLocalAsync()
        {
            if (_bundleWriter == null)
            {
                ShowResult(false, "", "BundleWriter가 설정되지 않았습니다.");
                return;
            }

            _state           = FormState.Submitting;
            _submitProgress  = 0.3f;
            _submitStageText = "번들 저장 중...";

            try
            {
                if (_captureResult != null)
                {
                    _bundle = await _bundleWriter.WriteAsync(_captureResult);
                    _bundle.title       = _title;
                    _bundle.description = _description;
                }

                _submitProgress  = 1.0f;
                _submitStageText = "저장 완료";

                ShowResult(true, "로컬 저장됨", "번들이 로컬에 저장되었습니다.");
                Debug.Log($"[BugOneTouch] 번들 로컬 저장 완료: {_bundle?.id}");
            }
            catch (System.Exception ex)
            {
                ShowResult(false, "", $"로컬 저장 실패: {ex.Message}");
                Debug.LogError($"[BugOneTouch] 번들 저장 오류: {ex}");
            }
        }

        // ─── 웹 저장 / Jira 등록 (R2 경로) ──────────────────────────────────────

        /// <summary>
        /// [웹 저장] 경로: create-report → R2 업로드만 수행합니다.
        /// </summary>
        private async Task SaveToWebAsync()
        {
            if (_reportSubmitter == null)
            {
                ShowResult(false, "", "ReportSubmitter가 설정되지 않았습니다.");
                return;
            }

            _state = FormState.Submitting;
            _submitProgress = 0f;
            _submitStageText = "웹 저장 중...";

            _cancelSource?.Cancel();
            _cancelSource?.Dispose();
            _cancelSource = new CancellationTokenSource();

            try
            {
                var request = BuildWebSubmitRequest();
                _reportSubmitter.OnProgressChanged += HandleSubmitProgress;
                ReportSubmitter.SubmitResult result;
                try
                {
                    result = await _reportSubmitter.SubmitAsync(request, _cancelSource.Token);
                }
                finally
                {
                    _reportSubmitter.OnProgressChanged -= HandleSubmitProgress;
                }

                if (result.Success)
                {
                    _resultWebUrl = result.WorkspaceUrl ?? "";
                    ShowResult(true, "", "웹 대시보드에서 확인하세요.");
                    Debug.Log($"[BugOneTouch] 웹 저장 완료: {result.ReportId}");
                }
                else
                {
                    ShowResult(false, "", result.ErrorMessage ?? "웹 저장 실패");
                }
            }
            catch (OperationCanceledException)
            {
                _state = FormState.Editing;
                _submitProgress = 0f;
                _submitStageText = "";
                Debug.Log("[BugOneTouch] 웹 저장 취소됨");
            }
            catch (Exception ex)
            {
                ShowResult(false, "", $"웹 저장 실패: {ex.Message}");
                Debug.LogError($"[BugOneTouch] 웹 저장 오류: {ex}");
            }
        }

        /// <summary>
        /// [Jira 등록] 경로: create-report → R2 업로드 → Jira 이슈 생성.
        /// R2에 저장 후 기존 Jira 제출 플로우를 실행합니다.
        /// </summary>
        private async Task SubmitToJiraWithWebAsync()
        {
            if (_reportSubmitter == null || _jiraService == null)
            {
                ShowResult(false, "", "ReportSubmitter 또는 JiraService가 설정되지 않았습니다.");
                return;
            }

            _state = FormState.Submitting;
            _submitProgress = 0f;
            _submitStageText = "웹 저장 중...";

            _cancelSource?.Cancel();
            _cancelSource?.Dispose();
            _cancelSource = new CancellationTokenSource();

            try
            {
                // 1단계: R2 업로드 (웹 저장)
                var webRequest = BuildWebSubmitRequest();
                _reportSubmitter.OnProgressChanged += HandleWebThenJiraProgress;
                ReportSubmitter.SubmitResult webResult;
                try
                {
                    webResult = await _reportSubmitter.SubmitAsync(webRequest, _cancelSource.Token);
                }
                finally
                {
                    _reportSubmitter.OnProgressChanged -= HandleWebThenJiraProgress;
                }

                if (!webResult.Success)
                {
                    ShowResult(false, "", webResult.ErrorMessage ?? "웹 저장 실패 (Jira 등록 중단)");
                    return;
                }

                _resultWebUrl = webResult.WorkspaceUrl ?? "";

                // 2단계: 번들 저장 (로컬)
                _submitProgress = 0.5f;
                _submitStageText = "번들 저장 중...";

                if (_bundle == null && _bundleWriter != null && _captureResult != null)
                {
                    _bundle = await _bundleWriter.WriteAsync(_captureResult);
                    _bundle.title = _title;
                    _bundle.description = _description;
                }

                // 3단계: Jira 이슈 제출
                _submitProgress = 0.6f;
                _submitStageText = "Jira 이슈 생성 중...";

                // R2 URL이 있으면 description에 링크를 포함하고, 직접 첨부 업로드를 건너뜀
                bool hasR2Urls = webResult.FileUrls != null && webResult.FileUrls.Count > 0;

                var jiraRequest = new JiraSubmissionService.SubmissionRequest
                {
                    BundleId = _bundle?.id ?? "unknown",
                    UseR2Links = hasR2Urls,
                    IssueRequest = new JiraIssueCreator.CreateIssueRequest
                    {
                        ProjectKey = _settings?.jiraProjectKey ?? "",
                        Summary = _title,
                        Description = BuildDescription(),
                        IssueType = _activeIssueTypes[_issueTypeIndex],
                        Priority = _activePriorities[_priorityIndex],
                        R2Urls = webResult.FileUrls,
                    },
                };

                // R2 URL이 없는 경우에만 기존 직접 첨부 방식 사용
                if (!hasR2Urls && _captureResult != null)
                    BuildAttachments(jiraRequest, _captureResult);

                _jiraService.OnProgressChanged += HandleJiraPhaseProgress;
                JiraSubmissionService.SubmissionResult jiraResult;
                try
                {
                    jiraResult = await _jiraService.SubmitAsync(jiraRequest, _cancelSource.Token);
                }
                finally
                {
                    _jiraService.OnProgressChanged -= HandleJiraPhaseProgress;
                }

                if (jiraResult.Success)
                {
                    if (_bundle != null)
                    {
                        _bundle.jira_issue_key = jiraResult.IssueKey;
                        if (_bundle.integrations == null)
                            _bundle.integrations = new BundleIntegrations();
                        _bundle.integrations.jira.issueKey = jiraResult.IssueKey;
                        _bundle.integrations.jira.connected = true;
                    }

                    ShowResult(true, jiraResult.IssueKey, "", jiraResult.IssueUrl);
                    Debug.Log($"[BugOneTouch] Jira 이슈 생성 완료 (웹 저장 포함): {jiraResult.IssueKey}");
                }
                else
                {
                    ShowResult(false, "", jiraResult.ErrorMessage ?? "Jira 등록 실패");
                }
            }
            catch (OperationCanceledException)
            {
                _state = FormState.Editing;
                _submitProgress = 0f;
                _submitStageText = "";
                Debug.Log("[BugOneTouch] Jira 등록 (웹 경로) 취소됨");
            }
            catch (Exception ex)
            {
                ShowResult(false, "", $"Jira 등록 실패: {ex.Message}");
                Debug.LogError($"[BugOneTouch] Jira 등록 (웹 경로) 오류: {ex}");
            }
        }

        /// <summary>
        /// CaptureResult를 ReportSubmitter.SubmitRequest로 변환합니다.
        /// </summary>
        private ReportSubmitter.SubmitRequest BuildWebSubmitRequest()
        {
            var request = new ReportSubmitter.SubmitRequest
            {
                // TODO: 실제 workspace_id는 SupabaseAuthClient의 캐시된 값 사용
                WorkspaceId = "",
                Title = _title,
                Description = BuildDescription(),
            };

            // 환경 정보 JSON
            if (_captureResult?.StateSnapshot != null)
            {
                request.DeviceInfoJson = JsonUtility.ToJson(_captureResult.StateSnapshot);
            }

            // 파일 목록 구성
            if (_captureResult != null)
            {
                TryAddWebFile(request, _captureResult.ScreenshotPath, "screenshot", "screenshot.png", "image/png");
                TryAddWebFile(request, _captureResult.LogsPath, "log", "logs.zip", "application/zip");

                if (_includeVideo && !string.IsNullOrEmpty(_captureResult.VideoPath)
                    && System.IO.File.Exists(_captureResult.VideoPath))
                {
                    TryAddWebFile(request, _captureResult.VideoPath, "video",
                        System.IO.Path.GetFileName(_captureResult.VideoPath), "video/mp4");
                }
            }

            return request;
        }

        private static void TryAddWebFile(
            ReportSubmitter.SubmitRequest request,
            string filePath, string type, string fileName, string contentType)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return;

            try
            {
                var fileInfo = new System.IO.FileInfo(filePath);
                request.Files.Add(new ReportSubmitter.FileEntry
                {
                    Type = type,
                    FileName = fileName,
                    LocalPath = filePath,
                    ContentType = contentType,
                    FileSize = fileInfo.Length
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 웹 파일 정보 생성 실패: {filePath}\n{ex.Message}");
            }
        }

        private void HandleWebThenJiraProgress(float progress, string message)
        {
            // 웹 저장 단계: 전체의 0 ~ 0.5 범위
            _submitProgress = progress * 0.5f;
            _submitStageText = message;
        }

        private void HandleJiraPhaseProgress(float progress, string message)
        {
            // Jira 제출 단계: 전체의 0.6 ~ 1.0 범위
            _submitProgress = 0.6f + progress * 0.4f;
            _submitStageText = message;
        }

        // ─── 헬퍼 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 결과 화면으로 전환합니다.
        /// </summary>
        /// <param name="success">성공 여부</param>
        /// <param name="issueKey">생성된 이슈 키 (예: PROJ-123)</param>
        /// <param name="message">오류 메시지 (실패 시)</param>
        /// <param name="fallbackIssueUrl">이슈 URL 폴백 (jiraSiteUrl이 비어있을 때 사용)</param>
        private void ShowResult(bool success, string issueKey, string message, string fallbackIssueUrl = "")
        {
            _resultSuccess  = success;
            _resultIssueKey = issueKey;
            _resultMessage  = message;
            _resultIssueUrl = "";

            // 성공 시 실제 Jira 이슈 키(PROJ-123 형태)인 경우에만 URL 생성
            if (success && !string.IsNullOrEmpty(issueKey) && issueKey.Contains("-"))
            {
                string siteUrl = _settings?.jiraSiteUrl ?? "";
                if (!string.IsNullOrEmpty(siteUrl))
                {
                    // Settings에 저장된 사이트 URL로 browse URL 생성 (사용자가 직접 열 수 있는 URL)
                    _resultIssueUrl = siteUrl.TrimEnd('/') + "/browse/" + issueKey;
                }
                else if (!string.IsNullOrEmpty(fallbackIssueUrl))
                {
                    // jiraSiteUrl이 비어있으면 제출 응답의 self URL을 폴백으로 사용
                    // (Jira REST API self URL: https://api.atlassian.com/ex/jira/{cloudId}/rest/api/3/issue/{id})
                    _resultIssueUrl = fallbackIssueUrl;
                    Debug.LogWarning("[BugOneTouch] jiraSiteUrl이 비어있어 API self URL을 이슈 링크로 사용합니다. " +
                                     "Settings > Jira 탭에서 'Jira 사이트 URL'을 직접 설정하거나, " +
                                     "프로젝트 조회 버튼을 누르면 자동으로 설정됩니다.");
                }
            }

            _state = FormState.Result;
        }

        /// <summary>
        /// 설명과 재현 단계를 합쳐 Jira용 설명 문자열을 구성합니다.
        /// </summary>
        private string BuildDescription()
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(_description))
            {
                sb.AppendLine(_description);
            }

            if (!string.IsNullOrEmpty(_stepsToReproduce))
            {
                sb.AppendLine();
                sb.AppendLine("재현 단계:");
                sb.AppendLine(_stepsToReproduce);
            }

            return sb.ToString();
        }

        /// <summary>
        /// CaptureResult를 기반으로 첨부파일 목록을 구성합니다.
        /// AttachmentItem은 FileName + Data(바이트) + ContentType 구조입니다.
        /// </summary>
        private void BuildAttachments(
            JiraSubmissionService.SubmissionRequest request,
            CaptureResult captureResult)
        {
            TryAddFileAttachment(request, captureResult.ScreenshotPath,  "screenshot.png",  "image/png");
            TryAddFileAttachment(request, captureResult.LogsPath,         "logs.zip",         "application/zip");
            TryAddFileAttachment(request, captureResult.StatePath,        "state.json",       "application/json");

            if (_includeVideo && !string.IsNullOrEmpty(captureResult.VideoPath))
            {
                if (System.IO.File.Exists(captureResult.VideoPath))
                {
                    // MP4 단일 파일 - 직접 첨부
                    TryAddFileAttachment(request, captureResult.VideoPath,
                        System.IO.Path.GetFileName(captureResult.VideoPath), "video/mp4");
                    Debug.Log($"[BugOneTouch] MP4 파일 첨부 준비 완료: {captureResult.VideoPath}");
                }
                else if (System.IO.Directory.Exists(captureResult.VideoPath))
                {
                    // raw 프레임 디렉토리 - ZIP 압축 후 첨부 (기존 로직 유지)
                    try
                    {
                        byte[] zipData;
                        using (var ms = new System.IO.MemoryStream())
                        {
                            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                            {
                                foreach (var filePath in System.IO.Directory.GetFiles(captureResult.VideoPath))
                                {
                                    var entry = archive.CreateEntry(System.IO.Path.GetFileName(filePath), System.IO.Compression.CompressionLevel.Fastest);
                                    using (var entryStream = entry.Open())
                                    {
                                        var fileBytes = System.IO.File.ReadAllBytes(filePath);
                                        entryStream.Write(fileBytes, 0, fileBytes.Length);
                                    }
                                }
                            }
                            zipData = ms.ToArray();
                        }

#pragma warning disable CS0618
                        request.Attachments.Add(new JiraAttachmentUploader.AttachmentItem
                        {
                            FileName    = "video_frames.zip",
                            Data        = zipData,
                            ContentType = "application/zip",
                        });
#pragma warning restore CS0618

                        Debug.Log($"[BugOneTouch] 영상 프레임 ZIP 첨부 준비 완료 ({zipData.Length / 1024}KB)");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[BugOneTouch] 영상 ZIP 압축 실패: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 파일 경로로부터 AttachmentItem을 생성하여 요청에 추가합니다.
        /// 파일이 없거나 읽을 수 없으면 건너뜁니다.
        /// </summary>
        private static void TryAddFileAttachment(
            JiraSubmissionService.SubmissionRequest request,
            string filePath,
            string fileName,
            string contentType)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            if (!System.IO.File.Exists(filePath)) return;

            try
            {
                byte[] data = System.IO.File.ReadAllBytes(filePath);
#pragma warning disable CS0618
                request.Attachments.Add(new JiraAttachmentUploader.AttachmentItem
                {
                    FileName    = fileName,
                    Data        = data,
                    ContentType = contentType,
                });
#pragma warning restore CS0618
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] 첨부파일 읽기 실패: {filePath}\n{ex.Message}");
            }
        }

        /// <summary>
        /// Jira 제출 진행 이벤트 핸들러.
        /// </summary>
        private void HandleSubmitProgress(float progress, string message)
        {
            _submitProgress  = progress;
            _submitStageText = message;
        }

        /// <summary>
        /// 화면 전체를 반투명하게 딤(dim) 처리합니다.
        /// </summary>
        private static void DrawDimBackground()
        {
            Color original = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = original;
        }

        /// <summary>
        /// 지정 영역에 프로그레스 바를 그립니다.
        /// </summary>
        private static void DrawProgressBar(Rect rect, float progress)
        {
            Color original = GUI.color;

            // 배경
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // 채우기
            float fillWidth = Mathf.Max(2f, rect.width * Mathf.Clamp01(progress));
            GUI.color = new Color(0.2f, 0.7f, 0.9f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, fillWidth, rect.height), Texture2D.whiteTexture);

            GUI.color = original;
        }

        /// <summary>
        /// 수평 구분선을 그립니다.
        /// </summary>
        private static void DrawSeparator()
        {
            Rect r = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            Color original = GUI.color;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = original;
        }

        /// <summary>
        /// 필드 레이블을 그립니다.
        /// </summary>
        private void DrawFieldLabel(string label)
        {
            GUILayout.Label(label, _labelStyle);
        }

        /// <summary>
        /// IMGUI 스타일을 초기화합니다.
        /// </summary>
        private void InitializeStyles()
        {
            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset((int)Padding, (int)Padding, 24, (int)Padding),
            };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.9f, 0.9f, 0.9f) },
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal   = { textColor = new Color(0.85f, 0.85f, 0.85f) },
            };

            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 13,
                padding  = new RectOffset(6, 6, 4, 4),
            };

            _textAreaStyle = new GUIStyle(GUI.skin.textArea)
            {
                fontSize  = 12,
                padding   = new RectOffset(6, 6, 4, 4),
                wordWrap  = true,
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
            };

            _errorStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1f, 0.4f, 0.4f) },
            };

            _successStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.3f, 0.9f, 0.3f) },
            };
        }

        // ─── GUIDisabledScope 헬퍼 ────────────────────────────────────────────────

        /// <summary>
        /// GUI 비활성화 스코프 (using 패턴으로 사용).
        /// </summary>
        private struct GUIDisabledScope : System.IDisposable
        {
            public GUIDisabledScope(bool disabled)
            {
                GUI.enabled = !disabled;
            }

            public void Dispose()
            {
                GUI.enabled = true;
            }
        }
    }
}
