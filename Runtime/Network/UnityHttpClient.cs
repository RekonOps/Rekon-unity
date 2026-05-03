using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RekonOps.Rekon
{
    /// <summary>
    /// IRekonHttpClient의 실제 구현체.
    /// UnityWebRequest + TaskCompletionSource + SynchronizationContext 디스패치를 통합합니다.
    /// 4 모듈 (LicenseValidator, AuthBrokerClient, SupabaseAuthClient, R2Uploader) 의
    /// 중복 HTTP 패턴을 하나로 통합합니다.
    /// </summary>
    public class UnityHttpClient : IRekonHttpClient
    {
        private const int DefaultTimeoutSeconds = 30;
        private const int PutTimeoutSeconds = 120;

        // ─── GET ─────────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public Task<HttpResponse> GetAsync(
            string url,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            return SendAsync("GET", url, body: null, contentType: null, headers: headers,
                progress: null, timeoutSeconds: DefaultTimeoutSeconds, cancellationToken: cancellationToken);
        }

        // ─── POST ────────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public Task<HttpResponse> PostAsync(
            string url,
            string jsonBody,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            return SendAsync("POST", url, body: bodyBytes, contentType: "application/json",
                headers: headers, progress: null, timeoutSeconds: DefaultTimeoutSeconds,
                cancellationToken: cancellationToken);
        }

        // ─── PUT ─────────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public Task<HttpResponse> PutAsync(
            string url,
            byte[] body,
            string contentType,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            return SendAsync("PUT", url, body: body, contentType: contentType,
                headers: null, progress: progress, timeoutSeconds: PutTimeoutSeconds,
                cancellationToken: cancellationToken);
        }

        // ─── 내부 공통 로직 ──────────────────────────────────────────────────────

        /// <summary>
        /// 메인 스레드에서 UnityWebRequest를 실행하는 공통 메서드.
        /// 취소 토큰 → Abort → TaskCompletionSource 패턴을 통합합니다.
        /// </summary>
        private Task<HttpResponse> SendAsync(
            string method,
            string url,
            byte[] body,
            string contentType,
            Dictionary<string, string> headers,
            IProgress<float> progress,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpResponse>();
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
                    else if (method == "PUT" && body != null)
                    {
                        // PUT: UnityWebRequest.Put 사용 (R2Uploader 패턴)
                        request = UnityWebRequest.Put(url, body);
                        if (!string.IsNullOrEmpty(contentType))
                            request.SetRequestHeader("Content-Type", contentType);
                    }
                    else
                    {
                        // POST / DELETE 등 (JSON body)
                        var uploadHandler = new UploadHandlerRaw(body ?? Encoding.UTF8.GetBytes("{}"));
                        uploadHandler.contentType = contentType ?? "application/json";
                        request = new UnityWebRequest(url, method)
                        {
                            uploadHandler = uploadHandler,
                            downloadHandler = new DownloadHandlerBuffer()
                        };
                    }

                    // GET/PUT에 downloadHandler 보장
                    if (request.downloadHandler == null)
                        request.downloadHandler = new DownloadHandlerBuffer();

                    // 추가 헤더 설정
                    if (headers != null)
                    {
                        foreach (var kv in headers)
                            request.SetRequestHeader(kv.Key, kv.Value);
                    }

                    request.timeout = timeoutSeconds;

                    // 취소 등록 — isDisposed 플래그로 레이스 컨디션 방지
                    registration = cancellationToken.Register(() =>
                    {
                        if (!isDisposed)
                        {
                            try { request?.Abort(); }
                            catch (Exception) { /* Abort 실패 무시 */ }
                        }
                        tcs.TrySetCanceled();
                    });

                    // 요청 전송 및 완료 대기
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled();
                            return;
                        }

                        // PUT 진행률 콜백 (업로드 전용)
                        if (progress != null)
                        {
                            try { progress.Report(operation.progress); }
                            catch { /* 진행률 콜백 실패 무시 */ }
                        }

                        await Task.Yield();
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    // 최종 진행률 보고 (100%)
                    if (progress != null)
                    {
                        try { progress.Report(1f); }
                        catch { }
                    }

                    // 응답 처리 — HttpResponse 구조체 반환 (예외 없음, caller가 상태 코드 판별)
                    int statusCode = (int)request.responseCode;
                    string responseBody = request.downloadHandler?.text ?? "";

                    if (request.result == UnityWebRequest.Result.ConnectionError ||
                        request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        // 네트워크 연결 오류 — statusCode가 0인 경우
                        tcs.TrySetException(new NetworkException(
                            $"네트워크 오류: {request.error ?? "알 수 없음"}"));
                    }
                    else
                    {
                        // 성공(2xx) 또는 HTTP 에러(4xx/5xx) 모두 HttpResponse로 반환
                        tcs.TrySetResult(new HttpResponse
                        {
                            StatusCode = statusCode,
                            Body = responseBody
                        });
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
                    // isDisposed 플래그를 먼저 세운 뒤 Dispose — Register 콜백 레이스 컨디션 방지
                    isDisposed = true;
                    registration.Dispose();
                    request?.Dispose();
                }
            });

            return tcs.Task;
        }
    }
}
