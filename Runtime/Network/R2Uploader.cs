using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// R2 Signed URL을 사용하여 파일을 PUT 업로드하는 클래스.
    /// create-report에서 발급받은 Signed URL에 파일 데이터를 업로드합니다.
    /// </summary>
    public class R2Uploader
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>업로드 진행률 이벤트 (progress 0~1, fileName)</summary>
        public event Action<float, string> OnUploadProgress;

        // ─── 상수 ─────────────────────────────────────────────────────────────────

        private const int MaxRetryCount = 2;
        private const float RetryBaseDelaySeconds = 2f;
        private const int UploadTimeoutSeconds = 120;

        // ─── 결과/요청 모델 ──────────────────────────────────────────────────────

        /// <summary>단일 파일 업로드 결과</summary>
        public class UploadResult
        {
            public bool Success;
            public string FileId;
            public string FileName;
            public string ErrorMessage;
        }

        /// <summary>업로드 작업 단위</summary>
        public class UploadTask
        {
            public string SignedUrl;
            public string FilePath;
            public string FileId;
            public string FileName;
            public string ContentType;
        }

        // ─── 공개 메서드 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 단일 파일을 Signed URL에 PUT 업로드합니다.
        /// </summary>
        public async Task<UploadResult> UploadFileAsync(
            string signedUrl,
            string filePath,
            string fileId,
            string fileName,
            string contentType,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(signedUrl))
                return new UploadResult { Success = false, FileId = fileId, FileName = fileName, ErrorMessage = "Signed URL이 없습니다." };

            if (!File.Exists(filePath))
                return new UploadResult { Success = false, FileId = fileId, FileName = fileName, ErrorMessage = $"파일이 존재하지 않습니다: {filePath}" };

            byte[] fileData;
            try
            {
                fileData = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                return new UploadResult { Success = false, FileId = fileId, FileName = fileName, ErrorMessage = $"파일 읽기 실패: {ex.Message}" };
            }

            int attempt = 0;
            Exception lastException = null;

            while (attempt <= MaxRetryCount)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    await PutUploadAsync(signedUrl, fileData, contentType, fileName, ct);
                    Debug.Log($"[BugBeacon] R2 업로드 완료: {fileName} ({fileData.Length / 1024}KB)");
                    return new UploadResult { Success = true, FileId = fileId, FileName = fileName };
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;
                    if (attempt <= MaxRetryCount)
                    {
                        float delay = RetryBaseDelaySeconds * Mathf.Pow(2f, attempt - 1);
                        Debug.LogWarning($"[BugBeacon] R2 업로드 실패 ({fileName}, 시도 {attempt}/{MaxRetryCount + 1}), {delay:F1}초 후 재시도: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                    }
                }
            }

            return new UploadResult
            {
                Success = false,
                FileId = fileId,
                FileName = fileName,
                ErrorMessage = $"업로드 실패 (재시도 초과): {lastException?.Message}"
            };
        }

        /// <summary>
        /// 복수 파일을 병렬 업로드합니다.
        /// </summary>
        public async Task<UploadResult[]> UploadFilesAsync(UploadTask[] tasks, CancellationToken ct = default)
        {
            if (tasks == null || tasks.Length == 0)
                return Array.Empty<UploadResult>();

            var uploadTasks = new Task<UploadResult>[tasks.Length];
            for (int i = 0; i < tasks.Length; i++)
            {
                var t = tasks[i];
                uploadTasks[i] = UploadFileAsync(t.SignedUrl, t.FilePath, t.FileId, t.FileName, t.ContentType, ct);
            }

            return await Task.WhenAll(uploadTasks);
        }

        // ─── 내부 메서드 ─────────────────────────────────────────────────────────

        /// <summary>
        /// UnityWebRequest로 PUT 업로드를 수행합니다.
        /// </summary>
        private async Task PutUploadAsync(
            string url,
            byte[] data,
            string contentType,
            string fileName,
            CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
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

                    registration = ct.Register(() =>
                    {
                        if (!isDisposed)
                        {
                            try { request?.Abort(); } catch { }
                        }
                        tcs.TrySetCanceled();
                    });

                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled();
                            return;
                        }

                        // 진행률 보고
                        try { OnUploadProgress?.Invoke(operation.progress, fileName); }
                        catch { }

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
                        OnUploadProgress?.Invoke(1f, fileName);
                        tcs.TrySetResult(true);
                    }
                    else
                    {
                        string responseText = request.downloadHandler?.text ?? "";
                        tcs.TrySetException(new Exception(
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

            await tcs.Task;
        }
    }
}
