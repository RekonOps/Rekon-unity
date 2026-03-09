using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// Supabase Edge Functions (auth-unity-start / auth-unity-status)를 호출하여
    /// Unity에서 웹 브라우저 기반 로그인을 처리하는 클라이언트입니다.
    /// </summary>
    public class SupabaseAuthClient
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>인증 완료 시 발생합니다.</summary>
        public event Action<AuthResult> OnAuthCompleted;

        /// <summary>인증 실패 시 발생합니다.</summary>
        public event Action<string> OnAuthFailed;

        // ─── 상수 ─────────────────────────────────────────────────────────────────

        private const int MaxRetryCount = 3;
        private const float RetryBaseDelaySeconds = 2f;
        private const float RequestTimeoutSeconds = 30f;
        private const float PollIntervalSeconds = 2f;
        private const int MaxPollAttempts = 150; // 2초 x 150 = 최대 5분

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly SessionTokenStore _tokenStore;

        // ─── 응답 모델 ─────────────────────────────────────────────────────────────

        /// <summary>auth-unity-start 응답 모델</summary>
        [Serializable]
        public class StartResponse
        {
            public string connect_id;
            public string login_url;
        }

        /// <summary>auth-unity-status 응답 모델</summary>
        [Serializable]
        public class StatusResponse
        {
            public string status;
            public string access_token;
            public string workspace_id;
            public string workspace_name;
            public string message;
        }

        // ─── 인증 결과 ─────────────────────────────────────────────────────────────

        /// <summary>인증 성공 시 반환되는 결과 모델</summary>
        public class AuthResult
        {
            public string AccessToken;
            public string WorkspaceId;
            public string WorkspaceName;
        }

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// SupabaseAuthClient를 초기화합니다.
        /// </summary>
        /// <param name="supabaseUrl">Supabase 프로젝트 URL</param>
        /// <param name="supabaseAnonKey">Supabase Anon Key</param>
        /// <param name="tokenStore">세션 토큰 저장소</param>
        public SupabaseAuthClient(string supabaseUrl, string supabaseAnonKey, SessionTokenStore tokenStore)
        {
            if (string.IsNullOrEmpty(supabaseUrl))
                throw new ArgumentNullException(nameof(supabaseUrl), "Supabase URL이 설정되지 않았습니다.");
            if (string.IsNullOrEmpty(supabaseAnonKey))
                throw new ArgumentNullException(nameof(supabaseAnonKey), "Supabase Anon Key가 설정되지 않았습니다.");

            _supabaseUrl = supabaseUrl.TrimEnd('/');
            _supabaseAnonKey = supabaseAnonKey;
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 현재 Supabase 로그인 상태를 확인합니다.
        /// </summary>
        public bool IsLoggedIn => !_tokenStore.IsSupabaseExpired();

        /// <summary>
        /// 저장된 Supabase 액세스 토큰을 반환합니다.
        /// 로그인 상태가 아니면 null 또는 빈 문자열을 반환합니다.
        /// </summary>
        public string AccessToken => _tokenStore.LoadSupabase();

        /// <summary>
        /// 웹 브라우저를 통한 로그인을 시작합니다.
        /// auth-unity-start를 호출한 뒤 브라우저를 열고, 완료될 때까지 폴링합니다.
        /// </summary>
        /// <param name="deviceId">디바이스 고유 식별자 (UUID)</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>인증 결과</returns>
        public async Task<AuthResult> StartWebLoginAsync(string deviceId, CancellationToken ct = default)
        {
            try
            {
                // 1단계: auth-unity-start 호출 (재시도 포함)
                Debug.Log("[BugOneTouch] Supabase 웹 로그인 시작 요청 중...");
                var startResponse = await PostAuthUnityStartAsync(deviceId, ct);

                if (string.IsNullOrEmpty(startResponse.connect_id) || string.IsNullOrEmpty(startResponse.login_url))
                {
                    var error = "auth-unity-start 응답에 connect_id 또는 login_url이 없습니다.";
                    Debug.LogError($"[BugOneTouch] {error}");
                    OnAuthFailed?.Invoke(error);
                    throw new InvalidOperationException(error);
                }

                Debug.Log($"[BugOneTouch] 로그인 URL 수신 완료. connect_id: {startResponse.connect_id}");

                // 2단계: 브라우저로 로그인 URL 열기
                Application.OpenURL(startResponse.login_url);
                Debug.Log("[BugOneTouch] 웹 브라우저에서 로그인 페이지를 열었습니다.");

                // 3단계: auth-unity-status 폴링
                Debug.Log("[BugOneTouch] 인증 완료 대기 중 (최대 5분)...");
                var result = await PollAuthUnityStatusAsync(startResponse.connect_id, ct);

                // 4단계: 토큰 저장 및 이벤트 발생
                _tokenStore.SaveSupabase(result.AccessToken);
                Debug.Log($"[BugOneTouch] Supabase 인증 완료. 워크스페이스: {result.WorkspaceName}");
                OnAuthCompleted?.Invoke(result);

                return result;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[BugOneTouch] Supabase 웹 로그인이 취소되었습니다.");
                throw;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                var errorMsg = $"Supabase 웹 로그인 실패: {ex.Message}";
                Debug.LogError($"[BugOneTouch] {errorMsg}");
                OnAuthFailed?.Invoke(errorMsg);
                throw;
            }
        }

        /// <summary>
        /// Supabase 로그아웃 (저장된 토큰 삭제).
        /// </summary>
        public void Logout()
        {
            _tokenStore.ClearSupabase();
            Debug.Log("[BugOneTouch] Supabase 로그아웃 완료.");
        }

        // ─── 유틸리티 ────────────────────────────────────────────────────────────────

        /// <summary>JSON 문자열 이스케이프 처리.</summary>
        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        // ─── 내부 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// auth-unity-start Edge Function을 호출합니다 (지수 백오프 재시도 포함).
        /// </summary>
        private async Task<StartResponse> PostAuthUnityStartAsync(string deviceId, CancellationToken ct)
        {
            var url = $"{_supabaseUrl}/functions/v1/auth-unity-start";
            var escapedDeviceId = EscapeJsonString(deviceId);
            var body = $"{{\"device_id\":\"{escapedDeviceId}\"}}";

            var json = await SendWithRetryAsync("POST", url, body, ct);
            return JsonUtility.FromJson<StartResponse>(json);
        }

        /// <summary>
        /// auth-unity-status를 2초 간격으로 폴링하여 인증 완료를 대기합니다.
        /// </summary>
        private async Task<AuthResult> PollAuthUnityStatusAsync(string connectId, CancellationToken ct)
        {
            var url = $"{_supabaseUrl}/functions/v1/auth-unity-status?connect_id={Uri.EscapeDataString(connectId)}";

            for (int attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var json = await SendRequestAsync("GET", url, null, ct);
                    var status = JsonUtility.FromJson<StatusResponse>(json);

                    switch (status.status)
                    {
                        case "completed":
                            if (string.IsNullOrEmpty(status.access_token))
                                throw new InvalidOperationException("completed 상태이나 access_token이 없습니다.");

                            return new AuthResult
                            {
                                AccessToken = status.access_token,
                                WorkspaceId = status.workspace_id,
                                WorkspaceName = status.workspace_name
                            };

                        case "expired":
                            var expiredMsg = !string.IsNullOrEmpty(status.message)
                                ? status.message
                                : "인증 세션이 만료되었습니다. 다시 시도해주세요.";
                            throw new TimeoutException(expiredMsg);

                        case "error":
                            var errorMsg = !string.IsNullOrEmpty(status.message)
                                ? status.message
                                : "인증 중 서버 오류가 발생했습니다.";
                            throw new InvalidOperationException(errorMsg);

                        case "pending":
                            // 대기 후 다음 폴링
                            break;

                        default:
                            Debug.LogWarning($"[BugOneTouch] 알 수 없는 상태: {status.status}");
                            break;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TimeoutException))
                {
                    // 폴링 중 네트워크 오류는 무시하고 다음 폴링 시도
                    Debug.LogWarning($"[BugOneTouch] 폴링 중 오류 (무시): {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), ct);
            }

            throw new TimeoutException($"인증 폴링 시간 초과 ({MaxPollAttempts * PollIntervalSeconds}초). 다시 시도해주세요.");
        }

        /// <summary>
        /// 지수 백오프로 HTTP 요청을 재시도합니다 (auth-unity-start 전용).
        /// </summary>
        private async Task<string> SendWithRetryAsync(string method, string url, string jsonBody, CancellationToken ct)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MaxRetryCount)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    return await SendRequestAsync(method, url, jsonBody, ct);
                }
                catch (AuthBrokerException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
                {
                    // 4xx 에러는 재시도하지 않음
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
                        Debug.LogWarning($"[BugOneTouch] Supabase 요청 실패 (시도 {attempt}/{MaxRetryCount}), " +
                                         $"{delay:F1}초 후 재시도. 에러: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                    }
                }
            }

            throw new AggregateException(
                $"Supabase 요청 최대 재시도 횟수 초과 ({MaxRetryCount}회)", lastException);
        }

        /// <summary>
        /// UnityWebRequest로 단일 HTTP 요청을 전송합니다.
        /// Authorization: Bearer {anonKey} 헤더를 자동으로 추가합니다.
        /// </summary>
        private async Task<string> SendRequestAsync(
            string method,
            string url,
            string jsonBody,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            var syncContext = SynchronizationContext.Current;

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
                    request.SetRequestHeader("Authorization", $"Bearer {_supabaseAnonKey}");

                    request.timeout = (int)RequestTimeoutSeconds;

                    // 취소 등록
                    registration = ct.Register(() =>
                    {
                        if (!isDisposed)
                        {
                            try { request?.Abort(); }
                            catch (Exception) { /* Abort 실패 시에도 취소 진행 */ }
                        }
                        tcs.TrySetCanceled();
                    });

                    // 요청 전송 및 완료 대기
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled();
                            return;
                        }
                        await Task.Yield();
                    }

                    if (ct.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    // 응답 처리
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
                            tcs.TrySetException(new NetworkException(
                                $"네트워크 오류: {errorMessage}"));
                        }
                        else
                        {
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
                    isDisposed = true;
                    registration.Dispose();
                    request?.Dispose();
                }
            });

            return await tcs.Task;
        }
    }
}
