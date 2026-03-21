using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RekonOps.Rekon
{
    /// <summary>
    /// Auth Broker Edge Functions와 HTTP 통신하는 클라이언트.
    /// X-Client-Token 헤더로 JWT를 전송하고, 401 응답 시 재인증 이벤트를 발생시킵니다.
    /// </summary>
    public class AuthBrokerClient
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>401 Unauthorized 응답을 수신했을 때 발생합니다.</summary>
        public event Action OnUnauthorized;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly string _baseUrl;
        private readonly SessionTokenStore _tokenStore;
        private const int MaxRetryCount = 3;
        private const float RetryBaseDelaySeconds = 2f;
        private const float RequestTimeoutSeconds = 30f;

        // ─── 응답 모델 ─────────────────────────────────────────────────────────────

        /// <summary>connect-jira-start 응답 모델</summary>
        [Serializable]
        public class ConnectStartResponse
        {
            public string connect_id;
            public string authorize_url;
        }

        /// <summary>connect-jira-status 응답 모델</summary>
        [Serializable]
        public class ConnectStatusResponse
        {
            public string status;          // "pending" | "completed" | "error"
            public string session_token;   // completed 상태에서만 포함
            public string site_url;        // completed 상태에서만 포함 (예: https://yourcompany.atlassian.net)
        }

        /// <summary>token-jira 응답 모델</summary>
        [Serializable]
        public class JiraTokenResponse
        {
            public string access_token;
            public string expires_at;
            public string cloud_id;
        }

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// AuthBrokerClient를 초기화합니다.
        /// </summary>
        /// <param name="baseUrl">Auth Broker 기본 URL (RekonSettings.authBrokerUrl)</param>
        /// <param name="tokenStore">세션 토큰 저장소</param>
        public AuthBrokerClient(string baseUrl, SessionTokenStore tokenStore)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentNullException(nameof(baseUrl), "Auth Broker URL이 설정되지 않았습니다.");

            _baseUrl = baseUrl.TrimEnd('/');
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Jira OAuth 흐름을 시작합니다.
        /// POST /connect-jira-start
        /// </summary>
        /// <param name="tenantId">테넌트 UUID</param>
        /// <param name="userId">Unity 사용자 외부 ID</param>
        /// <param name="cancellationToken">취소 토큰</param>
        public async Task<ConnectStartResponse> PostConnectJiraStartAsync(
            string tenantId,
            string userId,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/connect-jira-start";
            var body = $"{{\"tenant_id\":\"{tenantId}\",\"user_id\":\"{userId}\"}}";

            var json = await SendWithRetryAsync(
                method: "POST",
                url: url,
                jsonBody: body,
                requireAuth: false,
                cancellationToken: cancellationToken);

            return JsonUtility.FromJson<ConnectStartResponse>(json);
        }

        /// <summary>
        /// Jira OAuth 연결 상태를 폴링합니다.
        /// GET /connect-jira-status?connect_id={connectId}
        /// </summary>
        /// <param name="connectId">연결 UUID</param>
        /// <param name="cancellationToken">취소 토큰</param>
        public async Task<ConnectStatusResponse> GetConnectJiraStatusAsync(
            string connectId,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/connect-jira-status?connect_id={Uri.EscapeDataString(connectId)}";

            var json = await SendWithRetryAsync(
                method: "GET",
                url: url,
                jsonBody: null,
                requireAuth: false,
                cancellationToken: cancellationToken);

            return JsonUtility.FromJson<ConnectStatusResponse>(json);
        }

        /// <summary>
        /// Jira access_token을 갱신합니다.
        /// POST /token-jira (X-Client-Token 헤더 필수)
        /// </summary>
        /// <param name="sessionToken">JWT 세션 토큰</param>
        /// <param name="cancellationToken">취소 토큰</param>
        public async Task<JiraTokenResponse> PostTokenJiraAsync(
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/token-jira";

            var json = await SendWithRetryAsync(
                method: "POST",
                url: url,
                jsonBody: "{}",
                requireAuth: true,
                overrideToken: sessionToken,
                cancellationToken: cancellationToken);

            return JsonUtility.FromJson<JiraTokenResponse>(json);
        }

        // ─── 내부 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 지수 백오프로 HTTP 요청을 재시도합니다.
        /// </summary>
        private async Task<string> SendWithRetryAsync(
            string method,
            string url,
            string jsonBody,
            bool requireAuth,
            string overrideToken = null,
            CancellationToken cancellationToken = default)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MaxRetryCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 인증 토큰 획득
                    string token = overrideToken;
                    if (requireAuth && string.IsNullOrEmpty(token))
                    {
                        token = _tokenStore.Load();
                        if (string.IsNullOrEmpty(token))
                            throw new UnauthorizedAccessException("세션 토큰이 없습니다. 재인증이 필요합니다.");
                    }

                    var responseJson = await SendRequestAsync(method, url, jsonBody, token, cancellationToken);
                    return responseJson;
                }
                catch (AuthBrokerException ex) when (ex.StatusCode == 401)
                {
                    // 401은 재시도하지 않고 재인증 이벤트 발생
                    Debug.LogWarning("[AuthBrokerClient] 401 Unauthorized 수신 → 재인증 이벤트 발생");
                    OnUnauthorized?.Invoke();
                    throw;
                }
                catch (AuthBrokerException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
                {
                    // 4xx (401 제외)는 재시도하지 않음
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;

                    if (attempt < MaxRetryCount)
                    {
                        float delay = RetryBaseDelaySeconds * Mathf.Pow(2f, attempt - 1);
                        Debug.LogWarning($"[AuthBrokerClient] 요청 실패 (시도 {attempt}/{MaxRetryCount}), " +
                                         $"{delay:F1}초 후 재시도. 에러: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                    }
                }
            }

            throw new AggregateException($"Auth Broker 요청 최대 재시도 횟수 초과 ({MaxRetryCount}회)", lastException);
        }

        /// <summary>
        /// UnityWebRequest로 단일 HTTP 요청을 전송합니다.
        /// 메인 스레드에서 실행되어야 합니다.
        /// </summary>
        private async Task<string> SendRequestAsync(
            string method,
            string url,
            string jsonBody,
            string authToken,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<string>();
            var syncContext = SynchronizationContext.Current;

            // UnityWebRequest는 메인 스레드에서 실행 필요
            void RunOnMainThread(Action action)
            {
                if (syncContext != null)
                    syncContext.Post(_ => action(), null);
                else
                    action();
            }

            RunOnMainThread(async () =>
            {
                UnityWebRequest request = null;
                // dispose 여부를 추적하는 플래그 (Register 콜백과의 레이스 컨디션 방지)
                bool isDisposed = false;
                CancellationTokenRegistration registration = default;

                try
                {
                    // 요청 생성
                    if (method == "GET")
                    {
                        request = UnityWebRequest.Get(url);
                    }
                    else
                    {
                        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                        var uploadHandler = new UploadHandlerRaw(bodyBytes);
                        uploadHandler.contentType = "application/json";
                        request = new UnityWebRequest(url, method)
                        {
                            uploadHandler = uploadHandler,
                            downloadHandler = new DownloadHandlerBuffer()
                        };
                    }

                    // 헤더 설정
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Accept", "application/json");

                    if (!string.IsNullOrEmpty(authToken))
                        request.SetRequestHeader("X-Client-Token", authToken);

                    request.timeout = (int)RequestTimeoutSeconds;

                    // 취소 등록 - Abort()를 먼저 호출한 뒤 tcs를 취소 처리.
                    // isDisposed 플래그로 이미 dispose된 request에 접근하는 것을 방지.
                    registration = cancellationToken.Register(() =>
                    {
                        if (!isDisposed)
                        {
                            try { request?.Abort(); }
                            catch (Exception) { /* Abort 자체가 실패해도 취소는 진행 */ }
                        }
                        tcs.TrySetCanceled();
                    });

                    // 요청 전송 및 완료 대기
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        // 취소 요청이 들어오면 대기 루프 탈출 (Abort는 Register 콜백에서 처리됨)
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled();
                            return;
                        }
                        await Task.Yield();
                    }

                    // 루프 종료 후 한 번 더 취소 여부 확인
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    // 응답 처리 - request가 유효한 상태에서만 접근
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        tcs.TrySetResult(request.downloadHandler.text);
                    }
                    else
                    {
                        int statusCode = (int)request.responseCode;
                        string responseText = request.downloadHandler?.text ?? "";
                        string errorMessage = request.error ?? "";

                        if (statusCode == 0)
                        {
                            // 네트워크 오류 (재시도 가능)
                            tcs.TrySetException(new NetworkException(
                                $"네트워크 오류: {errorMessage}"));
                        }
                        else
                        {
                            // HTTP 에러 코드
                            tcs.TrySetException(new AuthBrokerException(
                                statusCode,
                                $"HTTP {statusCode}: {errorMessage} / {responseText}"));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    // isDisposed 플래그를 먼저 세운 뒤 Dispose 호출.
                    // 이후 Register 콜백이 실행되더라도 request에 접근하지 않음.
                    isDisposed = true;
                    registration.Dispose();
                    request?.Dispose();
                }
            });

            return await tcs.Task;
        }
    }

    // ─── 예외 클래스 ───────────────────────────────────────────────────────────────

    /// <summary>Auth Broker HTTP 에러 예외</summary>
    public class AuthBrokerException : Exception
    {
        public int StatusCode { get; }

        public AuthBrokerException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary>네트워크 연결 오류 예외 (재시도 가능)</summary>
    public class NetworkException : Exception
    {
        public NetworkException(string message) : base(message) { }
        public NetworkException(string message, Exception inner) : base(message, inner) { }
    }
}
