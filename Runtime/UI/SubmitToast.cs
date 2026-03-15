using System;
using System.IO;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// Silent Submit 완료 후 인게임 화면 우하단에 결과 알림 토스트를 표시하는 컴포넌트.
    ///
    /// 기능:
    ///   - 성공: 녹색 배경, "리포트가 저장되었습니다" + [리포트로 이동] [닫기]
    ///   - 로컬 저장: 노란색 배경, "오프라인 저장됨 (로그인 후 자동 업로드)" + [닫기]
    ///   - 실패: 빨간색 배경, "웹 저장 실패하여 로컬에 저장" + [이동] [닫기] (pending 폴더 열기)
    ///   - IMGUI (OnGUI) 기반으로 UI Toolkit / uGUI 의존 없이 동작
    ///
    /// 사용법:
    ///   SubmitToast toast = SubmitToast.EnsureInstance();
    ///   toast.BindSilentSubmitManager(silentSubmitManager);
    /// </summary>
    [DisallowMultipleComponent]
    public class SubmitToast : MonoBehaviour
    {
        // ─── 상수 ─────────────────────────────────────────────────────────────────

        /// <summary>토스트 박스 너비 (픽셀)</summary>
        private const float ToastWidth = 300f;

        /// <summary>토스트 박스 높이 (픽셀)</summary>
        private const float ToastHeight = 80f;

        /// <summary>토스트 박스 여백 (픽셀)</summary>
        private const float ToastMargin = 16f;

        /// <summary>토스트 표시 시간 (초)</summary>
        private const float DisplayDuration = 5f;

        /// <summary>페이드 인 시간 (초)</summary>
        private const float FadeInDuration = 0.3f;

        /// <summary>페이드 아웃 시간 (초)</summary>
        private const float FadeOutDuration = 0.5f;

        // ─── 상태 ─────────────────────────────────────────────────────────────────

        /// <summary>토스트 표시 상태</summary>
        private enum ToastState
        {
            Hidden,
            FadingIn,
            Visible,
            FadingOut,
        }

        /// <summary>토스트 결과 유형</summary>
        private enum ToastType
        {
            Success,
            LocalSave,
            Failure,
        }

        private ToastState _state = ToastState.Hidden;
        private ToastType _toastType = ToastType.Success;
        private float _stateStartTime;
        private float _currentAlpha;
        private string _message = "";
        private string _reportUrl = "";

        // ─── GUI 스타일 캐시 ──────────────────────────────────────────────────────

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private bool _stylesInitialized;

        // ─── 바인딩 ───────────────────────────────────────────────────────────────

        private SilentSubmitManager _submitManager;

        // ─── 싱글톤 캐시 ─────────────────────────────────────────────────────────

        private static SubmitToast _instance;

        // ─── 정적 팩토리 ──────────────────────────────────────────────────────────

        /// <summary>
        /// SubmitToast 인스턴스를 반환합니다. 없으면 생성합니다.
        /// static 캐시를 사용하여 FindObjectOfType 호출을 최소화합니다.
        /// DontDestroyOnLoad로 씬 전환 시에도 유지됩니다.
        /// </summary>
        public static SubmitToast EnsureInstance()
        {
            if (_instance != null) return _instance;

            _instance = FindObjectOfType<SubmitToast>();
            if (_instance != null) return _instance;

            GameObject go = new GameObject("[BugBeacon] SubmitToast");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SubmitToast>();
            return _instance;
        }

        // ─── 공개 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// SilentSubmitManager의 OnSubmitCompleted 이벤트를 구독합니다.
        /// </summary>
        /// <param name="manager">연동할 SilentSubmitManager</param>
        public void BindSilentSubmitManager(SilentSubmitManager manager)
        {
            // 기존 바인딩 해제
            if (_submitManager != null)
            {
                _submitManager.OnSubmitCompleted -= HandleSubmitCompleted;
            }

            _submitManager = manager;

            if (_submitManager != null)
            {
                _submitManager.OnSubmitCompleted += HandleSubmitCompleted;
                Debug.Log("[BugBeacon] SubmitToast: SilentSubmitManager 바인딩 완료");
            }
        }

        /// <summary>
        /// 토스트를 즉시 닫습니다.
        /// </summary>
        public void Hide()
        {
            _state = ToastState.Hidden;
            _currentAlpha = 0f;
        }

        // ─── Unity 생명주기 ───────────────────────────────────────────────────────

        private void Awake()
        {
            // 싱글톤 캐시 등록 (중복 인스턴스 방지)
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[BugBeacon] SubmitToast: 중복 인스턴스 감지, 제거합니다");
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_submitManager != null)
            {
                _submitManager.OnSubmitCompleted -= HandleSubmitCompleted;
            }

            // 싱글톤 캐시 해제
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _stateStartTime;

            switch (_state)
            {
                case ToastState.FadingIn:
                    _currentAlpha = Mathf.Clamp01(elapsed / FadeInDuration);
                    if (elapsed >= FadeInDuration)
                    {
                        _currentAlpha = 1f;
                        _state = ToastState.Visible;
                        _stateStartTime = now;
                    }
                    break;

                case ToastState.Visible:
                    _currentAlpha = 1f;
                    if (elapsed >= DisplayDuration)
                    {
                        _state = ToastState.FadingOut;
                        _stateStartTime = now;
                    }
                    break;

                case ToastState.FadingOut:
                    _currentAlpha = 1f - Mathf.Clamp01(elapsed / FadeOutDuration);
                    if (elapsed >= FadeOutDuration)
                    {
                        Hide();
                    }
                    break;
            }
        }

        private void OnGUI()
        {
            if (_state == ToastState.Hidden) return;

            // 스타일 지연 초기화 (OnGUI에서만 GUI.skin에 접근 가능)
            if (!_stylesInitialized)
            {
                InitializeStyles();
                _stylesInitialized = true;
            }

            DrawToast();
        }

        // ─── 이벤트 핸들러 ────────────────────────────────────────────────────────

        /// <summary>
        /// SilentSubmitManager.OnSubmitCompleted 이벤트 핸들러.
        /// </summary>
        private void HandleSubmitCompleted(bool success, string reportIdOrMessage)
        {
            if (success)
            {
                // "로컬 저장 완료" 문자열이 포함되면 로컬 저장으로 판별
                if (reportIdOrMessage != null && reportIdOrMessage.StartsWith("로컬 저장 완료"))
                {
                    _toastType = ToastType.LocalSave;
                    _message = "오프라인 저장됨 (로그인 후 자동 업로드)";
                    _reportUrl = "";
                }
                else
                {
                    _toastType = ToastType.Success;
                    _message = "리포트가 저장되었습니다";

                    // 웹 대시보드 URL 구성 (보안 검증 포함, 상수 URL 사용)
                    _reportUrl = BuildSecureReportUrl(BugBeaconSettings.WEB_DASHBOARD_URL, reportIdOrMessage);
                }
            }
            else
            {
                _toastType = ToastType.Failure;
                _message = "웹 저장 실패하여 로컬에 저장";
                _reportUrl = "";
            }

            // 페이드 인 시작
            _state = ToastState.FadingIn;
            _stateStartTime = Time.realtimeSinceStartup;
            _currentAlpha = 0f;

            Debug.Log($"[BugBeacon] SubmitToast: 표시 (유형={_toastType}, 메시지={_message})");
        }

        // ─── 렌더링 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 우하단에 토스트를 그립니다.
        /// </summary>
        private void DrawToast()
        {
            Color originalColor = GUI.color;
            Color originalBgColor = GUI.backgroundColor;

            // 위치 계산: 우하단
            float x = Screen.width - ToastWidth - ToastMargin;
            float y = Screen.height - ToastHeight - ToastMargin;
            Rect boxRect = new Rect(x, y, ToastWidth, ToastHeight);

            // 배경색 결정
            Color bgColor = _toastType switch
            {
                ToastType.Success   => new Color(0.1f, 0.5f, 0.15f, 0.9f * _currentAlpha),
                ToastType.LocalSave => new Color(0.6f, 0.5f, 0.0f, 0.9f * _currentAlpha),
                ToastType.Failure   => new Color(0.6f, 0.12f, 0.1f, 0.9f * _currentAlpha),
                _                   => new Color(0f, 0f, 0f, 0.75f * _currentAlpha),
            };

            // 배경 박스
            GUI.color = bgColor;
            GUI.Box(boxRect, GUIContent.none, _boxStyle);
            GUI.color = originalColor;

            float padding = 10f;
            float innerX = x + padding;
            float innerW = ToastWidth - padding * 2f;

            // 메시지 텍스트
            Color textColor = new Color(1f, 1f, 1f, _currentAlpha);
            _labelStyle.normal.textColor = textColor;
            Rect labelRect = new Rect(innerX, y + 10f, innerW, 30f);
            GUI.Label(labelRect, _message, _labelStyle);

            // 버튼 영역
            float buttonY = y + ToastHeight - 32f;
            float buttonHeight = 22f;

            // 페이드 중 알파가 매우 작으면 버튼 클릭 방지
            bool originalEnabled = GUI.enabled;
            if (_currentAlpha < 0.1f)
            {
                GUI.enabled = false;
            }

            // 버튼 알파 적용
            GUI.color = new Color(1f, 1f, 1f, _currentAlpha);

            if (_toastType == ToastType.Success && !string.IsNullOrEmpty(_reportUrl))
            {
                // 성공: [리포트로 이동] + [닫기] 버튼
                float btnWidth = (innerW - 8f) / 2f;

                Rect reportBtnRect = new Rect(innerX, buttonY, btnWidth, buttonHeight);
                if (GUI.Button(reportBtnRect, "리포트로 이동", _buttonStyle))
                {
                    // 최종 URL 재검증 후 열기
                    if (IsValidHttpsUrl(_reportUrl))
                    {
                        Application.OpenURL(_reportUrl);
                    }
                    else
                    {
                        Debug.LogWarning("[BugBeacon] SubmitToast: 유효하지 않은 URL이므로 열지 않음");
                    }
                    Hide();
                }

                Rect closeBtnRect = new Rect(innerX + btnWidth + 8f, buttonY, btnWidth, buttonHeight);
                if (GUI.Button(closeBtnRect, "닫기", _buttonStyle))
                {
                    Hide();
                }
            }
            else if (_toastType == ToastType.Failure)
            {
                // 실패: [이동] (로컬 pending 폴더 열기) + [닫기] 버튼
                float btnWidth = (innerW - 8f) / 2f;

                Rect moveBtnRect = new Rect(innerX, buttonY, btnWidth, buttonHeight);
                if (GUI.Button(moveBtnRect, "이동", _buttonStyle))
                {
                    OpenPendingFolder();
                    Hide();
                }

                Rect closeBtnRect = new Rect(innerX + btnWidth + 8f, buttonY, btnWidth, buttonHeight);
                if (GUI.Button(closeBtnRect, "닫기", _buttonStyle))
                {
                    Hide();
                }
            }
            else
            {
                // 로컬 저장 등: [닫기] 버튼만
                float closeBtnWidth = 60f;
                Rect closeBtnRect = new Rect(
                    x + ToastWidth - padding - closeBtnWidth,
                    buttonY,
                    closeBtnWidth,
                    buttonHeight);
                if (GUI.Button(closeBtnRect, "닫기", _buttonStyle))
                {
                    Hide();
                }
            }

            // GUI.enabled 복원
            GUI.enabled = originalEnabled;

            GUI.color = originalColor;
            GUI.backgroundColor = originalBgColor;
        }

        // ─── 헬퍼 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 로컬 pending 폴더를 파일 탐색기에서 엽니다.
        /// 에디터 모드에서는 RevealInFinder, 런타임에서는 file:// URL로 엽니다.
        /// </summary>
        private static void OpenPendingFolder()
        {
            string pendingFolderPath = PendingUploadManager.GetPendingDirectory();

            // 폴더가 없으면 생성 (탐색기에서 열 수 있도록)
            if (!Directory.Exists(pendingFolderPath))
            {
                Directory.CreateDirectory(pendingFolderPath);
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(pendingFolderPath);
#else
            // 경로에 한글/공백 등이 포함될 수 있으므로 Uri 클래스로 안전하게 변환
            Application.OpenURL(new Uri(pendingFolderPath).AbsoluteUri);
#endif
            Debug.Log($"[BugBeacon] SubmitToast: pending 폴더 열기 → {pendingFolderPath}");
        }

        /// <summary>
        /// 보안 검증된 리포트 URL을 구성합니다.
        /// Uri.TryCreate로 유효성 검증, https 스킴 강제, reportId를 URI 인코딩합니다.
        /// </summary>
        private static string BuildSecureReportUrl(string baseUrl, string reportId)
        {
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(reportId))
                return "";

            // 기본 URL 유효성 검증
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri baseUri))
            {
                Debug.LogWarning("[BugBeacon] SubmitToast: WEB_DASHBOARD_URL이 유효한 URI가 아닙니다");
                return "";
            }

            // 스킴 검증: https 필수 (로컬 개발 시 http://localhost 허용)
            bool isHttps = string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
            bool isLocalDev = string.Equals(baseUri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                              && (baseUri.Host == "localhost" || baseUri.Host == "127.0.0.1");
            if (!isHttps && !isLocalDev)
            {
                Debug.LogWarning($"[BugBeacon] SubmitToast: 허용되지 않는 스킴 거부 ({baseUri.Scheme}://{baseUri.Host})");
                return "";
            }

            // reportId URI 인코딩
            string encodedReportId = Uri.EscapeDataString(reportId);
            string fullUrl = $"{baseUri.AbsoluteUri.TrimEnd('/')}/workspace/{encodedReportId}";

            // 최종 URL 재검증
            if (!IsValidHttpsUrl(fullUrl))
            {
                Debug.LogWarning("[BugBeacon] SubmitToast: 구성된 최종 URL이 유효하지 않습니다");
                return "";
            }

            return fullUrl;
        }

        /// <summary>
        /// URL이 유효한 https URL인지 검증합니다.
        /// </summary>
        private static bool IsValidHttpsUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                return false;
            // https 허용 + 로컬 개발(http://localhost, http://127.0.0.1) 허용
            if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
                && (uri.Host == "localhost" || uri.Host == "127.0.0.1"))
                return true;
            return false;
        }

        /// <summary>
        /// IMGUI 스타일을 초기화합니다. OnGUI 내부에서 한 번만 호출됩니다.
        /// </summary>
        private void InitializeStyles()
        {
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.whiteTexture }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                normal = { textColor = Color.white },
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
            };
        }
    }
}
