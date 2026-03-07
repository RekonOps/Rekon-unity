using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// create-report Edge Function 호출 + R2 업로드를 조율하는 리포트 제출기.
    /// Unity → [웹 저장] 경로의 핵심 클래스입니다.
    /// </summary>
    public class ReportSubmitter
    {
        // ─── 이벤트 ───────────────────────────────────────────────────────────────

        /// <summary>진행률 이벤트 (progress 0~1, 단계 설명)</summary>
        public event Action<float, string> OnProgressChanged;

        // ─── 상수 ─────────────────────────────────────────────────────────────────

        private const int MaxRetryCount = 3;
        private const float RetryBaseDelaySeconds = 2f;
        private const float RequestTimeoutSeconds = 30f;

        // ─── 내부 상태 ───────────────────────────────────────────────────────────

        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly SessionTokenStore _tokenStore;
        private readonly R2Uploader _r2Uploader;

        // ─── 요청/결과 모델 ──────────────────────────────────────────────────────

        /// <summary>리포트 제출 요청</summary>
        public class SubmitRequest
        {
            public string WorkspaceId;
            public string Title;
            public string Description;
            public string DeviceInfoJson;    // JSON 문자열 (environment_info)
            public List<FileEntry> Files = new List<FileEntry>();
        }

        /// <summary>파일 정보</summary>
        public class FileEntry
        {
            public string Type;         // "video" | "screenshot" | "log"
            public string FileName;
            public string LocalPath;
            public string ContentType;
            public long FileSize;
        }

        /// <summary>제출 결과</summary>
        public class SubmitResult
        {
            public bool Success;
            public string ReportId;
            public string WorkspaceUrl;
            public string ErrorMessage;
            public R2Uploader.UploadResult[] UploadResults;

            /// <summary>
            /// 업로드된 파일의 공개 URL 목록.
            /// 키: 파일 유형(screenshot, log, video), 값: 공개 URL.
            /// </summary>
            public Dictionary<string, string> FileUrls;
        }

        // ─── 응답 모델 (JsonUtility) ─────────────────────────────────────────────

        [Serializable]
        private class CreateReportResponseBase
        {
            public string report_id;
            public string workspace_url;
        }

        [Serializable]
        private class ReportFileInfo
        {
            public string file_id;
            public string type;
            public string filename;
            public string upload_url;
            public string public_url;
        }

        // ─── 생성자 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// ReportSubmitter를 초기화합니다.
        /// </summary>
        public ReportSubmitter(
            string supabaseUrl,
            string supabaseAnonKey,
            SessionTokenStore tokenStore,
            R2Uploader r2Uploader)
        {
            if (string.IsNullOrEmpty(supabaseUrl))
                throw new ArgumentNullException(nameof(supabaseUrl));
            if (string.IsNullOrEmpty(supabaseAnonKey))
                throw new ArgumentNullException(nameof(supabaseAnonKey));

            _supabaseUrl = supabaseUrl.TrimEnd('/');
            _supabaseAnonKey = supabaseAnonKey;
            _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
            _r2Uploader = r2Uploader ?? throw new ArgumentNullException(nameof(r2Uploader));
        }

        // ─── 공개 메서드 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 리포트를 제출합니다 (create-report 호출 → R2 업로드).
        /// </summary>
        public async Task<SubmitResult> SubmitAsync(SubmitRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                // 1단계: Supabase 인증 토큰 확인
                var accessToken = _tokenStore.LoadSupabase();
                if (string.IsNullOrEmpty(accessToken))
                {
                    return new SubmitResult
                    {
                        Success = false,
                        ErrorMessage = "Supabase 로그인이 필요합니다."
                    };
                }

                ReportProgress(0.1f, "리포트 생성 중...");

                // 2단계: create-report 호출
                var createUrl = $"{_supabaseUrl}/functions/v1/create-report";
                var createBody = BuildCreateReportJson(request);
                var responseJson = await SendWithRetryAsync(createUrl, createBody, accessToken, ct);

                // 3단계: 응답 파싱
                var baseResponse = JsonUtility.FromJson<CreateReportResponseBase>(responseJson);
                if (string.IsNullOrEmpty(baseResponse?.report_id))
                {
                    return new SubmitResult
                    {
                        Success = false,
                        ErrorMessage = $"create-report 응답에 report_id가 없습니다: {responseJson}"
                    };
                }

                // report_files 배열 수동 파싱 (JsonUtility 배열 제한 우회)
                var reportFiles = ParseReportFiles(responseJson);

                Debug.Log($"[BugOneTouch] 리포트 생성 완료: {baseResponse.report_id}, 파일 {reportFiles.Count}개");

                // 공개 URL 매핑 구성
                var fileUrls = new Dictionary<string, string>();
                foreach (var rf in reportFiles)
                {
                    if (!string.IsNullOrEmpty(rf.public_url) && !string.IsNullOrEmpty(rf.type))
                    {
                        // 한글 파일 유형 레이블 매핑
                        string label = rf.type switch
                        {
                            "screenshot" => "스크린샷",
                            "log" => "로그",
                            "video" => "영상",
                            _ => rf.type
                        };
                        fileUrls[label] = rf.public_url;
                    }
                }

                ReportProgress(0.3f, "파일 업로드 중...");

                // 4단계: R2 업로드
                R2Uploader.UploadResult[] uploadResults = null;
                if (reportFiles.Count > 0 && request.Files.Count > 0)
                {
                    var uploadTasks = BuildUploadTasks(reportFiles, request.Files);
                    if (uploadTasks.Length > 0)
                    {
                        _r2Uploader.OnUploadProgress += HandleR2Progress;
                        try
                        {
                            uploadResults = await _r2Uploader.UploadFilesAsync(uploadTasks, ct);
                        }
                        finally
                        {
                            _r2Uploader.OnUploadProgress -= HandleR2Progress;
                        }

                        // 업로드 실패 확인
                        int failCount = 0;
                        foreach (var r in uploadResults)
                            if (!r.Success) failCount++;

                        if (failCount > 0)
                            Debug.LogWarning($"[BugOneTouch] {failCount}/{uploadResults.Length}개 파일 업로드 실패");
                    }
                }

                // 업로드 결과 확인: 하나라도 실패하면 부분 성공 처리
                bool allUploadsOk = true;
                if (uploadResults != null)
                {
                    foreach (var r in uploadResults)
                    {
                        if (!r.Success)
                        {
                            allUploadsOk = false;
                            break;
                        }
                    }
                }

                ReportProgress(1f, allUploadsOk ? "완료" : "완료 (일부 파일 업로드 실패)");

                return new SubmitResult
                {
                    Success = allUploadsOk,
                    ReportId = baseResponse.report_id,
                    WorkspaceUrl = baseResponse.workspace_url,
                    UploadResults = uploadResults,
                    FileUrls = fileUrls,
                    ErrorMessage = allUploadsOk ? null : "일부 첨부 파일 업로드에 실패했습니다."
                };
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[BugOneTouch] 리포트 제출 취소됨");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 리포트 제출 실패: {ex.Message}");
                return new SubmitResult
                {
                    Success = false,
                    ErrorMessage = $"제출 실패: {ex.Message}"
                };
            }
        }

        // ─── JSON 빌드 ──────────────────────────────────────────────────────────

        private static string BuildCreateReportJson(SubmitRequest request)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"workspace_id\":\"{EscapeJson(request.WorkspaceId)}\",");
            sb.Append($"\"title\":\"{EscapeJson(request.Title)}\",");
            sb.Append($"\"description\":\"{EscapeJson(request.Description ?? "")}\",");

            // device_info
            if (!string.IsNullOrEmpty(request.DeviceInfoJson))
                sb.Append($"\"device_info\":{request.DeviceInfoJson},");
            else
                sb.Append("\"device_info\":{},");

            // files 배열
            sb.Append("\"files\":[");
            for (int i = 0; i < request.Files.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var f = request.Files[i];
                sb.Append("{");
                sb.Append($"\"type\":\"{EscapeJson(f.Type)}\",");
                sb.Append($"\"filename\":\"{EscapeJson(f.FileName)}\",");
                sb.Append($"\"file_size\":{f.FileSize}");
                sb.Append("}");
            }
            sb.Append("]");

            sb.Append("}");
            return sb.ToString();
        }

        // ─── 응답 파싱 ──────────────────────────────────────────────────────────

        /// <summary>
        /// report_files 배열을 JSON에서 수동 파싱합니다.
        /// JsonUtility의 최상위 배열 역직렬화 제한을 우회합니다.
        /// </summary>
        private static List<ReportFileInfo> ParseReportFiles(string json)
        {
            var result = new List<ReportFileInfo>();

            try
            {
                // "report_files": [...] 영역 추출
                int arrStart = json.IndexOf("\"report_files\":", StringComparison.Ordinal);
                if (arrStart < 0) return result;

                int bracketStart = json.IndexOf('[', arrStart);
                if (bracketStart < 0) return result;

                // 대괄호 매칭으로 배열 끝 찾기
                int depth = 0;
                int bracketEnd = -1;
                for (int i = bracketStart; i < json.Length; i++)
                {
                    if (json[i] == '[') depth++;
                    else if (json[i] == ']') { depth--; if (depth == 0) { bracketEnd = i; break; } }
                }
                if (bracketEnd < 0) return result;

                string arrayJson = json.Substring(bracketStart, bracketEnd - bracketStart + 1);

                // 래퍼로 감싸서 JsonUtility 파싱
                string wrappedJson = "{\"items\":" + arrayJson + "}";
                var wrapper = JsonUtility.FromJson<ReportFilesWrapper>(wrappedJson);
                if (wrapper?.items != null)
                {
                    result.AddRange(wrapper.items);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BugOneTouch] report_files 파싱 실패 (빈 목록으로 진행): {ex.Message}");
            }

            return result;
        }

        [Serializable]
        private class ReportFilesWrapper
        {
            public ReportFileInfo[] items;
        }

        // ─── 업로드 태스크 빌드 ─────────────────────────────────────────────────

        private static R2Uploader.UploadTask[] BuildUploadTasks(
            List<ReportFileInfo> reportFiles,
            List<FileEntry> requestFiles)
        {
            var tasks = new List<R2Uploader.UploadTask>();

            foreach (var rf in reportFiles)
            {
                // filename 매칭으로 로컬 파일 찾기
                FileEntry matchedFile = null;
                foreach (var f in requestFiles)
                {
                    if (f.FileName == rf.filename)
                    {
                        matchedFile = f;
                        break;
                    }
                }

                if (matchedFile != null && !string.IsNullOrEmpty(rf.upload_url))
                {
                    tasks.Add(new R2Uploader.UploadTask
                    {
                        SignedUrl = rf.upload_url,
                        FilePath = matchedFile.LocalPath,
                        FileId = rf.file_id,
                        FileName = rf.filename,
                        ContentType = matchedFile.ContentType
                    });
                }
            }

            return tasks.ToArray();
        }

        // ─── HTTP 통신 ──────────────────────────────────────────────────────────

        private async Task<string> SendWithRetryAsync(
            string url, string jsonBody, string accessToken, CancellationToken ct)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MaxRetryCount)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await SendRequestAsync(url, jsonBody, accessToken, ct);
                }
                catch (AuthBrokerException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
                {
                    throw;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempt++;
                    if (attempt < MaxRetryCount)
                    {
                        float delay = RetryBaseDelaySeconds * Mathf.Pow(2f, attempt - 1);
                        Debug.LogWarning($"[BugOneTouch] create-report 요청 실패 (시도 {attempt}/{MaxRetryCount}), {delay:F1}초 후 재시도: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                    }
                }
            }

            throw new AggregateException($"create-report 요청 최대 재시도 횟수 초과 ({MaxRetryCount}회)", lastException);
        }

        private async Task<string> SendRequestAsync(
            string url, string jsonBody, string accessToken, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>();
            var syncContext = SynchronizationContext.Current;

            void RunOnMainThread(Action action)
            {
                if (syncContext != null) syncContext.Post(_ => action(), null);
                else action();
            }

            RunOnMainThread(async () =>
            {
                UnityWebRequest request = null;
                bool isDisposed = false;
                CancellationTokenRegistration registration = default;

                try
                {
                    var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                    var uploadHandler = new UploadHandlerRaw(bodyBytes);
                    uploadHandler.contentType = "application/json";
                    request = new UnityWebRequest(url, "POST")
                    {
                        uploadHandler = uploadHandler,
                        downloadHandler = new DownloadHandlerBuffer()
                    };

                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Accept", "application/json");
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                    request.timeout = (int)RequestTimeoutSeconds;

                    registration = ct.Register(() =>
                    {
                        if (!isDisposed) { try { request?.Abort(); } catch { } }
                        tcs.TrySetCanceled();
                    });

                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (ct.IsCancellationRequested) { tcs.TrySetCanceled(); return; }
                        await Task.Yield();
                    }

                    if (ct.IsCancellationRequested) { tcs.TrySetCanceled(); return; }

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        tcs.TrySetResult(request.downloadHandler.text);
                    }
                    else
                    {
                        int statusCode = (int)request.responseCode;
                        string responseText = request.downloadHandler?.text ?? "";
                        if (statusCode == 0)
                            tcs.TrySetException(new NetworkException($"네트워크 오류: {request.error}"));
                        else
                            tcs.TrySetException(new AuthBrokerException(statusCode, $"HTTP {statusCode}: {request.error} / {responseText}"));
                    }
                }
                catch (OperationCanceledException) { tcs.TrySetCanceled(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally
                {
                    isDisposed = true;
                    registration.Dispose();
                    request?.Dispose();
                }
            });

            return await tcs.Task;
        }

        // ─── 유틸리티 ───────────────────────────────────────────────────────────

        private void ReportProgress(float progress, string message)
        {
            try { OnProgressChanged?.Invoke(progress, message); }
            catch { }
        }

        private void HandleR2Progress(float progress, string fileName)
        {
            // R2 업로드 진행률을 전체 진행률의 0.3 ~ 0.9 범위로 매핑
            float mapped = 0.3f + progress * 0.6f;
            ReportProgress(mapped, $"업로드 중: {fileName}");
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
