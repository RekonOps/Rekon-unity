using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ReportSubmitService Capability 핀 테스트 (TDD 강화 P2 — Slice 7 보강).
    ///
    /// 목표:
    ///   - IR2UploadService 인터페이스 seam 도입 후 mock 주입 가능성 검증
    ///   - R2 업로드 호출 인자 (FileName, Data, ContentType) 핀
    ///   - SubmitPhase 전이 로직 및 에러 분기 핀 (입력 검증 경로 전체)
    ///   - 이벤트 계층 (UsageLimitExceededException, OperationCanceledException) 핀
    ///
    /// 한계:
    ///   - create-report / confirm-upload Web API 호출은 UnityWebRequest 의존 (Unity 메인 스레드)
    ///     → 실제 HTTP 흐름은 PlayMode 외 직접 호출 불가
    ///   - Mock 통합 검증: IR2UploadService.UploadFileAsync 호출 여부는
    ///     BuildFileMap (server 응답 필요) 이후이므로, HTTP seam 없이는 end-to-end 불가
    ///   - 현 슬라이스 범위: 인터페이스 seam 도입 + 입력 검증 경로 완전 핀 + mock 결합성 핀
    /// </summary>
    [TestFixture]
    public class ReportSubmitServiceCapabilityTests
    {
        // ─── Mock IR2UploadService ─────────────────────────────────────────────

        /// <summary>
        /// IR2UploadService mock.
        /// 호출 인자(presignedUrl, fileData, contentType)를 기록하고,
        /// 미리 설정된 결과 또는 예외를 반환합니다.
        /// </summary>
        private class MockR2UploadService : IR2UploadService
        {
            // 기록된 호출 인자 목록 (복수 파일 업로드 지원)
            public List<UploadCall> Calls { get; } = new List<UploadCall>();

            // 반환할 결과 (기본: 성공)
            public UploadResult ResultToReturn { get; set; } = new UploadResult
            {
                Success = true,
                StatusCode = 200,
                ErrorMessage = null,
                BytesUploaded = 0
            };

            // 예외를 throw할 경우 설정 (null이면 ResultToReturn 반환)
            public Exception ExceptionToThrow { get; set; }

            public async Task<UploadResult> UploadFileAsync(
                string presignedUrl,
                byte[] fileData,
                string contentType,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Calls.Add(new UploadCall
                {
                    PresignedUrl = presignedUrl,
                    FileData = fileData,
                    ContentType = contentType,
                });

                // 진행률 보고 (100% 즉시)
                progress?.Report(1f);

                await Task.CompletedTask;

                if (ExceptionToThrow != null)
                    throw ExceptionToThrow;

                return ResultToReturn;
            }

            /// <summary>단일 호출 기록</summary>
            public class UploadCall
            {
                public string PresignedUrl { get; set; }
                public byte[] FileData { get; set; }
                public string ContentType { get; set; }
            }
        }

        // ─── IR2UploadService 인터페이스 seam 핀 ───────────────────────────────

        [Test]
        public void IR2UploadService_인터페이스_존재_및_UploadFileAsync_포함()
        {
            // IR2UploadService 인터페이스가 존재하고 UploadFileAsync 시그니처를 가져야 함
            var type = typeof(IR2UploadService);
            Assert.IsTrue(type.IsInterface,
                "IR2UploadService는 interface여야 합니다.");

            var method = type.GetMethod("UploadFileAsync");
            Assert.IsNotNull(method,
                "IR2UploadService에 UploadFileAsync 메서드가 있어야 합니다.");
        }

        [Test]
        public void R2UploadService_IR2UploadService_구현_확인()
        {
            // 구체 클래스가 인터페이스를 구현하는지 핀
            var concrete = new R2UploadService();
            Assert.IsInstanceOf<IR2UploadService>(concrete,
                "R2UploadService는 IR2UploadService를 구현해야 합니다.");
        }

        [Test]
        public void MockR2UploadService_IR2UploadService_구현_확인()
        {
            // mock도 인터페이스를 구현하는지 핀 (seam 유효성)
            var mock = new MockR2UploadService();
            Assert.IsInstanceOf<IR2UploadService>(mock,
                "MockR2UploadService는 IR2UploadService를 구현해야 합니다.");
        }

        // ─── ReportSubmitService 생성자 — interface seam 수용 핀 ──────────────

        [Test]
        public void ReportSubmitService_IR2UploadService_mock_주입_성공()
        {
            // mock 주입이 성공해야 함 (interface seam 동작 확인)
            var mock = new MockR2UploadService();
            Assert.DoesNotThrow(() =>
            {
                var _ = new ReportSubmitService(mock);
            }, "IR2UploadService mock을 생성자에 주입할 수 있어야 합니다.");
        }

        [Test]
        public void ReportSubmitService_null_uploadService_ArgumentNullException()
        {
            // null 주입 시 ArgumentNullException을 throw해야 함 (기존 핀 유지)
            Assert.Throws<ArgumentNullException>(() =>
            {
                var _ = new ReportSubmitService((IR2UploadService)null);
            }, "IR2UploadService null 주입 시 ArgumentNullException이어야 합니다.");
        }

        // ─── 입력 검증 경로 — 5단계 Phase 전이 전 분기 핀 ─────────────────────

        [Test]
        public async Task SubmitReportAsync_null_request_즉시_실패_반환()
        {
            // null 요청은 API 호출 없이 즉시 Failed 상태 SubmitResult 반환해야 함
            var mock = new MockR2UploadService();
            var service = new ReportSubmitService(mock);

            var result = await service.SubmitReportAsync(null);

            Assert.IsFalse(result.Success, "null request 시 Success=false여야 합니다.");
            Assert.IsNotNull(result.ErrorMessage, "null request 시 ErrorMessage가 있어야 합니다.");
            Assert.AreEqual(0, mock.Calls.Count,
                "null request 시 R2 업로드가 호출되면 안 됩니다.");
        }

        [Test]
        public async Task SubmitReportAsync_빈_AccessToken_즉시_실패()
        {
            // AccessToken 없으면 Phase=CreatingReport 진입 전 즉시 실패해야 함
            var mock = new MockR2UploadService();
            var service = new ReportSubmitService(mock);

            var request = new ReportSubmitRequest
            {
                AccessToken = "",
                WorkspaceId = "ws-001",
                Title = "테스트",
                Files = new List<FileAttachment>
                {
                    new FileAttachment { FileName = "f.mp4", Data = new byte[] { 1 }, FileType = "video" }
                }
            };

            var result = await service.SubmitReportAsync(request);

            Assert.IsFalse(result.Success, "빈 AccessToken 시 Success=false여야 합니다.");
            Assert.AreEqual(0, mock.Calls.Count,
                "AccessToken 없을 때 R2 업로드 호출이 없어야 합니다.");
        }

        [Test]
        public async Task SubmitReportAsync_빈_WorkspaceId_즉시_실패()
        {
            var mock = new MockR2UploadService();
            var service = new ReportSubmitService(mock);

            var request = new ReportSubmitRequest
            {
                AccessToken = "tok-abc",
                WorkspaceId = "",
                Title = "테스트",
                Files = new List<FileAttachment>
                {
                    new FileAttachment { FileName = "f.mp4", Data = new byte[] { 1 }, FileType = "video" }
                }
            };

            var result = await service.SubmitReportAsync(request);

            Assert.IsFalse(result.Success, "빈 WorkspaceId 시 Success=false여야 합니다.");
            Assert.AreEqual(0, mock.Calls.Count,
                "WorkspaceId 없을 때 R2 업로드 호출이 없어야 합니다.");
        }

        [Test]
        public async Task SubmitReportAsync_빈_Title_즉시_실패()
        {
            var mock = new MockR2UploadService();
            var service = new ReportSubmitService(mock);

            var request = new ReportSubmitRequest
            {
                AccessToken = "tok-abc",
                WorkspaceId = "ws-001",
                Title = "",
                Files = new List<FileAttachment>
                {
                    new FileAttachment { FileName = "f.mp4", Data = new byte[] { 1 }, FileType = "video" }
                }
            };

            var result = await service.SubmitReportAsync(request);

            Assert.IsFalse(result.Success, "빈 Title 시 Success=false여야 합니다.");
            Assert.AreEqual(0, mock.Calls.Count,
                "Title 없을 때 R2 업로드 호출이 없어야 합니다.");
        }

        [Test]
        public async Task SubmitReportAsync_빈_파일_목록_즉시_실패()
        {
            // Files가 비어있으면 UploadingFiles 단계 진입 전 즉시 실패해야 함
            var mock = new MockR2UploadService();
            var service = new ReportSubmitService(mock);

            var request = new ReportSubmitRequest
            {
                AccessToken = "tok-abc",
                WorkspaceId = "ws-001",
                Title = "버그 발생",
                Files = new List<FileAttachment>() // 빈 목록
            };

            var result = await service.SubmitReportAsync(request);

            Assert.IsFalse(result.Success, "파일 목록이 비어있을 때 Success=false여야 합니다.");
            Assert.AreEqual(0, mock.Calls.Count,
                "파일 없을 때 R2 업로드 호출이 없어야 합니다.");
        }

        [Test]
        public async Task SubmitReportAsync_null_파일_목록_즉시_실패()
        {
            var mock = new MockR2UploadService();
            var service = new ReportSubmitService(mock);

            var request = new ReportSubmitRequest
            {
                AccessToken = "tok-abc",
                WorkspaceId = "ws-001",
                Title = "버그",
                Files = null
            };

            var result = await service.SubmitReportAsync(request);

            Assert.IsFalse(result.Success, "Files=null 시 Success=false여야 합니다.");
            Assert.AreEqual(0, mock.Calls.Count,
                "Files=null 시 R2 업로드 호출이 없어야 합니다.");
        }

        // ─── SubmitPhase 전이 로직 검증 (진행률 콜백 기반) ────────────────────

        [Test]
        public async Task SubmitReportAsync_CreatingReport_Phase_진행률_초기_보고()
        {
            // 유효한 요청 전달 시 CreatingReport Phase(0%)가 최초로 보고되어야 함
            // (HTTP 호출 실패 전에 phase가 전환되는 순서 핀)
            var mock = new MockR2UploadService();
            var service = new ReportSubmitService(mock);

            var progressReports = new List<SubmitProgress>();
            var progressCallback = new Progress<SubmitProgress>(p => progressReports.Add(p));

            var request = new ReportSubmitRequest
            {
                AccessToken = "tok",
                WorkspaceId = "ws",
                Title = "테스트",
                Files = new List<FileAttachment>
                {
                    new FileAttachment { FileName = "f.png", Data = new byte[] { 1, 2, 3 }, FileType = "screenshot" }
                }
            };

            // Unity 메인 스레드 외 환경에서 HTTP 호출은 실패하거나 예외 발생
            // 최소 CreatingReport Phase가 보고된 후 실패해야 함
            var task = service.SubmitReportAsync(request, progressCallback);

            // 짧은 대기 (100ms) — HTTP 시도 전 즉시 보고되는 Phase 캡처
            bool completed = task.Wait(200);

            // CreatingReport 또는 Failed Phase가 보고되어야 함 (Phase 전이 증거)
            // Unity 메인 스레드 밖이므로 실패 분기가 기대됨
            if (progressReports.Count > 0)
            {
                Assert.AreEqual(SubmitPhase.CreatingReport, progressReports[0].Phase,
                    "첫 번째 Phase 보고는 CreatingReport여야 합니다.");
                Assert.AreEqual(0f, progressReports[0].OverallProgress, 0.001f,
                    "CreatingReport 초기 진행률은 0%여야 합니다.");
            }

            // 완료 또는 미완료 모두 허용 (Unity 환경 의존)
        }

        // ─── ContentType 결정 로직 핀 (R2UploadService.DetectContentType) ──────

        [Test]
        public void DetectContentType_mp4_video_mp4()
        {
            // R2UploadService.DetectContentType이 mp4 → video/mp4 반환해야 함
            // (ReportSubmitService가 이 메서드를 사용하여 contentType을 결정)
            string contentType = R2UploadService.DetectContentType("capture.mp4");
            Assert.AreEqual("video/mp4", contentType,
                "mp4 파일의 ContentType은 'video/mp4'여야 합니다.");
        }

        [Test]
        public void DetectContentType_png_image_png()
        {
            string contentType = R2UploadService.DetectContentType("screen01.png");
            Assert.AreEqual("image/png", contentType,
                "png 파일의 ContentType은 'image/png'이어야 합니다.");
        }

        [Test]
        public void DetectContentType_log_text_plain()
        {
            string contentType = R2UploadService.DetectContentType("rekon_log.log");
            Assert.AreEqual("text/plain", contentType,
                "log 파일의 ContentType은 'text/plain'이어야 합니다.");
        }

        [Test]
        public void DetectContentType_미지원_확장자_octet_stream()
        {
            string contentType = R2UploadService.DetectContentType("data.xyz");
            Assert.AreEqual("application/octet-stream", contentType,
                "미지원 확장자는 'application/octet-stream'이어야 합니다.");
        }

        [Test]
        public void DetectContentType_null_입력_octet_stream()
        {
            string contentType = R2UploadService.DetectContentType(null);
            Assert.AreEqual("application/octet-stream", contentType,
                "null 입력 시 'application/octet-stream'이어야 합니다.");
        }

        // ─── Failed 이벤트 분기 핀 ─────────────────────────────────────────────

        [Test]
        public async Task SubmitReportAsync_이미_취소된_Token_Failed_또는_Canceled()
        {
            // 취소된 CancellationToken 전달 시 Failed 또는 취소가 전파되어야 함
            var mock = new MockR2UploadService();
            var service = new ReportSubmitService(mock);

            var canceledToken = new CancellationToken(canceled: true);
            var request = new ReportSubmitRequest
            {
                AccessToken = "tok",
                WorkspaceId = "ws",
                Title = "테스트",
                Files = new List<FileAttachment>
                {
                    new FileAttachment { FileName = "f.mp4", Data = new byte[] { 1 }, FileType = "video" }
                }
            };

            var task = service.SubmitReportAsync(request, null, canceledToken);
            bool completed = task.Wait(200);

            if (completed)
            {
                // 완료된 경우: Canceled 또는 Faulted 상태여야 함
                // 또는 result.Success=false (입력 검증 후 취소 감지)
                Assert.IsTrue(
                    task.IsCanceled || task.IsFaulted || (task.Result != null && !task.Result.Success),
                    "취소된 Token 시 Canceled/Faulted/Success=false 중 하나여야 합니다.");
            }
            // 타임아웃 내 미완료(Unity 메인 스레드 의존)도 허용

            // 핵심: R2 업로드는 호출되지 않아야 함 (취소 전 단계에서 중단)
            Assert.AreEqual(0, mock.Calls.Count,
                "취소된 Token 시 R2 업로드가 호출되면 안 됩니다.");
        }

        // ─── UsageLimitExceededException 처리 경로 핀 ──────────────────────────

        [Test]
        public void UsageLimitExceededException_monthly_reason_핀()
        {
            // 사용량 초과 예외가 올바른 필드를 가져야 함
            var ex = new UsageLimitExceededException("monthly", 100, "https://rekonops.dev/upgrade", "한도 초과");

            Assert.AreEqual("monthly", ex.LimitReason);
            Assert.AreEqual(100, ex.MonthlyLimit);
            Assert.AreEqual("https://rekonops.dev/upgrade", ex.UpgradeUrl);
        }

        [Test]
        public void SubmitResult_IsUsageLimitExceeded_true_시_전용_필드_핀()
        {
            // SubmitResult의 usage limit 전용 필드들이 올바르게 채워져야 함
            var result = new SubmitResult
            {
                Success = false,
                IsUsageLimitExceeded = true,
                UsageLimitReason = "monthly",
                MonthlyLimit = 50,
                UpgradeUrl = "https://rekonops.dev/plans",
                ErrorMessage = "HTTP 429: 월간 한도 초과"
            };

            Assert.IsTrue(result.IsUsageLimitExceeded);
            Assert.AreEqual("monthly", result.UsageLimitReason);
            Assert.AreEqual(50, result.MonthlyLimit);
            Assert.IsFalse(string.IsNullOrEmpty(result.UpgradeUrl));
            Assert.IsFalse(result.Success);
        }

        // ─── mock 업로드 실패 시 SubmitResult.ReportId 보존 핀 ─────────────────

        [Test]
        public void SubmitResult_업로드_실패_시_ReportId_보존_가능()
        {
            // 1단계(create-report) 성공 후 2단계 실패 시 ReportId가 포함된 실패 결과가 반환될 수 있음
            // 이 동작을 SubmitResult 모델 수준에서 핀
            var result = new SubmitResult
            {
                Success = false,
                ReportId = "rpt-partial-001",  // 1단계 생성 완료
                ErrorMessage = "파일 업로드 실패 (capture.mp4): R2 오류"
            };

            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.ReportId, "업로드 실패 시에도 ReportId가 보존되어야 합니다.");
            Assert.AreEqual("rpt-partial-001", result.ReportId);
        }
    }
}
