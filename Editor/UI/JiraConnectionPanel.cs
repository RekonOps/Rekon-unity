using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GaoZombie.BugOneTouch.Editor
{
    /// <summary>
    /// Jira OAuth 연결 상태를 표시하고 연결/해제 흐름을 제공하는 UI 패널.
    /// BugOneTouchSettingsWindow의 Jira 탭 내부에서 OnGUI()를 호출하여 사용합니다.
    ///
    /// 상태 전이:
    ///   Disconnected → Connecting → Connected
    ///   Connected    → Disconnecting → Disconnected
    ///   Connecting   → (취소) → Disconnected
    /// </summary>
    public class JiraConnectionPanel
    {
        // ─── 연결 상태 열거형 ─────────────────────────────────────────────────────

        private enum ConnectionState
        {
            /// <summary>연결되지 않은 초기 상태</summary>
            Disconnected,

            /// <summary>OAuth 플로우 진행 중 (브라우저 인증 대기)</summary>
            Connecting,

            /// <summary>토큰이 존재하여 연결된 상태</summary>
            Connected,
        }

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private ConnectionState _state = ConnectionState.Disconnected;
        private BugOneTouchSettings _settings;
        private SessionTokenStore _tokenStore;
        private OAuthFlowManager _oauthManager;
        private AuthBrokerClient _brokerClient;

        private string _statusMessage = "";
        private string _lastError = "";
        private float _connectProgress = 0f;

        // OAuth 플로우 취소 토큰
        private CancellationTokenSource _cancelSource;

        // EditorWindow 갱신을 위한 타이머
        private double _lastRepaintTime;

        // ─── 초기화 / 정리 ────────────────────────────────────────────────────────

        /// <summary>
        /// 패널에서 사용 중인 SessionTokenStore 인스턴스.
        /// BugOneTouchSettingsWindow의 메타데이터 서비스와 토큰 저장소를 공유할 때 사용합니다.
        /// </summary>
        public SessionTokenStore TokenStore => _tokenStore;

        /// <summary>
        /// 패널을 초기화합니다. BugOneTouchSettings를 주입받아 서비스를 생성합니다.
        /// </summary>
        public void Initialize(BugOneTouchSettings settings)
        {
            _settings = settings;
            _tokenStore = new SessionTokenStore();
            _brokerClient = new AuthBrokerClient(
                string.IsNullOrEmpty(settings.authBrokerUrl) ? "http://localhost" : settings.authBrokerUrl,
                _tokenStore);
            _oauthManager = new OAuthFlowManager(_brokerClient, _tokenStore);

            // OAuth 이벤트 구독
            _oauthManager.OnStatusChanged += OnOAuthStatusChanged;
            _oauthManager.OnCompleted      += OnOAuthCompleted;
            _oauthManager.OnFailed         += OnOAuthFailed;

            // 기존 토큰 확인
            RefreshConnectionState();
        }

        /// <summary>
        /// 패널 리소스를 정리합니다. OnDisable에서 호출해야 합니다.
        /// </summary>
        public void Cleanup()
        {
            if (_oauthManager != null)
            {
                _oauthManager.OnStatusChanged -= OnOAuthStatusChanged;
                _oauthManager.OnCompleted      -= OnOAuthCompleted;
                _oauthManager.OnFailed         -= OnOAuthFailed;
            }

            _cancelSource?.Cancel();
            _cancelSource?.Dispose();
            _cancelSource = null;
        }

        // ─── GUI 렌더링 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Jira 연결 패널 UI를 그립니다. EditorWindow.OnGUI에서 호출합니다.
        /// </summary>
        public void OnGUI()
        {
            // 비동기 콜백이 OnGUI 렌더링 도중 _state를 변경하면
            // Layout 이벤트와 Repaint 이벤트 사이에 Begin/End 쌍이 어긋나는
            // GUILayout 상태 에러가 발생합니다.
            // OnGUI 시작 시점에 상태를 스냅샷으로 캡처하여
            // 이 프레임 렌더링 내내 동일한 상태를 사용합니다.
            ConnectionState currentState = _state;

            DrawConnectionStatus(currentState);
            EditorGUILayout.Space(8f);
            DrawConnectionActions(currentState);
            EditorGUILayout.Space(8f);
            DrawErrorMessage();

            // 연결 중일 때 주기적 리페인트 (폴링 진행 상황 반영)
            if (currentState == ConnectionState.Connecting)
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - _lastRepaintTime > 0.5)
                {
                    _lastRepaintTime = now;
                    // 부모 윈도우 리페인트 요청
                    foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    {
                        if (w is BugOneTouchSettingsWindow)
                            w.Repaint();
                    }
                }
            }
        }

        // ─── 연결 상태 표시 ───────────────────────────────────────────────────────

        private void DrawConnectionStatus(ConnectionState currentState)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("연결 상태:", GUILayout.Width(70f));

                switch (currentState)
                {
                    case ConnectionState.Disconnected:
                        DrawStatusBadge("● 미연결", new Color(0.6f, 0.6f, 0.6f));
                        break;

                    case ConnectionState.Connecting:
                        DrawStatusBadge("◌ 연결 중...", new Color(0.9f, 0.7f, 0.1f));
                        break;

                    case ConnectionState.Connected:
                        DrawStatusBadge("● 연결됨", new Color(0.2f, 0.8f, 0.2f));
                        break;
                }
            }

            // 연결 중 진행 표시
            if (currentState == ConnectionState.Connecting && !string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(4f);
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(false, 18f),
                    _connectProgress,
                    _statusMessage);
            }

            // 연결됨 안내
            if (currentState == ConnectionState.Connected)
            {
                EditorGUILayout.HelpBox("Jira 토큰이 저장되어 있습니다.", MessageType.Info);
            }
        }

        // ─── 액션 버튼 ────────────────────────────────────────────────────────────

        private void DrawConnectionActions(ConnectionState currentState)
        {
            switch (currentState)
            {
                case ConnectionState.Disconnected:
                    DrawDisconnectedActions();
                    break;

                case ConnectionState.Connecting:
                    DrawConnectingActions();
                    break;

                case ConnectionState.Connected:
                    DrawConnectedActions();
                    break;
            }
        }

        private void DrawDisconnectedActions()
        {
            if (GUILayout.Button("Jira 연결 시작", GUILayout.Height(28f)))
            {
                StartOAuthFlow();
            }
        }

        private void DrawConnectingActions()
        {
            EditorGUILayout.HelpBox(
                "브라우저에서 Jira 계정으로 로그인한 후 인증을 완료해주세요.\n최대 5분 동안 대기합니다.",
                MessageType.Info);

            EditorGUILayout.Space(4f);

            if (GUILayout.Button("취소", GUILayout.Height(28f)))
            {
                CancelOAuthFlow();
            }
        }

        private void DrawConnectedActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("연결 테스트", GUILayout.Height(26f)))
                {
                    TestConnection();
                }

                GUILayout.Space(8f);

                // 연결 해제 버튼 (빨간색 강조)
                Color originalColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("연결 해제", GUILayout.Height(26f)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Jira 연결 해제",
                        "저장된 Jira 토큰을 삭제하시겠습니까?\n이후 다시 연결해야 Jira에 버그를 제출할 수 있습니다.",
                        "연결 해제",
                        "취소"))
                    {
                        DisconnectJira();
                    }
                }
                GUI.backgroundColor = originalColor;
            }
        }

        // ─── 오류 메시지 ──────────────────────────────────────────────────────────

        private void DrawErrorMessage()
        {
            if (!string.IsNullOrEmpty(_lastError))
            {
                EditorGUILayout.HelpBox(_lastError, MessageType.Error);

                if (GUILayout.Button("오류 지우기", GUILayout.Width(100f)))
                {
                    _lastError = "";
                }
            }
        }

        // ─── 상태 배지 ────────────────────────────────────────────────────────────

        private static void DrawStatusBadge(string text, Color color)
        {
            Color original = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
            GUI.contentColor = original;
        }

        // ─── OAuth 플로우 ─────────────────────────────────────────────────────────

        /// <summary>
        /// OAuth 연결 플로우를 시작합니다.
        /// </summary>
        private void StartOAuthFlow()
        {
            _lastError     = "";
            _statusMessage = "연결 시작 중...";
            _connectProgress = 0f;
            _state         = ConnectionState.Connecting;

            // Auth Broker URL이 변경되었을 수 있으므로 재생성
            _brokerClient = new AuthBrokerClient(
                string.IsNullOrEmpty(_settings.authBrokerUrl) ? "http://localhost" : _settings.authBrokerUrl,
                _tokenStore);

            if (_oauthManager != null)
            {
                _oauthManager.OnStatusChanged -= OnOAuthStatusChanged;
                _oauthManager.OnCompleted      -= OnOAuthCompleted;
                _oauthManager.OnFailed         -= OnOAuthFailed;
            }

            _oauthManager = new OAuthFlowManager(_brokerClient, _tokenStore);
            _oauthManager.OnStatusChanged += OnOAuthStatusChanged;
            _oauthManager.OnCompleted      += OnOAuthCompleted;
            _oauthManager.OnFailed         += OnOAuthFailed;

            _cancelSource?.Cancel();
            _cancelSource?.Dispose();
            _cancelSource = new CancellationTokenSource();

            // 비동기 OAuth 플로우 시작 (에디터 비동기 실행)
            // tenantId, userId는 Settings에서 가져오며, 비어있으면 자동 생성
            _settings.EnsureIdentityIds();
            string tenantId = _settings.tenantId;
            string userId   = _settings.userId;

            var task = _oauthManager.StartOAuthFlowAsync(tenantId, userId, _cancelSource.Token);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    foreach (var ex in t.Exception.InnerExceptions)
                    {
                        Debug.LogError($"[BugOneTouch] OAuth 플로우 오류: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }, TaskContinuationOptions.OnlyOnFaulted);

            Debug.Log("[BugOneTouch] Jira OAuth 플로우 시작");
        }

        /// <summary>
        /// OAuth 연결 플로우를 취소합니다.
        /// </summary>
        private void CancelOAuthFlow()
        {
            _cancelSource?.Cancel();
            _state         = ConnectionState.Disconnected;
            _statusMessage = "";
            _connectProgress = 0f;
            Debug.Log("[BugOneTouch] Jira OAuth 플로우 취소");
        }

        /// <summary>
        /// Jira 연결을 해제하고 저장된 토큰을 삭제합니다.
        /// </summary>
        private void DisconnectJira()
        {
            _tokenStore.Clear();
            _state         = ConnectionState.Disconnected;
            _statusMessage = "";
            _connectProgress = 0f;
            _lastError     = "";
            Debug.Log("[BugOneTouch] Jira 연결 해제 및 토큰 삭제");
        }

        /// <summary>
        /// 저장된 토큰으로 연결 테스트를 수행합니다.
        /// </summary>
        private void TestConnection()
        {
            try
            {
                string token = _tokenStore.Load();
                if (string.IsNullOrEmpty(token))
                {
                    EditorUtility.DisplayDialog("연결 테스트", "저장된 토큰이 없습니다.", "확인");
                    return;
                }

                EditorUtility.DisplayDialog(
                    "연결 테스트",
                    "토큰이 존재합니다.\n실제 API 연결 테스트를 위해서는 Jira API 호출이 필요합니다.",
                    "확인");
                Debug.Log("[BugOneTouch] Jira 연결 테스트: 토큰 확인 완료");
            }
            catch (System.Exception ex)
            {
                _lastError = $"연결 테스트 실패: {ex.Message}";
                Debug.LogError($"[BugOneTouch] Jira 연결 테스트 오류: {ex.Message}");
            }
        }

        // ─── 상태 조회 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 저장된 토큰 유무 및 만료 여부로 연결 상태를 갱신합니다.
        /// 토큰이 만료된 경우 자동으로 삭제하고 Disconnected 상태로 전환합니다.
        /// </summary>
        private void RefreshConnectionState()
        {
            if (_tokenStore == null) return;

            try
            {
                string token = _tokenStore.Load();
                if (string.IsNullOrEmpty(token))
                {
                    _state = ConnectionState.Disconnected;
                    return;
                }

                // 토큰 만료 여부 확인 (0초 여유 = 현재 시각 기준 엄격 검사)
                if (_tokenStore.IsExpired(0))
                {
                    Debug.LogWarning("[BugOneTouch] 세션 토큰이 만료되었습니다. 재연결이 필요합니다.");
                    _tokenStore.Clear();
                    _state = ConnectionState.Disconnected;
                }
                else
                {
                    _state = ConnectionState.Connected;
                }
            }
            catch
            {
                _state = ConnectionState.Disconnected;
            }
        }

        // ─── OAuth 이벤트 핸들러 ──────────────────────────────────────────────────

        private void OnOAuthStatusChanged(string message)
        {
            // 에디터 메인 스레드에서 UI 업데이트
            EditorApplication.delayCall += () =>
            {
                _statusMessage   = message;
                _connectProgress = Mathf.Min(_connectProgress + 0.1f, 0.9f);
                RepaintSettingsWindow();
            };
        }

        private void OnOAuthCompleted(string sessionToken, string siteUrl)
        {
            EditorApplication.delayCall += () =>
            {
                _state           = ConnectionState.Connected;
                _statusMessage   = "";
                _connectProgress = 1f;
                _lastError       = "";

                // OAuth 응답에서 받은 site_url을 Settings에 자동 설정
                if (!string.IsNullOrEmpty(siteUrl) && _settings != null)
                {
                    _settings.jiraSiteUrl = siteUrl.TrimEnd('/');
                    UnityEditor.EditorUtility.SetDirty(_settings);
                    Debug.Log($"[BugOneTouch] jiraSiteUrl 자동 설정: {_settings.jiraSiteUrl}");
                }

                Debug.Log("[BugOneTouch] Jira OAuth 연결 완료");
                RepaintSettingsWindow();
            };
        }

        private void OnOAuthFailed(string errorMessage)
        {
            EditorApplication.delayCall += () =>
            {
                _state           = ConnectionState.Disconnected;
                _statusMessage   = "";
                _connectProgress = 0f;
                _lastError       = $"연결 실패: {errorMessage}";
                Debug.LogError($"[BugOneTouch] Jira OAuth 실패: {errorMessage}");
                RepaintSettingsWindow();
            };
        }

        // ─── 헬퍼 ─────────────────────────────────────────────────────────────────

        private static void RepaintSettingsWindow()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w is BugOneTouchSettingsWindow)
                {
                    w.Repaint();
                    break;
                }
            }
        }
    }
}
