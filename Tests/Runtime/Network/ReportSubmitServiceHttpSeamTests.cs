using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ReportSubmitService HTTP seam 단위 테스트.
    ///
    /// IRekonHttpClient mock 주입을 통해 create-report / confirm-upload 의
    /// HTTP 호출 경로를 UnityWebRequest 없이 검증합니다.
    ///
    /// 사용 헬퍼:
    ///   - MockRekonHttpClient (Tests/Runtime/Helpers/MockRekonHttpClient.cs)
    ///   - MockR2UploadService (파일 로컬 정의 — 기존 Capability 테스트와 동일 패턴)
    /// </summary>
    [TestFixture]
    public class ReportSubmitServiceHttpSeamTests
    {
        // ─── Mock IR2UploadService ─────────────────────────────────────────────

        /// <summary>IR2UploadService 테스트용 mock (항상 성공 반환)</summary>
        private class MockR2UploadService : IR2UploadService
        {
            public List<UploadCall> Calls { get; } = new List<UploadCall>();
            public UploadResult ResultToReturn { get; set; } = new UploadResult
            {
                Success = true,
                StatusCode = 200,
                ErrorMessage = null,
                BytesUploaded = 0
            };
            public Exception ExceptionToThrow { get; set; }

            public async Task<UploadResult> UploadFileAsync(
                string presignedUrl,
                byte[] fileData,
                string contentType,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls.Add(new UploadCall { PresignedUrl = presignedUrl, ContentType = contentType });
                progress?.Report(1f);
                await Task.CompletedTask;
                if (ExceptionToThrow != null) throw ExceptionToThrow;
                return ResultToReturn;
            }

            public class UploadCall
            {
                public string PresignedUrl;
                public string ContentType;
            }
        }

        // ─── 헬퍼 ─────────────────────────────────────────────────────────────

        /// <summary>유효한 단일 파일 요청 생성</summary>
        private static ReportSubmitRequest BuildValidRequest(string token = "test-access-token")
        {
            return new ReportSubmitRequest
            {
                AccessToken = token,
                WorkspaceId = "ws-test-001",
                Title = "HTTP seam 테스트 리포트",
                Description = "단위 테스트용",
                Files = new List<FileAttachment>
                {
                    new FileAttachment
                    {
                        FileName = "capture.mp4",
                        Data = new byte[] { 1, 2, 3 },
                        FileType = "video"
                    }
                }
            };
        }

        /// <summary>create-report 정상 응답 JSON (파일명 일치 필수)</summary>
        private static string CreateReportOkJson(string reportId = "rpt-test-001")
        {
            return "{\"report_id\":\"" + reportId + "\"," +
                   "\"report_files\":[{\"file_id\":\"fid-001\",\"type\":\"video\"," +
                   "\"filename\":\"capture.mp4\",\"upload_url\":\"https://r2.example.com/upload/fid-001\"}]," +
                   "\"workspace_url\":\"https://rekonops.dev/ws/ws-test-001\"}";
        }

        /// <summary>confirm-upload 정상 응답 JSON</summary>
        private static string ConfirmUploadOkJson()
        {
            return "{\"updated_count\":1,\"results\":[{\"file_id\":\"fid-001\",\"status\":\"confirmed\"}]}";
        }

        // ─── T1: create-report 200 OK 정상 흐름 ───────────────────────────────

        [Test]
        public async Task SubmitReportAsync_create_report_200_정상흐름_POST_Authorization_헤더_검증()
        {
            // Arrange
            var mockUpload = new MockR2UploadService();
            var mockHttp = new MockRekonHttpClient();
            // create-report → 정상 응답
            mockHttp.SetResponseFor("/api/unity/reports/confirm", new HttpResponse
            {
                StatusCode = 200,
                Body = ConfirmUploadOkJson()
            });
            // 기본 응답 = create-report 용 (confirm URL이 더 먼저 매칭되므로 순서 중요)
            mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = CreateReportOkJson()
            };

            var service = new ReportSubmitService(mockUpload, mockHttp);
            const string token = "bearer-test-token-abc";

            // Act
            var result = await service.SubmitReportAsync(BuildValidRequest(token));

            // Assert — HTTP 호출 횟수 (create + confirm = 2)
            Assert.AreEqual(2, mockHttp.Calls.Count,
                "create-report + confirm-upload 총 2번 POST 호출이 있어야 합니다.");

            // create-report 호출 검증
            var createCall = mockHttp.Calls[0];
            Assert.AreEqual("POST", createCall.Method, "create-report 는 POST여야 합니다.");
            StringAssert.Contains("/api/unity/reports", createCall.Url,
                "create-report URL 에 /api/unity/reports 가 포함되어야 합니다.");
            Assert.IsNotNull(createCall.Headers, "헤더가 전송되어야 합니다.");
            Assert.IsTrue(createCall.Headers.ContainsKey("Authorization"),
                "Authorization 헤더가 있어야 합니다.");
            Assert.AreEqual($"Bearer {token}", createCall.Headers["Authorization"],
                "Authorization 헤더 값이 Bearer {token} 형식이어야 합니다.");
            Assert.IsTrue(createCall.Headers.ContainsKey("Accept"),
                "Accept 헤더가 있어야 합니다.");

            // 결과 검증
            Assert.IsTrue(result.Success, "정상 흐름에서 Success=true여야 합니다.");
            Assert.AreEqual("rpt-test-001", result.ReportId, "ReportId가 응답에서 파싱되어야 합니다.");
        }

        // ─── T2: create-report 429 usage_limit_exceeded ───────────────────────

        [Test]
        public async Task SubmitReportAsync_create_report_429_usage_limit_exceeded_필드_추출()
        {
            // Arrange
            var mockUpload = new MockR2UploadService();
            var mockHttp = new MockRekonHttpClient();
            mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 429,
                Body = "{\"error\":\"월간 리포트 한도 초과\"," +
                       "\"code\":\"usage_limit_exceeded\"," +
                       "\"reason\":\"monthly\"," +
                       "\"upgradeUrl\":\"https://rekonops.dev/plans\"," +
                       "\"monthly_limit\":3}"
            };

            var service = new ReportSubmitService(mockUpload, mockHttp);

            // Act
            var result = await service.SubmitReportAsync(BuildValidRequest());

            // Assert — HTTP 호출은 1번 (재시도 없음 — 429는 재시도 안 함)
            Assert.AreEqual(1, mockHttp.Calls.Count,
                "429 usage_limit_exceeded 는 재시도 없이 1번만 호출되어야 합니다.");

            // R2 업로드 호출 없음
            Assert.AreEqual(0, mockUpload.Calls.Count,
                "create-report 실패 시 R2 업로드가 호출되면 안 됩니다.");

            // SubmitResult 검증
            Assert.IsFalse(result.Success, "Success=false여야 합니다.");
            Assert.IsTrue(result.IsUsageLimitExceeded,
                "IsUsageLimitExceeded=true여야 합니다.");
            Assert.AreEqual("monthly", result.UsageLimitReason,
                "UsageLimitReason이 'monthly'여야 합니다.");
            Assert.AreEqual(3, result.MonthlyLimit,
                "MonthlyLimit이 3이어야 합니다.");
            Assert.AreEqual("https://rekonops.dev/plans", result.UpgradeUrl,
                "UpgradeUrl이 응답에서 파싱되어야 합니다.");
        }

        // ─── T3: create-report 401 — 재시도 없음 ────────────────────────────

        [Test]
        public async Task SubmitReportAsync_create_report_401_재시도_없이_1번만_호출()
        {
            // Arrange
            var mockUpload = new MockR2UploadService();
            var mockHttp = new MockRekonHttpClient();
            mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 401,
                Body = "{\"error\":\"인증 실패\",\"code\":\"unauthorized\"}"
            };

            var service = new ReportSubmitService(mockUpload, mockHttp);

            // Act
            var result = await service.SubmitReportAsync(BuildValidRequest());

            // Assert — 4xx 는 재시도 없음 (SendWithRetryAsync 정책)
            Assert.AreEqual(1, mockHttp.Calls.Count,
                "401 에러는 재시도 없이 1번만 호출되어야 합니다.");
            Assert.AreEqual(0, mockUpload.Calls.Count,
                "create-report 401 실패 시 R2 업로드가 호출되면 안 됩니다.");

            Assert.IsFalse(result.Success, "Success=false여야 합니다.");
            Assert.IsFalse(result.IsUsageLimitExceeded,
                "401 에러는 IsUsageLimitExceeded=false여야 합니다.");
        }

        // ─── T4: create-report 5xx — MaxRetries(3) 재시도 ───────────────────

        [Test]
        public async Task SubmitReportAsync_create_report_5xx_MaxRetries_3번_호출()
        {
            // Arrange
            var mockUpload = new MockR2UploadService();
            var mockHttp = new MockRekonHttpClient();
            // 500 응답 — AggregateException 유발 (재시도 3회)
            mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 500,
                Body = "{\"error\":\"서버 내부 오류\"}"
            };

            var service = new ReportSubmitService(mockUpload, mockHttp);

            // Act
            var result = await service.SubmitReportAsync(BuildValidRequest());

            // Assert — MaxRetries = 3 (while attempt < MaxRetries)
            Assert.AreEqual(3, mockHttp.Calls.Count,
                "5xx 에러는 MaxRetries(3)번 호출 후 실패해야 합니다.");
            Assert.IsFalse(result.Success, "최대 재시도 후 Success=false여야 합니다.");
            Assert.AreEqual(0, mockUpload.Calls.Count,
                "create-report 5xx 실패 시 R2 업로드가 호출되면 안 됩니다.");
        }

        // ─── T5: confirm-upload 정상 흐름 ────────────────────────────────────

        [Test]
        public async Task SubmitReportAsync_confirm_upload_정상흐름_URL_및_ReportId_검증()
        {
            // Arrange
            var mockUpload = new MockR2UploadService();
            var mockHttp = new MockRekonHttpClient();
            // create-report → 정상 응답
            mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = CreateReportOkJson("rpt-confirm-test-001")
            };
            // confirm-upload → 정상 응답
            mockHttp.SetResponseFor("/api/unity/reports/confirm", new HttpResponse
            {
                StatusCode = 200,
                Body = ConfirmUploadOkJson()
            });

            var service = new ReportSubmitService(mockUpload, mockHttp);

            // Act
            var result = await service.SubmitReportAsync(BuildValidRequest());

            // Assert
            Assert.IsTrue(result.Success, "전체 흐름 성공 시 Success=true여야 합니다.");
            Assert.AreEqual("rpt-confirm-test-001", result.ReportId,
                "ReportId가 create-report 응답에서 파싱되어야 합니다.");

            // confirm-upload 호출 검증
            Assert.AreEqual(2, mockHttp.Calls.Count,
                "create-report + confirm-upload 총 2번 호출이 있어야 합니다.");
            var confirmCall = mockHttp.Calls[1];
            Assert.AreEqual("POST", confirmCall.Method, "confirm-upload 는 POST여야 합니다.");
            StringAssert.Contains("/api/unity/reports/confirm", confirmCall.Url,
                "confirm-upload URL 에 /api/unity/reports/confirm 이 포함되어야 합니다.");

            // confirm body 에 report_id 포함 검증
            Assert.IsNotNull(confirmCall.Body, "confirm-upload body 가 있어야 합니다.");
            StringAssert.Contains("rpt-confirm-test-001", confirmCall.Body,
                "confirm body 에 report_id 가 포함되어야 합니다.");
        }

        // ─── T6: IRekonHttpClient mock 주입 생성자 호환성 검증 ───────────────

        [Test]
        public void ReportSubmitService_IRekonHttpClient_mock_주입_성공()
        {
            // httpClient 파라미터 옵셔널 → 기존 caller(RekonBootstrap) 0 변경 확인
            var mockUpload = new MockR2UploadService();
            var mockHttp = new MockRekonHttpClient();

            // mock 주입 버전
            Assert.DoesNotThrow(() =>
            {
                var _ = new ReportSubmitService(mockUpload, mockHttp);
            }, "IRekonHttpClient mock 주입 시 예외가 없어야 합니다.");

            // 기존 1-arg 형식 (옵셔널 → null → UnityHttpClient 기본값)
            Assert.DoesNotThrow(() =>
            {
                var _ = new ReportSubmitService(mockUpload);
            }, "httpClient 생략 시(기존 호출 형식) 예외가 없어야 합니다.");
        }

        // ─── T7: create-report body 에 AccessToken 포함 금지 — Bearer 만 사용 ─

        [Test]
        public async Task SubmitReportAsync_create_report_body_에_AccessToken_미포함()
        {
            // Arrange — Bearer 헤더에만 토큰 전달, body 에 포함되면 안 됨
            var mockUpload = new MockR2UploadService();
            var mockHttp = new MockRekonHttpClient();
            mockHttp.SetResponseFor("/api/unity/reports/confirm", new HttpResponse
            {
                StatusCode = 200,
                Body = ConfirmUploadOkJson()
            });
            mockHttp.ResponseToReturn = new HttpResponse
            {
                StatusCode = 200,
                Body = CreateReportOkJson()
            };

            const string sensitiveToken = "SENSITIVE_TOKEN_12345";
            var service = new ReportSubmitService(mockUpload, mockHttp);

            // Act
            await service.SubmitReportAsync(BuildValidRequest(sensitiveToken));

            // Assert — create-report body 에 토큰이 포함되면 안 됨
            Assert.IsTrue(mockHttp.Calls.Count > 0, "HTTP 호출이 있어야 합니다.");
            var createBody = mockHttp.Calls[0].Body ?? "";
            StringAssert.DoesNotContain(sensitiveToken, createBody,
                "create-report body 에 AccessToken 이 포함되면 안 됩니다 (Authorization 헤더에만).");
        }
    }
}
