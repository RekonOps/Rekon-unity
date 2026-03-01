using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// Jira OAuth 2.0 브라우저 인증 플로우를 관리합니다.
    /// Application.OpenURL로 브라우저를 열고, connect-jira-status를 폴링하여 완료를 기다립니다.
    /// </summary>
    public class OAuthFlowManager
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>OAuth 플로우 진행 상태 변경 이벤트. (message)</summary>
        public event Action<string> OnStatusChanged;

        /// <summary>OAuth 플로우 완료 이벤트. (sessionToken)</summary>
        public event Action<string> OnCompleted;

        /// <summary>OAuth 플로우 실패 이벤트. (errorMessage)</summary>
        public event Action<string> OnFailed;

        // ─── 상수 ─────────────────────────────────────────────────────────────────

        private const float PollIntervalSeconds = 2f;
        private const float TimeoutSeconds = 300f; // 5분
        private const string StatusPending = "pending";
        private const string StatusCompleted = "completed";
        private const string StatusError = "error";

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly AuthBrokerClient _brokerClient;
        private readonly SessionTokenStore _tokenStore;
        private CancellationTokenSource _currentCts;
        private bool _isRunning;

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// OAuthFlowManager를 초기화합니다.
        /// </summary>
        /// <param name="brokerClient">Auth Broker 클라이언트</param>
        /// <param name="tokenStore">세션 토큰 저장소</param>
        public OAuthFlowManager(AuthBrokerClient brokerClient, SessionTokenStore tokenStore)
        {
            _brokerClient = brokerClient ?? throw new ArgumentNullException(nameof(brokerClient));
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// OAuth 플로우가 현재 실행 중인지 여부
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Jira OAuth 인증 플로우를 시작합니다.
        /// 1. connect-jira-start 호출로 authorize_url 획득
        /// 2. 브라우저에서 authorize_url 열기
        /// 3. connect-jira-status 폴링 (2초 간격, 최대 5분)
        /// 4. 완료 시 JWT 세션 토큰을 SessionTokenStore에 저장
        /// </summary>
        /// <param name="tenantId">테넌트 UUID</param>
        /// <param name="userId">Unity 사용자 외부 ID</param>
        /// <param name="externalCancellationToken">외부 취소 토큰</param>
        /// <returns>발급된 JWT 세션 토큰</returns>
        public async Task<string> StartOAuthFlowAsync(
            string tenantId,
            string userId,
            CancellationToken externalCancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("이미 OAuth 플로우가 진행 중입니다.");

            // 타임아웃 + 외부 취소를 합친 CancellationToken 생성
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            _currentCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            _isRunning = true;
            NotifyStatus("Jira 연동을 시작합니다...");

            try
            {
                // Step 1: OAuth 흐름 시작 요청
                NotifyStatus("Auth Broker에 연동 요청 중...");
                var startResponse = await _brokerClient.PostConnectJiraStartAsync(
                    tenantId,
                    userId,
                    _currentCts.Token);

                if (string.IsNullOrEmpty(startResponse?.connect_id) ||
                    string.IsNullOrEmpty(startResponse?.authorize_url))
                {
                    throw new InvalidOperationException("Auth Broker로부터 유효하지 않은 응답을 받았습니다.");
                }

                Debug.Log($"[OAuthFlowManager] 연동 시작. connect_id: {startResponse.connect_id}");

                // Step 2: 기본 브라우저에서 Jira 인증 URL 열기
                NotifyStatus("브라우저에서 Jira 인증 페이지를 열고 있습니다...");
                Application.OpenURL(startResponse.authorize_url);
                Debug.Log($"[OAuthFlowManager] 브라우저 열기 요청: {startResponse.authorize_url}");

                // Step 3: 완료 상태 폴링
                NotifyStatus("Jira 인증 완료를 기다리는 중... (브라우저에서 Jira 로그인 후 허가해주세요)");
                var sessionToken = await PollForCompletionAsync(
                    startResponse.connect_id,
                    _currentCts.Token);

                // Step 4: JWT 세션 토큰 저장
                _tokenStore.Save(sessionToken);
                Debug.Log("[OAuthFlowManager] OAuth 플로우 완료. 세션 토큰 저장됨.");

                NotifyStatus("Jira 연동이 완료되었습니다!");
                OnCompleted?.Invoke(sessionToken);

                return sessionToken;
            }
            catch (OperationCanceledException) when (_currentCts.IsCancellationRequested && !externalCancellationToken.IsCancellationRequested)
            {
                // 내부 타임아웃 (외부 취소 아님)
                var msg = $"Jira 인증 시간이 초과되었습니다 ({TimeoutSeconds / 60:F0}분). 다시 시도해주세요.";
                Debug.LogWarning($"[OAuthFlowManager] {msg}");
                NotifyStatus(msg);
                OnFailed?.Invoke(msg);
                throw new TimeoutException(msg);
            }
            catch (OperationCanceledException)
            {
                // 외부 취소
                var msg = "Jira 인증이 취소되었습니다.";
                Debug.Log($"[OAuthFlowManager] {msg}");
                NotifyStatus(msg);
                OnFailed?.Invoke(msg);
                throw;
            }
            catch (Exception ex)
            {
                var msg = $"Jira 인증 중 오류가 발생했습니다: {ex.Message}";
                Debug.LogError($"[OAuthFlowManager] {msg}");
                NotifyStatus(msg);
                OnFailed?.Invoke(msg);
                throw;
            }
            finally
            {
                _isRunning = false;
                _currentCts?.Dispose();
                _currentCts = null;
            }
        }

        /// <summary>
        /// 현재 진행 중인 OAuth 플로우를 취소합니다.
        /// </summary>
        public void Cancel()
        {
            if (_currentCts != null && !_currentCts.IsCancellationRequested)
            {
                Debug.Log("[OAuthFlowManager] OAuth 플로우 취소 요청");
                _currentCts.Cancel();
            }
        }

        // ─── 내부 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// connect-jira-status를 2초 간격으로 폴링하여 완료를 기다립니다.
        /// </summary>
        /// <param name="connectId">연결 UUID</param>
        /// <param name="cancellationToken">타임아웃 포함 취소 토큰</param>
        /// <returns>JWT 세션 토큰</returns>
        private async Task<string> PollForCompletionAsync(
            string connectId,
            CancellationToken cancellationToken)
        {
            Debug.Log($"[OAuthFlowManager] 상태 폴링 시작. connect_id: {connectId}");

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), cancellationToken);

                AuthBrokerClient.ConnectStatusResponse statusResponse;
                try
                {
                    statusResponse = await _brokerClient.GetConnectJiraStatusAsync(
                        connectId,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 폴링 중 네트워크 오류는 로그만 남기고 계속 시도
                    Debug.LogWarning($"[OAuthFlowManager] 상태 폴링 중 오류 (재시도): {ex.Message}");
                    continue;
                }

                var status = statusResponse?.status;
                Debug.Log($"[OAuthFlowManager] 폴링 결과: {status}");

                switch (status)
                {
                    case StatusCompleted:
                        if (string.IsNullOrEmpty(statusResponse.session_token))
                            throw new InvalidOperationException("completed 상태이나 세션 토큰이 없습니다.");
                        return statusResponse.session_token;

                    case StatusError:
                        throw new InvalidOperationException("Jira 연동 중 서버 오류가 발생했습니다.");

                    case StatusPending:
                        // 계속 폴링
                        break;

                    default:
                        Debug.LogWarning($"[OAuthFlowManager] 알 수 없는 상태: {status}");
                        break;
                }
            }

            throw new OperationCanceledException("폴링이 취소되었습니다.", cancellationToken);
        }

        /// <summary>
        /// 상태 변경 이벤트를 안전하게 발생시킵니다.
        /// </summary>
        private void NotifyStatus(string message)
        {
            try
            {
                OnStatusChanged?.Invoke(message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OAuthFlowManager] OnStatusChanged 이벤트 핸들러 오류: {ex.Message}");
            }
        }
    }
}
