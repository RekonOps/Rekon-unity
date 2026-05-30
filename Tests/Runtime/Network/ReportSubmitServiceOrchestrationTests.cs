using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ReportSubmitService 3단계 오케스트레이션 회귀 안전망 (characterization) 테스트.
    ///
    /// 목적: §7 보안 강화 (특히 B2 checksum/Content-MD5, C1 Bearer 헤더 토큰) 작업 전,
    ///       create-report → R2 업로드 → confirm 의 end-to-end 흐름·헤더·contentType·파일매핑을 핀.
    ///       기존 ReportSubmitServiceTests.cs 는 모델/입력검증/seam존재만 핀하고
    ///       실제 3단계 흐름을 한 번도 검증하지 않으므로 이를 보강합니다.
    ///
    /// 전략: IRekonHttpClient(MockHttpClient) + IR2UploadService(MockR2UploadService) 양쪽을 주입하여
    ///        UnityWebRequest / 실제 R2 / 네트워크 없이 흐름을 검증합니다.
    ///        MockHttpClient 패턴은 LicenseValidatorCapabilityTests.cs 의 것을 복제했습니다.
    ///
    /// 주의: ReportSubmitService 생성자가 RekonSettings.WEB_DASHBOARD_URL 을 읽으므로
    ///        URL prefix 는 하드코딩하지 않고 'endsWith(/api/unity/reports)' 형태로 느슨하게 핀합니다.
    ///
    /// #164 회피: [Test] async Task 패턴은 Unity Test Runner 가 인식하지 못하므로
    ///            동기 [Test] + .GetAwaiter().GetResult() 패턴을 사용합니다.
    ///            MockHttpClient/MockR2UploadService 는 Task.FromResult() 만 사용하므로
    ///            스레드 풀 데드락 없이 안전하게 블로킹 호출 가능합니다.
    /// </summary>
    [TestFixture]
    public class ReportSubmitServiceOrchestrationTests
    {
        // ─── MockHttpClient (호출 순서대로 응답을 큐로 반환) ──────────────────────

        private class MockHttpClient : IRekonHttpClient
        {
            public List<RequestCall> Calls { get; } = new List<RequestCall>();

            /// <summary>POST 호출 순서대로 반환할 응답 큐 (비면 마지막 응답 반복).</summary>
            public Queue<HttpResponse> PostResponses { get; } = new Queue<HttpResponse>();

            /// <summary>큐가 빈 경우 사용할 기본 응답.</summary>
            public HttpResponse DefaultResponse { get; set; } =
                new HttpResponse { StatusCode = 200, Body = "{}" };

            public Task<HttpResponse> GetAsync(
                string url,
                Dictionary<string, string> headers = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(new RequestCall { Method = "GET", Url = url, Headers = headers });
                return Task.FromResult(DefaultResponse);
            }

            public Task<HttpResponse> PostAsync(
                string url,
                string jsonBody,
                Dictionary<string, string> headers = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(new RequestCall { Method = "POST", Url = url, Body = jsonBody, Headers = headers });
                var response = PostResponses.Count > 0 ? PostResponses.Dequeue() : DefaultResponse;
                return Task.FromResult(response);
            }

            public Task<HttpResponse> PutAsync(
                string url,
                byte[] body,
                string contentType,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(new RequestCall { Method = "PUT", Url = url, ContentType = contentType });
                return Task.FromResult(DefaultResponse);
            }

            public class RequestCall
            {
                public string Method;
                public string Url;
                public string Body;
                public string ContentType;
                public Dictionary<string, string> Headers;
            }
        }

        // ─── MockR2UploadService (호출 기록 + 설정 가능한 결과) ───────────────────

        private class MockR2UploadService : IR2UploadService
        {
            public List<UploadCall> Calls { get; } = new List<UploadCall>();

            /// <summary>반환할 업로드 결과 (기본: 성공 200).</summary>
            public UploadResult ResultToReturn { get; set; } =
                new UploadResult { Success = true, StatusCode = 200, BytesUploaded = 0 };

            public Task<UploadResult> UploadFileAsync(
                string presignedUrl,
                byte[] fileData,
                string contentType,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
            {
                Calls.Add(new UploadCall
                {
                    PresignedUrl = presignedUrl,
                    FileData = fileData,
                    ContentType = contentType
                });
                return Task.FromResult(ResultToReturn);
            }

            public class UploadCall
            {
                public string PresignedUrl;
                public byte[] FileData;
                public string ContentType;
            }
        }

        // ─── 진행률 기록기 ────────────────────────────────────────────────────────

        private class ProgressRecorder : IProgress<SubmitProgress>
        {
            public List<SubmitProgress> Reports { get; } = new List<SubmitProgress>();
            public void Report(SubmitProgress value) => Reports.Add(value);
        }

        // ─── 픽스처 / 헬퍼 ────────────────────────────────────────────────────────

        private MockHttpClient _http;
        private MockR2UploadService _r2;

        [SetUp]
        public void SetUp()
        {
            _http = new MockHttpClient();
            _r2 = new MockR2UploadService();
        }

        private static byte[] VideoBytes => new byte[] { 0x00, 0x00, 0x00, 0x18 };

        private static ReportSubmitRequest BuildRequest(string fileName = "capture.mp4", byte[] data = null)
        {
            return new ReportSubmitRequest
            {
                AccessToken = "access-token-xyz",
                WorkspaceId = "ws-uuid-001",
                Title = "재현된 버그",
                Description = "플레이 중 발생",
                Files = new List<FileAttachment>
                {
                    new FileAttachment
                    {
                        FileName = fileName,
                        Data = data ?? VideoBytes,
                        FileType = "video"
                    }
                }
            };
        }

        private static HttpResponse CreateOk(string reportId, string fileName, string fileId, string uploadUrl)
        {
            // JsonUtility 가 파싱할 수 있는 create-report 응답.
            var body =
                "{" +
                $"\"report_id\":\"{reportId}\"," +
                "\"report_files\":[" +
                $"{{\"file_id\":\"{fileId}\",\"type\":\"video\",\"filename\":\"{fileName}\",\"upload_url\":\"{uploadUrl}\"}}" +
                "]," +
                "\"workspace_url\":\"https://example.com/ws\"" +
                "}";
            return new HttpResponse { StatusCode = 200, Body = body };
        }

        private static HttpResponse ConfirmOk(string fileId)
        {
            var body =
                "{" +
                "\"updated_count\":1," +
                $"\"results\":[{{\"file_id\":\"{fileId}\",\"status\":\"confirmed\"}}]" +
                "}";
            return new HttpResponse { StatusCode = 200, Body = body };
        }

        // ─── 정상 흐름 핀 ──────────────────────────────────────────────────────────

        [Test]
        public void SubmitReportAsync_HappyPath_SuccessWithReportId()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            _http.PostResponses.Enqueue(CreateOk("rpt-001", "capture.mp4", "file-001", "https://r2.example.com/put/file-001"));
            _http.PostResponses.Enqueue(ConfirmOk("file-001"));
            _r2.ResultToReturn = new UploadResult { Success = true, StatusCode = 200 };

            var service = new ReportSubmitService(_r2, _http);
            var result = service.SubmitReportAsync(BuildRequest()).GetAwaiter().GetResult();

            Assert.IsTrue(result.Success, "정상 3단계 흐름은 Success=true 여야 합니다.");
            Assert.AreEqual("rpt-001", result.ReportId, "ReportId 가 create 응답과 일치해야 합니다.");
            Assert.IsFalse(result.IsUsageLimitExceeded);
        }

        [Test]
        public void SubmitReportAsync_BearerAndAcceptHeaders_Pinned()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            // C1: Bearer 헤더 토큰 변경 후에도 'Authorization: Bearer {token}' + 'Accept: application/json' 구성 불변 보장.
            _http.PostResponses.Enqueue(CreateOk("rpt-002", "capture.mp4", "file-002", "https://r2.example.com/put/file-002"));
            _http.PostResponses.Enqueue(ConfirmOk("file-002"));

            var service = new ReportSubmitService(_r2, _http);
            service.SubmitReportAsync(BuildRequest()).GetAwaiter().GetResult();

            Assert.GreaterOrEqual(_http.Calls.Count, 1, "최소 1번의 HTTP POST 가 있어야 합니다.");
            var headers = _http.Calls[0].Headers;
            Assert.IsNotNull(headers, "헤더가 null 이 아니어야 합니다.");
            Assert.IsTrue(headers.ContainsKey("Authorization"), "Authorization 헤더가 있어야 합니다.");
            Assert.AreEqual("Bearer access-token-xyz", headers["Authorization"],
                "Authorization 헤더는 'Bearer {accessToken}' 형식이어야 합니다.");
            Assert.IsTrue(headers.ContainsKey("Accept"), "Accept 헤더가 있어야 합니다.");
            Assert.AreEqual("application/json", headers["Accept"], "Accept 헤더는 application/json 이어야 합니다.");
        }

        [Test]
        public void SubmitReportAsync_EndpointOrder_Pinned()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            _http.PostResponses.Enqueue(CreateOk("rpt-003", "capture.mp4", "file-003", "https://r2.example.com/put/file-003"));
            _http.PostResponses.Enqueue(ConfirmOk("file-003"));

            var service = new ReportSubmitService(_r2, _http);
            service.SubmitReportAsync(BuildRequest()).GetAwaiter().GetResult();

            var posts = _http.Calls.FindAll(c => c.Method == "POST");
            Assert.AreEqual(2, posts.Count, "정상 흐름에서 POST 는 정확히 2번(create, confirm) 이어야 합니다.");
            StringAssert.EndsWith("/api/unity/reports", posts[0].Url,
                "1st POST 는 .../api/unity/reports 로 끝나야 합니다.");
            StringAssert.EndsWith("/api/unity/reports/confirm", posts[1].Url,
                "2nd POST 는 .../api/unity/reports/confirm 로 끝나야 합니다.");
        }

        [Test]
        public void SubmitReportAsync_R2UploadArgs_Pinned()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            var fileData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
            const string uploadUrl = "https://r2.example.com/put/file-004";
            _http.PostResponses.Enqueue(CreateOk("rpt-004", "capture.mp4", "file-004", uploadUrl));
            _http.PostResponses.Enqueue(ConfirmOk("file-004"));

            var service = new ReportSubmitService(_r2, _http);
            service.SubmitReportAsync(BuildRequest("capture.mp4", fileData)).GetAwaiter().GetResult();

            Assert.AreEqual(1, _r2.Calls.Count, "R2 업로드는 1번 호출되어야 합니다.");
            Assert.AreEqual(uploadUrl, _r2.Calls[0].PresignedUrl,
                "presignedUrl 은 create 응답의 upload_url 이어야 합니다.");
            Assert.AreEqual("video/mp4", _r2.Calls[0].ContentType,
                "capture.mp4 의 contentType 은 video/mp4 여야 합니다 (DetectContentType).");
            Assert.AreSame(fileData, _r2.Calls[0].FileData,
                "fileData 는 원본 byte[] 참조 그대로 전달되어야 합니다.");
        }

        // ─── 파일 매핑(BuildFileMap) 핀 ────────────────────────────────────────────

        [Test]
        public void SubmitReportAsync_FilenameNotInServerResponse_Fails()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            // 클라 파일명이 서버 응답에 없으면 BuildFileMap 에서 InvalidOperationException →
            // 외부 catch 에서 Success=false 로 처리 (현재 동작).
            _http.PostResponses.Enqueue(CreateOk("rpt-005", "different-name.mp4", "file-005", "https://r2.example.com/put/x"));
            // confirm 응답은 도달하지 않음

            var service = new ReportSubmitService(_r2, _http);
            var result = service.SubmitReportAsync(BuildRequest("capture.mp4")).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "서버 응답에 매칭 파일명이 없으면 Success=false 여야 합니다.");
            Assert.AreEqual(0, _r2.Calls.Count, "매핑 실패 시 R2 업로드는 호출되지 않아야 합니다.");
        }

        // ─── 부분 실패 핀 (업로드 실패) ───────────────────────────────────────────

        [Test]
        public void SubmitReportAsync_UploadFails_PreservesReportId_NoConfirm()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            _http.PostResponses.Enqueue(CreateOk("rpt-006", "capture.mp4", "file-006", "https://r2.example.com/put/file-006"));
            _r2.ResultToReturn = new UploadResult { Success = false, StatusCode = 403, ErrorMessage = "presigned 만료" };

            var service = new ReportSubmitService(_r2, _http);
            var result = service.SubmitReportAsync(BuildRequest()).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "업로드 실패 시 Success=false 여야 합니다.");
            Assert.AreEqual("rpt-006", result.ReportId, "업로드 실패 시에도 ReportId 는 보존되어야 합니다.");

            var posts = _http.Calls.FindAll(c => c.Method == "POST");
            Assert.AreEqual(1, posts.Count, "업로드 실패 시 confirm POST(2nd) 가 발생하지 않아야 합니다.");
        }

        // ─── 429 usage_limit_exceeded 핀 ──────────────────────────────────────────

        [Test]
        public void SubmitReportAsync_429UsageLimit_SetsUsageLimitFields_NoUpload()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            var body =
                "{" +
                "\"error\":\"월간 한도 초과\"," +
                "\"code\":\"usage_limit_exceeded\"," +
                "\"reason\":\"monthly\"," +
                "\"monthly_limit\":100," +
                "\"upgradeUrl\":\"https://rekonops.dev/upgrade\"" +
                "}";
            _http.PostResponses.Enqueue(new HttpResponse { StatusCode = 429, Body = body });

            var service = new ReportSubmitService(_r2, _http);
            var result = service.SubmitReportAsync(BuildRequest()).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.IsUsageLimitExceeded, "429 usage_limit_exceeded 는 IsUsageLimitExceeded=true 여야 합니다.");
            Assert.AreEqual("monthly", result.UsageLimitReason);
            Assert.AreEqual(0, _r2.Calls.Count, "사용량 초과 시 R2 업로드는 호출되지 않아야 합니다.");
        }

        // ─── 4xx (403) create 응답 핀 — 재시도 없이 실패 ─────────────────────────

        [Test]
        public void SubmitReportAsync_403OnCreate_FailsWithoutRetryOrUpload()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            // SendWithRetryAsync 의 AuthBrokerException 4xx 분기 → 재시도 없이 throw → 외부 catch → Success=false.
            _http.PostResponses.Enqueue(new HttpResponse { StatusCode = 403, Body = "{\"error\":\"forbidden\"}" });

            var service = new ReportSubmitService(_r2, _http);
            var result = service.SubmitReportAsync(BuildRequest()).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "403 응답이면 Success=false 여야 합니다.");
            Assert.AreEqual(0, _r2.Calls.Count, "create 실패 시 R2 업로드는 호출되지 않아야 합니다.");

            var posts = _http.Calls.FindAll(c => c.Method == "POST");
            Assert.AreEqual(1, posts.Count, "4xx 는 재시도하지 않으므로 POST 는 1번이어야 합니다.");
        }

        // ─── 진행률 Phase 순서 핀 ──────────────────────────────────────────────────

        [Test]
        public void SubmitReportAsync_ProgressPhases_MonotonicAndOrdered()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            _http.PostResponses.Enqueue(CreateOk("rpt-007", "capture.mp4", "file-007", "https://r2.example.com/put/file-007"));
            _http.PostResponses.Enqueue(ConfirmOk("file-007"));

            var recorder = new ProgressRecorder();
            var service = new ReportSubmitService(_r2, _http);
            service.SubmitReportAsync(BuildRequest(), recorder).GetAwaiter().GetResult();

            Assert.IsNotEmpty(recorder.Reports, "진행률 보고가 있어야 합니다.");

            // Phase 가 CreatingReport → UploadingFiles → ConfirmingUpload → Completed 순으로 등장.
            int idxCreating  = recorder.Reports.FindIndex(p => p.Phase == SubmitPhase.CreatingReport);
            int idxUploading = recorder.Reports.FindIndex(p => p.Phase == SubmitPhase.UploadingFiles);
            int idxConfirm   = recorder.Reports.FindIndex(p => p.Phase == SubmitPhase.ConfirmingUpload);
            int idxCompleted = recorder.Reports.FindIndex(p => p.Phase == SubmitPhase.Completed);

            Assert.GreaterOrEqual(idxCreating, 0, "CreatingReport 단계가 보고되어야 합니다.");
            Assert.Greater(idxUploading, idxCreating, "UploadingFiles 는 CreatingReport 뒤여야 합니다.");
            Assert.Greater(idxConfirm, idxUploading, "ConfirmingUpload 는 UploadingFiles 뒤여야 합니다.");
            Assert.Greater(idxCompleted, idxConfirm, "Completed 는 ConfirmingUpload 뒤여야 합니다.");

            // OverallProgress 단조 비감소 (0 → 1.0)
            float prev = -1f;
            foreach (var r in recorder.Reports)
            {
                Assert.GreaterOrEqual(r.OverallProgress, prev,
                    "OverallProgress 는 단조 비감소여야 합니다.");
                prev = r.OverallProgress;
            }
            Assert.AreEqual(1.0f, recorder.Reports[recorder.Reports.Count - 1].OverallProgress, 0.0001f,
                "마지막 진행률은 1.0 이어야 합니다.");
        }

        // ─── create 응답 방어 분기 핀 ──────────────────────────────────────────────

        [Test]
        public void SubmitReportAsync_EmptyReportFiles_FailsImmediately()
        {
            // #164 회피: async Task → 동기 [Test] + .GetAwaiter().GetResult()
            // report_id 는 있지만 report_files 가 빈 배열 → 즉시 Success=false (방어 분기).
            var body = "{\"report_id\":\"rpt-008\",\"report_files\":[]}";
            _http.PostResponses.Enqueue(new HttpResponse { StatusCode = 200, Body = body });

            var service = new ReportSubmitService(_r2, _http);
            var result = service.SubmitReportAsync(BuildRequest()).GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "report_files 가 비면 Success=false 여야 합니다.");
            Assert.AreEqual(0, _r2.Calls.Count, "파일 정보가 없으면 R2 업로드는 호출되지 않아야 합니다.");
        }
    }
}
