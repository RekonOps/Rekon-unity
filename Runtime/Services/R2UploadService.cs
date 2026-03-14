using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// Presigned URL을 사용한 R2 파일 업로드 서비스.
    /// UnityWebRequest PUT으로 파일을 업로드하며, 진행률 콜백/재시도/취소를 지원합니다.
    /// </summary>
    public class R2UploadService
    {
        // ─── 설정 ───────────────────────────────────────────────────────────────

        /// <summary>최대 재시도 횟수 (기본 3회)</summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>재시도 기본 지연 시간(초). 지수 백오프 적용: base * 2^(attempt-1)</summary>
        public float BaseRetryDelaySec { get; set; } = 1f;

        // ─── 상수 ───────────────────────────────────────────────────────────────

        private const int UploadTimeoutSeconds = 120;

        // ─── Content-Type 매핑 ──────────────────────────────────────────────────

        private static readonly Dictionary<string, string> ExtensionToContentType =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // 영상
                { ".mp4", "video/mp4" },
                { ".webm", "video/webm" },
                { ".mov", "video/quicktime" },
                { ".avi", "video/x-msvideo" },

                // 이미지
                { ".png", "image/png" },
                { ".jpg", "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".gif", "image/gif" },
                { ".webp", "image/webp" },
                { ".bmp", "image/bmp" },

                // 텍스트/로그
                { ".txt", "text/plain" },
                { ".log", "text/plain" },
                { ".csv", "text/csv" },
                { ".json", "application/json" },
                { ".xml", "application/xml" },

                // 압축
                { ".zip", "application/zip" },
                { ".gz", "application/gzip" },
            };

        // ─── 공개 메서드 ────────────────────────────────────────────────────────

        /// <summary>
        /// 단일 파일을 Presigned URL에 PUT 업로드합니다.
        /// </summary>
        /// <param name="presignedUrl">R2 Presigned URL</param>
        /// <param name="fileData">업로드할 파일 데이터</param>
        /// <param name="contentType">
        /// Content-Type. null 또는 빈 문자열이면 "application/octet-stream" 사용.
        /// </param>
        /// <param name="progress">진행률 콜백 (0~1)</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>업로드 결과</returns>
        public async Task<UploadResult> UploadFileAsync(
            string presignedUrl,
            byte[] fileData,
            string contentType,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            // 입력 검증
            if (string.IsNullOrEmpty(presignedUrl))
            {
                return new UploadResult
                {
                    Success = false,
                    StatusCode = 0,
                    ErrorMessage = "Presigned URL이 비어있습니다.",
                    BytesUploaded = 0
                };
            }

            if (fileData == null || fileData.Length == 0)
            {
                return new UploadResult
                {
                    Success = false,
                    StatusCode = 0,
                    ErrorMessage = "업로드할 파일 데이터가 없습니다.",
                    BytesUploaded = 0
                };
            }

            // Content-Type 결정
            if (string.IsNullOrEmpty(contentType))
                contentType = "application/octet-stream";

            int attempt = 0;
            Exception lastException = null;
            int lastStatusCode = 0;

            while (attempt < MaxRetries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (attempt > 0)
                    {
                        Debug.Log($"[BugBeacon] R2 업로드 재시도 {attempt}/{MaxRetries}");
                    }

                    var result = await PutUploadAsync(
                        presignedUrl, fileData, contentType, progress, cancellationToken);

                    Debug.Log($"[BugBeacon] R2 업로드 성공 ({fileData.Length / 1024}KB)");
                    return result;
                }
                catch (OperationCanceledException)
                {
                    Debug.Log("[BugBeacon] R2 업로드 취소됨");
                    throw;
                }
                catch (R2UploadException ex)
                {
                    lastException = ex;
                    lastStatusCode = ex.StatusCode;
                    attempt++;

                    // 4xx 클라이언트 오류는 재시도 무의미 (403 Presigned URL 만료 등)
                    if (ex.StatusCode >= 400 && ex.StatusCode < 500)
                    {
                        Debug.LogError(
                            $"[BugBeacon] R2 업로드 클라이언트 오류 (HTTP {ex.StatusCode}), 재시도하지 않음: {ex.Message}");
                        break;
                    }

                    if (attempt < MaxRetries)
                    {
                        float delay = BaseRetryDelaySec * Mathf.Pow(2f, attempt - 1);
                        Debug.LogWarning(
                            $"[BugBeacon] R2 업로드 실패 (시도 {attempt}/{MaxRetries}), " +
                            $"{delay:F1}초 후 재시도: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;

                    if (attempt < MaxRetries)
                    {
                        float delay = BaseRetryDelaySec * Mathf.Pow(2f, attempt - 1);
                        Debug.LogWarning(
                            $"[BugBeacon] R2 업로드 실패 (시도 {attempt}/{MaxRetries}), " +
                            $"{delay:F1}초 후 재시도: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                    }
                }
            }

            Debug.LogError($"[BugBeacon] R2 업로드 최종 실패 (재시도 {MaxRetries}회 초과): {lastException?.Message}");

            return new UploadResult
            {
                Success = false,
                StatusCode = lastStatusCode,
                ErrorMessage = $"업로드 실패 (재시도 초과): {lastException?.Message}",
                BytesUploaded = 0
            };
        }

        // ─── 유틸리티 메서드 ────────────────────────────────────────────────────

        /// <summary>
        /// 파일 확장자로 Content-Type을 자동 감지합니다.
        /// 알 수 없는 확장자는 "application/octet-stream"을 반환합니다.
        /// </summary>
        /// <param name="fileName">파일 이름 또는 경로</param>
        /// <returns>Content-Type 문자열</returns>
        public static string DetectContentType(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "application/octet-stream";

            int dotIndex = fileName.LastIndexOf('.');
            if (dotIndex < 0 || dotIndex >= fileName.Length - 1)
                return "application/octet-stream";

            string extension = fileName.Substring(dotIndex);

            if (ExtensionToContentType.TryGetValue(extension, out string contentType))
                return contentType;

            return "application/octet-stream";
        }

        // ─── 내부 메서드 ────────────────────────────────────────────────────────

        /// <summary>
        /// UnityWebRequest로 PUT 업로드를 수행합니다.
        /// Unity 메인 스레드에서 실행되도록 SynchronizationContext를 사용합니다.
        /// </summary>
        private async Task<UploadResult> PutUploadAsync(
            string url,
            byte[] data,
            string contentType,
            IProgress<float> progress,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<UploadResult>();
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
                    request = UnityWebRequest.Put(url, data);
                    request.SetRequestHeader("Content-Type", contentType);
                    request.timeout = UploadTimeoutSeconds;

                    // 취소 토큰 등록
                    registration = ct.Register(() =>
                    {
                        if (!isDisposed)
                        {
                            try { request?.Abort(); }
                            catch { /* 무시 */ }
                        }
                        tcs.TrySetCanceled();
                    });

                    var operation = request.SendWebRequest();

                    // 진행률 폴링
                    while (!operation.isDone)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled();
                            return;
                        }

                        try
                        {
                            progress?.Report(operation.progress);
                        }
                        catch
                        {
                            /* 진행률 콜백 오류 무시 */
                        }

                        await Task.Yield();
                    }

                    if (ct.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                        return;
                    }

                    int statusCode = (int)request.responseCode;

                    if (statusCode >= 200 && statusCode < 300)
                    {
                        // 최종 진행률 100%
                        try { progress?.Report(1f); }
                        catch { /* 무시 */ }

                        tcs.TrySetResult(new UploadResult
                        {
                            Success = true,
                            StatusCode = statusCode,
                            ErrorMessage = null,
                            BytesUploaded = data.LongLength
                        });
                    }
                    else
                    {
                        string responseText = request.downloadHandler?.text ?? "";
                        tcs.TrySetException(new R2UploadException(
                            statusCode,
                            $"R2 업로드 HTTP {statusCode}: {request.error} / {responseText}"));
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

        // ─── 내부 예외 ──────────────────────────────────────────────────────────

        /// <summary>
        /// R2 업로드 HTTP 오류를 나타내는 예외.
        /// 상태 코드를 포함하여 4xx/5xx 구분에 사용합니다.
        /// </summary>
        private class R2UploadException : Exception
        {
            public int StatusCode { get; }

            public R2UploadException(int statusCode, string message) : base(message)
            {
                StatusCode = statusCode;
            }
        }
    }

    /// <summary>
    /// R2 업로드 결과를 나타내는 클래스.
    /// </summary>
    public class UploadResult
    {
        /// <summary>업로드 성공 여부</summary>
        public bool Success { get; set; }

        /// <summary>HTTP 응답 상태 코드 (0이면 네트워크 오류)</summary>
        public int StatusCode { get; set; }

        /// <summary>오류 메시지 (성공 시 null)</summary>
        public string ErrorMessage { get; set; }

        /// <summary>업로드된 바이트 수</summary>
        public long BytesUploaded { get; set; }
    }
}
