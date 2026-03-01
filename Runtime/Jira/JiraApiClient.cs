using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// Jira REST API v3 클라이언트.
    /// Auth Broker의 token-jira 엔드포인트에서 access_token을 획득하고,
    /// Jira Cloud API를 호출하는 기반 클라이언트입니다.
    /// </summary>
    public class JiraApiClient
    {
        // ─── 상수 ─────────────────────────────────────────────────────────────────

        private const string JiraApiBaseTemplate = "https://api.atlassian.com/ex/jira/{0}/rest/api/3";
        private const float RequestTimeoutSeconds = 60f;
        private const int MaxRetryCount = 3;
        private const float RetryBaseDelaySeconds = 2f;

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly TokenRefreshManager _tokenRefreshManager;

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// JiraApiClient를 초기화합니다.
        /// </summary>
        /// <param name="tokenRefreshManager">Jira access_token 관리자</param>
        public JiraApiClient(TokenRefreshManager tokenRefreshManager)
        {
            _tokenRefreshManager = tokenRefreshManager
                ?? throw new ArgumentNullException(nameof(tokenRefreshManager));
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Jira REST API v3 엔드포인트에 GET 요청을 전송합니다.
        /// </summary>
        /// <param name="path">API 경로 (예: "/issue/PROJ-123")</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>응답 JSON 문자열</returns>
        public async Task<string> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            var (accessToken, cloudId) = await _tokenRefreshManager.GetValidAccessTokenAsync(cancellationToken);
            var url = BuildUrl(cloudId, path);
            return await SendWithRetryAsync("GET", url, null, accessToken, cancellationToken);
        }

        /// <summary>
        /// Jira REST API v3 엔드포인트에 POST 요청을 전송합니다.
        /// </summary>
        /// <param name="path">API 경로</param>
        /// <param name="jsonBody">요청 본문 JSON</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>응답 JSON 문자열</returns>
        public async Task<string> PostAsync(
            string path,
            string jsonBody,
            CancellationToken cancellationToken = default)
        {
            var (accessToken, cloudId) = await _tokenRefreshManager.GetValidAccessTokenAsync(cancellationToken);
            var url = BuildUrl(cloudId, path);
            return await SendWithRetryAsync("POST", url, jsonBody, accessToken, cancellationToken);
        }

        /// <summary>
        /// Jira REST API v3 엔드포인트에 멀티파트 POST 요청을 전송합니다.
        /// 첨부파일 업로드에 사용됩니다.
        /// </summary>
        /// <param name="path">API 경로</param>
        /// <param name="formData">멀티파트 폼 데이터</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>응답 JSON 문자열</returns>
        public async Task<string> PostMultipartAsync(
            string path,
            MultipartFormData formData,
            CancellationToken cancellationToken = default)
        {
            var (accessToken, cloudId) = await _tokenRefreshManager.GetValidAccessTokenAsync(cancellationToken);
            var url = BuildUrl(cloudId, path);
            return await SendMultipartAsync(url, formData, accessToken, cancellationToken);
        }

        // ─── 내부 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Jira API 전체 URL을 구성합니다.
        /// </summary>
        private static string BuildUrl(string cloudId, string path)
        {
            var baseUrl = string.Format(JiraApiBaseTemplate, cloudId);
            return baseUrl + (path.StartsWith("/") ? path : "/" + path);
        }

        /// <summary>
        /// 지수 백오프로 HTTP 요청을 재시도합니다.
        /// </summary>
        private async Task<string> SendWithRetryAsync(
            string method,
            string url,
            string jsonBody,
            string accessToken,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MaxRetryCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await SendRequestAsync(method, url, jsonBody, accessToken, cancellationToken);
                }
                catch (JiraApiException ex) when (ex.StatusCode == 401)
                {
                    // 401: access_token 만료 → 캐시 무효화 후 1회 재시도
                    if (attempt == 0)
                    {
                        Debug.LogWarning("[JiraApiClient] 401 감지 → access_token 캐시 무효화 후 재시도");
                        _tokenRefreshManager.InvalidateAccessTokenCache();
                        var (newToken, _) = await _tokenRefreshManager.GetValidAccessTokenAsync(cancellationToken);
                        accessToken = newToken;
                        attempt++;
                        continue;
                    }
                    throw;
                }
                catch (JiraApiException ex) when (ex.StatusCode == 429)
                {
                    // 429 Rate Limited: 재시도
                    attempt++;
                    if (attempt < MaxRetryCount)
                    {
                        float delay = RetryBaseDelaySeconds * Mathf.Pow(2f, attempt - 1);
                        Debug.LogWarning($"[JiraApiClient] 429 Rate Limited. {delay:F1}초 후 재시도.");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                    }
                    else
                    {
                        lastException = ex;
                        break;
                    }
                }
                catch (JiraApiException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
                {
                    // 다른 4xx: 재시도하지 않음
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
                        Debug.LogWarning($"[JiraApiClient] 요청 실패 (시도 {attempt}/{MaxRetryCount}), " +
                                         $"{delay:F1}초 후 재시도. 에러: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                    }
                }
            }

            throw new AggregateException($"Jira API 요청 최대 재시도 횟수 초과 ({MaxRetryCount}회)", lastException);
        }

        /// <summary>
        /// UnityWebRequest로 단일 JSON HTTP 요청을 전송합니다.
        /// </summary>
        private async Task<string> SendRequestAsync(
            string method,
            string url,
            string jsonBody,
            string accessToken,
            CancellationToken cancellationToken)
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
                        request = new UnityWebRequest(url, method)
                        {
                            uploadHandler = new UploadHandlerRaw(bodyBytes)
                            {
                                contentType = "application/json"
                            },
                            downloadHandler = new DownloadHandlerBuffer()
                        };
                    }

                    // 헤더 설정
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Accept", "application/json");
                    request.timeout = (int)RequestTimeoutSeconds;

                    // 취소 등록
                    cancellationToken.Register(() =>
                    {
                        request?.Abort();
                        tcs.TrySetCanceled();
                    });

                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                        await Task.Yield();

                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        tcs.TrySetResult(request.downloadHandler.text);
                    }
                    else
                    {
                        int statusCode = (int)request.responseCode;
                        string responseText = request.downloadHandler?.text ?? "";

                        if (statusCode == 0)
                        {
                            tcs.TrySetException(new NetworkException(
                                $"Jira API 네트워크 오류: {request.error}"));
                        }
                        else
                        {
                            tcs.TrySetException(new JiraApiException(
                                statusCode,
                                $"Jira API HTTP {statusCode}: {request.error}\n응답: {responseText}"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    request?.Dispose();
                }
            });

            return await tcs.Task;
        }

        /// <summary>
        /// UnityWebRequest로 멀티파트 폼 요청을 전송합니다.
        /// </summary>
        private async Task<string> SendMultipartAsync(
            string url,
            MultipartFormData formData,
            string accessToken,
            CancellationToken cancellationToken)
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
                try
                {
                    var wwwForm = new WWWForm();
                    foreach (var field in formData.Fields)
                        wwwForm.AddField(field.Key, field.Value);

                    foreach (var file in formData.Files)
                        wwwForm.AddBinaryData("file", file.Data, file.FileName, file.ContentType);

                    request = UnityWebRequest.Post(url, wwwForm);
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                    // Jira 첨부파일 업로드 시 X-Atlassian-Token 필수
                    request.SetRequestHeader("X-Atlassian-Token", "no-check");
                    request.timeout = (int)RequestTimeoutSeconds;

                    cancellationToken.Register(() =>
                    {
                        request?.Abort();
                        tcs.TrySetCanceled();
                    });

                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                        await Task.Yield();

                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        tcs.TrySetResult(request.downloadHandler.text);
                    }
                    else
                    {
                        int statusCode = (int)request.responseCode;
                        string responseText = request.downloadHandler?.text ?? "";
                        tcs.TrySetException(new JiraApiException(
                            statusCode,
                            $"Jira 멀티파트 HTTP {statusCode}: {request.error}\n응답: {responseText}"));
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    request?.Dispose();
                }
            });

            return await tcs.Task;
        }
    }

    // ─── 멀티파트 폼 데이터 모델 ──────────────────────────────────────────────────

    /// <summary>멀티파트 폼 데이터 컨테이너</summary>
    public class MultipartFormData
    {
        public System.Collections.Generic.Dictionary<string, string> Fields { get; }
            = new System.Collections.Generic.Dictionary<string, string>();

        public System.Collections.Generic.List<MultipartFile> Files { get; }
            = new System.Collections.Generic.List<MultipartFile>();

        public void AddField(string name, string value) => Fields[name] = value;

        public void AddFile(string fileName, byte[] data, string contentType = "application/octet-stream")
            => Files.Add(new MultipartFile(fileName, data, contentType));
    }

    /// <summary>멀티파트 파일 항목</summary>
    public class MultipartFile
    {
        public string FileName { get; }
        public byte[] Data { get; }
        public string ContentType { get; }

        public MultipartFile(string fileName, byte[] data, string contentType)
        {
            FileName = fileName;
            Data = data;
            ContentType = contentType;
        }
    }

    // ─── 예외 클래스 ──────────────────────────────────────────────────────────────

    /// <summary>Jira REST API HTTP 에러 예외</summary>
    public class JiraApiException : Exception
    {
        public int StatusCode { get; }

        public JiraApiException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
