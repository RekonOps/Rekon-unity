using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 버그 리포트 제출 서비스.
    /// Web API 프록시(/api/unity/reports)를 통해 create-report → R2 업로드 → confirm-upload
    /// 3단계를 순차적으로 처리하며, 전체 진행률을 통합 관리합니다.
    /// </summary>
    public class ReportSubmitService
    {
        // ─── 상수 ───────────────────────────────────────────────────────────────

        private const int RequestTimeoutSeconds = 30;
        private const int MaxRetries = 3;
        private const float RetryBaseDelaySec = 2f;

        // 진행률 구간 (0~1)
        private const float PhaseCreateEnd = 0.10f;        // create-report: 0% ~ 10%
        private const float PhaseUploadStart = 0.10f;      // R2 업로드: 10% ~ 90%
        private const float PhaseUploadEnd = 0.90f;
        private const float PhaseConfirmEnd = 1.00f;       // confirm-upload: 90% ~ 100%

        // ─── 의존성 ─────────────────────────────────────────────────────────────

        private readonly R2UploadService _uploadService;

        /// <summary>Web API 프록시 기본 URL (BugBeaconSettings.WEB_DASHBOARD_URL)</summary>
        private readonly string _webApiBaseUrl;

        // ─── JSON 응답 모델 ─────────────────────────────────────────────────────

        /// <summary>create-report 응답의 개별 파일 정보</summary>
        [Serializable]
        private class CreateReportFileResponse
        {
            public string file_id;
            public string type;
            public string filename;
            public string upload_url;
        }

        /// <summary>create-report 응답 모델</summary>
        [Serializable]
        private class CreateReportResponse
        {
            public string report_id;
            public CreateReportFileResponse[] report_files;
            public string workspace_url;
        }

        /// <summary>confirm-upload 응답의 개별 파일 결과</summary>
        [Serializable]
        private class ConfirmFileResult
        {
            public string file_id;
            public string status;
        }

        /// <summary>confirm-upload 응답 모델</summary>
        [Serializable]
        private class ConfirmUploadResponse
        {
            public int updated_count;
            public ConfirmFileResult[] results;
            public string warning;
        }

        /// <summary>API 에러 응답 모델</summary>
        [Serializable]
        private class ErrorResponse
        {
            public string error;
            public string code;
            /// <summary>사용량 초과 시 제한 유형: "monthly"</summary>
            public string reason;
            /// <summary>업그레이드 안내 URL</summary>
            public string upgradeUrl;
        }

        // ─── 생성자 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// ReportSubmitService를 초기화합니다.
        /// Web API 프록시(WEB_DASHBOARD_URL)를 통해 리포트를 제출합니다.
        /// </summary>
        /// <param name="uploadService">R2 업로드 서비스</param>
        public ReportSubmitService(R2UploadService uploadService)
        {
            _uploadService = uploadService ?? throw new ArgumentNullException(nameof(uploadService));

            // Web 프록시 기본 URL — 상수에서 직접 읽어 Supabase 설정 의존성 제거
            _webApiBaseUrl = BugBeaconSettings.WEB_DASHBOARD_URL.TrimEnd('/');
        }

        // ─── 공개 메서드 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 리포트를 제출합니다 (3단계 통합).
        /// 1. create-report: 리포트 메타데이터 생성 + Signed URL 발급
        /// 2. R2 업로드: 각 파일을 Signed URL로 업로드
        /// 3. confirm-upload: 업로드 완료 확인
        /// </summary>
        /// <param name="request">제출 요청 정보</param>
        /// <param name="progress">진행률 콜백</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>제출 결과</returns>
        public async Task<SubmitResult> SubmitReportAsync(
            ReportSubmitRequest request,
            IProgress<SubmitProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            // 입력 검증
            if (request == null)
                return FailResult("요청 정보가 null입니다.");
            if (string.IsNullOrEmpty(request.AccessToken))
                return FailResult("AccessToken이 비어있습니다.");
            if (string.IsNullOrEmpty(request.WorkspaceId))
                return FailResult("WorkspaceId가 비어있습니다.");
            if (string.IsNullOrEmpty(request.Title))
                return FailResult("제목이 비어있습니다.");
            if (request.Files == null || request.Files.Count == 0)
                return FailResult("첨부 파일이 없습니다.");

            try
            {
                // ── 1단계: create-report (0% ~ 10%) ──────────────────────────────
                ReportProgress(progress, SubmitPhase.CreatingReport, 0f, "리포트 생성 중...");

                var createResponse = await CallCreateReportAsync(request, cancellationToken);

                if (string.IsNullOrEmpty(createResponse.report_id))
                    return FailResult("create-report 응답에 report_id가 없습니다.");

                if (createResponse.report_files == null || createResponse.report_files.Length == 0)
                    return FailResult("create-report 응답에 파일 정보가 없습니다.");

                string reportId = createResponse.report_id;
                Debug.Log($"[BugBeacon] 리포트 생성 완료: {reportId} (파일 {createResponse.report_files.Length}개)");

                ReportProgress(progress, SubmitPhase.CreatingReport, PhaseCreateEnd, "리포트 생성 완료");

                // ── 2단계: R2 업로드 (10% ~ 90%) ─────────────────────────────────
                ReportProgress(progress, SubmitPhase.UploadingFiles, PhaseUploadStart, "파일 업로드 시작...");

                var fileMap = BuildFileMap(request.Files, createResponse.report_files);
                var uploadedFileIds = new List<string>();
                int totalFiles = fileMap.Count;
                int completedFiles = 0;

                foreach (var entry in fileMap)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileInfo = entry.Key;
                    var serverFile = entry.Value;

                    string contentType = R2UploadService.DetectContentType(fileInfo.FileName);

                    // 개별 파일 진행률을 전체 진행률로 환산
                    int currentFileIndex = completedFiles;
                    var fileProgress = new Progress<float>(filePct =>
                    {
                        float fileContribution = (PhaseUploadEnd - PhaseUploadStart) / totalFiles;
                        float overall = PhaseUploadStart + (currentFileIndex * fileContribution)
                                        + (filePct * fileContribution);
                        ReportProgress(progress, SubmitPhase.UploadingFiles, overall,
                            $"파일 업로드 중... ({currentFileIndex + 1}/{totalFiles}) {fileInfo.FileName}");
                    });

                    var uploadResult = await _uploadService.UploadFileAsync(
                        serverFile.upload_url,
                        fileInfo.Data,
                        contentType,
                        fileProgress,
                        cancellationToken);

                    if (!uploadResult.Success)
                    {
                        Debug.LogError($"[BugBeacon] 파일 업로드 실패: {fileInfo.FileName} - {uploadResult.ErrorMessage}");
                        return FailResult($"파일 업로드 실패 ({fileInfo.FileName}): {uploadResult.ErrorMessage}", reportId);
                    }

                    uploadedFileIds.Add(serverFile.file_id);
                    completedFiles++;

                    Debug.Log($"[BugBeacon] 파일 업로드 완료: {fileInfo.FileName} ({completedFiles}/{totalFiles})");
                }

                ReportProgress(progress, SubmitPhase.UploadingFiles, PhaseUploadEnd, "파일 업로드 완료");

                // ── 3단계: confirm-upload (90% ~ 100%) ───────────────────────────
                ReportProgress(progress, SubmitPhase.ConfirmingUpload, PhaseUploadEnd, "업로드 확인 중...");

                var confirmResponse = await CallConfirmUploadAsync(
                    request.AccessToken, reportId, uploadedFileIds, cancellationToken);

                if (confirmResponse == null)
                    return FailResult("confirm-upload 응답이 null입니다.", reportId);

                if (!string.IsNullOrEmpty(confirmResponse.warning))
                    Debug.LogWarning($"[BugBeacon] confirm-upload 경고: {confirmResponse.warning}");

                // 확인 결과 검증
                int confirmedCount = 0;
                if (confirmResponse.results != null)
                {
                    foreach (var result in confirmResponse.results)
                    {
                        if (result.status == "confirmed" || result.status == "r2_skipped")
                            confirmedCount++;
                        else
                            Debug.LogWarning($"[BugBeacon] 파일 확인 실패: {result.file_id} -> {result.status}");
                    }
                }

                Debug.Log($"[BugBeacon] 업로드 확인 완료: {confirmedCount}/{uploadedFileIds.Count}개 파일 확인됨");

                ReportProgress(progress, SubmitPhase.Completed, PhaseConfirmEnd, "리포트 제출 완료");

                return new SubmitResult
                {
                    Success = true,
                    ReportId = reportId,
                    ErrorMessage = null
                };
            }
            catch (UsageLimitExceededException ex)
            {
                // 429 사용량 초과 전용 처리 — pending 큐 등록 없이 사용자에게 안내
                Debug.LogWarning($"[BugBeacon] 사용량 한도 초과: reason={ex.LimitReason}, upgradeUrl={ex.UpgradeUrl}");
                ReportProgress(progress, SubmitPhase.Failed, 0f, "사용량 한도 초과");
                return new SubmitResult
                {
                    Success = false,
                    IsUsageLimitExceeded = true,
                    UsageLimitReason = ex.LimitReason,
                    UpgradeUrl = ex.UpgradeUrl,
                    ErrorMessage = ex.Message
                };
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[BugBeacon] 리포트 제출이 취소되었습니다.");
                ReportProgress(progress, SubmitPhase.Failed, 0f, "제출 취소됨");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugBeacon] 리포트 제출 실패: {ex.Message}");
                ReportProgress(progress, SubmitPhase.Failed, 0f, $"제출 실패: {ex.Message}");
                return FailResult($"리포트 제출 실패: {ex.Message}");
            }
        }

        // ─── 내부 메서드: API 호출 ──────────────────────────────────────────────

        /// <summary>
        /// create-report Web API 프록시를 호출합니다.
        /// </summary>
        private async Task<CreateReportResponse> CallCreateReportAsync(
            ReportSubmitRequest request,
            CancellationToken ct)
        {
            // Web API 프록시 엔드포인트 — Supabase Edge Function을 직접 호출하지 않음
            var url = $"{_webApiBaseUrl}/api/unity/reports";

            // JSON 본문 수동 구성 (JsonUtility는 직렬화 제약이 있으므로)
            var filesJson = new StringBuilder("[");
            for (int i = 0; i < request.Files.Count; i++)
            {
                var file = request.Files[i];
                if (i > 0) filesJson.Append(",");
                filesJson.Append("{");
                filesJson.Append($"\"type\":\"{EscapeJson(file.FileType)}\",");
                filesJson.Append($"\"filename\":\"{EscapeJson(file.FileName)}\",");
                filesJson.Append($"\"file_size\":{file.Data.Length}");
                filesJson.Append("}");
            }
            filesJson.Append("]");

            var bodyJson = "{" +
                $"\"workspace_id\":\"{EscapeJson(request.WorkspaceId)}\"," +
                $"\"title\":\"{EscapeJson(request.Title)}\"," +
                $"\"description\":\"{EscapeJson(request.Description ?? "")}\"," +
                $"\"files\":{filesJson}" +
                "}";

            var responseJson = await SendWithRetryAsync("POST", url, bodyJson, request.AccessToken, ct);

            // 응답 JSON 파싱 및 필수 필드 검증
            var createResponse = JsonUtility.FromJson<CreateReportResponse>(responseJson);
            if (createResponse == null || string.IsNullOrEmpty(createResponse.report_id))
            {
                Debug.LogError($"[BugBeacon] 리포트 생성 응답 파싱 실패. 응답: {responseJson}");
                throw new System.Exception("리포트 생성 응답이 올바르지 않습니다 (report_id 누락 또는 파싱 실패)");
            }

            return createResponse;
        }

        /// <summary>
        /// confirm-upload Web API 프록시를 호출합니다.
        /// </summary>
        private async Task<ConfirmUploadResponse> CallConfirmUploadAsync(
            string accessToken,
            string reportId,
            List<string> fileIds,
            CancellationToken ct)
        {
            // Web API 프록시 엔드포인트 — Supabase Edge Function을 직접 호출하지 않음
            var url = $"{_webApiBaseUrl}/api/unity/reports/confirm";

            var fileIdsJson = new StringBuilder("[");
            for (int i = 0; i < fileIds.Count; i++)
            {
                if (i > 0) fileIdsJson.Append(",");
                fileIdsJson.Append($"\"{EscapeJson(fileIds[i])}\"");
            }
            fileIdsJson.Append("]");

            var bodyJson = "{" +
                $"\"report_id\":\"{EscapeJson(reportId)}\"," +
                $"\"file_ids\":{fileIdsJson}" +
                "}";

            var responseJson = await SendWithRetryAsync("POST", url, bodyJson, accessToken, ct);
            return JsonUtility.FromJson<ConfirmUploadResponse>(responseJson);
        }

        // ─── 내부 메서드: HTTP 통신 ─────────────────────────────────────────────

        /// <summary>
        /// 지수 백오프로 HTTP 요청을 재시도합니다.
        /// </summary>
        private async Task<string> SendWithRetryAsync(
            string method,
            string url,
            string jsonBody,
            string accessToken,
            CancellationToken ct)
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt < MaxRetries)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    return await SendRequestAsync(method, url, jsonBody, accessToken, ct);
                }
                catch (UsageLimitExceededException)
                {
                    // 429 사용량 초과는 재시도하지 않음
                    throw;
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

                    if (attempt < MaxRetries)
                    {
                        float delay = RetryBaseDelaySec * Mathf.Pow(2f, attempt - 1);
                        Debug.LogWarning($"[BugBeacon] API 요청 실패 (시도 {attempt}/{MaxRetries}), " +
                                         $"{delay:F1}초 후 재시도: {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                    }
                }
            }

            throw new AggregateException(
                $"API 요청 최대 재시도 횟수 초과 ({MaxRetries}회)", lastException);
        }

        /// <summary>
        /// UnityWebRequest로 단일 HTTP 요청을 전송합니다.
        /// Bearer 토큰 인증만 사용합니다 (apikey 헤더는 Web 프록시가 대신 처리).
        /// </summary>
        private async Task<string> SendRequestAsync(
            string method,
            string url,
            string jsonBody,
            string accessToken,
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
                    var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                    var uploadHandler = new UploadHandlerRaw(bodyBytes);
                    uploadHandler.contentType = "application/json";
                    request = new UnityWebRequest(url, method)
                    {
                        uploadHandler = uploadHandler,
                        downloadHandler = new DownloadHandlerBuffer()
                    };

                    // 헤더 설정
                    // apikey 헤더는 Web 프록시가 Supabase 호출 시 대신 추가함
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Accept", "application/json");
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

                    request.timeout = RequestTimeoutSeconds;

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

                        // 에러 응답 본문에서 상세 메시지 추출 시도
                        string detailMessage = errorMessage;
                        string usageLimitReason = null;
                        string upgradeUrl = null;
                        try
                        {
                            var errorObj = JsonUtility.FromJson<ErrorResponse>(responseText);
                            if (!string.IsNullOrEmpty(errorObj?.error))
                                detailMessage = errorObj.error;

                            // 429 사용량 초과 전용 필드 추출
                            if (statusCode == 429 && errorObj?.code == "usage_limit_exceeded")
                            {
                                usageLimitReason = errorObj.reason;   // "monthly"
                                upgradeUrl = errorObj.upgradeUrl;
                            }
                        }
                        catch { /* JSON 파싱 실패 시 기본 에러 메시지 사용 */ }

                        if (statusCode == 0)
                        {
                            tcs.TrySetException(new NetworkException(
                                $"네트워크 오류: {detailMessage}"));
                        }
                        else if (statusCode == 429 && usageLimitReason != null)
                        {
                            // 사용량 초과 전용 예외: reason + upgradeUrl 포함
                            tcs.TrySetException(new UsageLimitExceededException(
                                usageLimitReason,
                                upgradeUrl,
                                $"HTTP 429: {detailMessage}"));
                        }
                        else
                        {
                            tcs.TrySetException(new AuthBrokerException(
                                statusCode,
                                $"HTTP {statusCode}: {detailMessage}"));
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

        // ─── 유틸리티 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 클라이언트 파일 목록과 서버 응답 파일 목록을 매칭합니다.
        /// 파일 이름 기준으로 매핑하며, 매칭되지 않는 파일이 있으면 예외를 발생시킵니다.
        /// </summary>
        private Dictionary<FileAttachment, CreateReportFileResponse> BuildFileMap(
            List<FileAttachment> clientFiles,
            CreateReportFileResponse[] serverFiles)
        {
            var map = new Dictionary<FileAttachment, CreateReportFileResponse>();
            var serverLookup = new Dictionary<string, CreateReportFileResponse>();

            // 서버 응답 파일을 filename 기준으로 인덱싱
            foreach (var sf in serverFiles)
            {
                if (!string.IsNullOrEmpty(sf.filename))
                    serverLookup[sf.filename] = sf;
            }

            foreach (var clientFile in clientFiles)
            {
                if (serverLookup.TryGetValue(clientFile.FileName, out var matched))
                {
                    map[clientFile] = matched;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"서버 응답에 파일 '{clientFile.FileName}'에 대한 upload_url이 없습니다.");
                }
            }

            return map;
        }

        /// <summary>진행률을 보고합니다.</summary>
        private static void ReportProgress(
            IProgress<SubmitProgress> progress,
            SubmitPhase phase,
            float overall,
            string message)
        {
            try
            {
                progress?.Report(new SubmitProgress
                {
                    Phase = phase,
                    OverallProgress = Mathf.Clamp01(overall),
                    StatusMessage = message
                });
            }
            catch
            {
                /* 진행률 콜백 오류 무시 */
            }
        }

        /// <summary>실패 결과를 생성합니다.</summary>
        private static SubmitResult FailResult(string errorMessage, string reportId = null)
        {
            Debug.LogError($"[BugBeacon] {errorMessage}");
            return new SubmitResult
            {
                Success = false,
                ReportId = reportId,
                ErrorMessage = errorMessage
            };
        }

        /// <summary>JSON 문자열 이스케이프 처리.</summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    // ─── 요청/응답 모델 ─────────────────────────────────────────────────────────

    /// <summary>리포트 제출 요청 모델</summary>
    public class ReportSubmitRequest
    {
        /// <summary>Supabase Auth 토큰</summary>
        public string AccessToken { get; set; }

        /// <summary>워크스페이스 ID (UUID)</summary>
        public string WorkspaceId { get; set; }

        /// <summary>버그 리포트 제목</summary>
        public string Title { get; set; }

        /// <summary>버그 리포트 설명 (선택사항)</summary>
        public string Description { get; set; }

        /// <summary>첨부 파일 목록</summary>
        public List<FileAttachment> Files { get; set; }
    }

    /// <summary>첨부 파일 정보</summary>
    public class FileAttachment
    {
        /// <summary>파일 이름 (예: capture.mp4, screen01.png)</summary>
        public string FileName { get; set; }

        /// <summary>파일 데이터 (바이트 배열)</summary>
        public byte[] Data { get; set; }

        /// <summary>파일 유형: "screenshot", "video", "log"</summary>
        public string FileType { get; set; }
    }

    /// <summary>제출 진행률 정보</summary>
    public class SubmitProgress
    {
        /// <summary>현재 단계</summary>
        public SubmitPhase Phase { get; set; }

        /// <summary>전체 진행률 (0~1)</summary>
        public float OverallProgress { get; set; }

        /// <summary>상태 메시지</summary>
        public string StatusMessage { get; set; }
    }

    /// <summary>제출 단계</summary>
    public enum SubmitPhase
    {
        /// <summary>리포트 생성 중 (create-report 호출)</summary>
        CreatingReport,

        /// <summary>파일 업로드 중 (R2 Signed URL PUT)</summary>
        UploadingFiles,

        /// <summary>업로드 확인 중 (confirm-upload 호출)</summary>
        ConfirmingUpload,

        /// <summary>제출 완료</summary>
        Completed,

        /// <summary>제출 실패</summary>
        Failed
    }

    /// <summary>제출 결과</summary>
    public class SubmitResult
    {
        /// <summary>제출 성공 여부</summary>
        public bool Success { get; set; }

        /// <summary>생성된 리포트 ID (UUID). 1단계 성공 후 실패 시에도 포함될 수 있음.</summary>
        public string ReportId { get; set; }

        /// <summary>오류 메시지 (성공 시 null)</summary>
        public string ErrorMessage { get; set; }

        /// <summary>429 사용량 초과 에러 여부</summary>
        public bool IsUsageLimitExceeded { get; set; }

        /// <summary>사용량 초과 유형: "monthly" (IsUsageLimitExceeded가 true일 때만 유효)</summary>
        public string UsageLimitReason { get; set; }

        /// <summary>업그레이드 안내 URL (IsUsageLimitExceeded가 true일 때만 유효)</summary>
        public string UpgradeUrl { get; set; }
    }

    /// <summary>
    /// 429 사용량 초과 예외.
    /// create-report API에서 { code: "usage_limit_exceeded", reason: "monthly" } 응답 시 발생합니다.
    /// </summary>
    public class UsageLimitExceededException : Exception
    {
        /// <summary>초과 유형: "monthly"</summary>
        public string LimitReason { get; }

        /// <summary>업그레이드 안내 URL</summary>
        public string UpgradeUrl { get; }

        public UsageLimitExceededException(string limitReason, string upgradeUrl, string message)
            : base(message)
        {
            LimitReason = limitReason ?? "";
            UpgradeUrl = upgradeUrl ?? "";
        }
    }
}
