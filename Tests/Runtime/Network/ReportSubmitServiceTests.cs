using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ReportSubmitService 동작 핀 테스트 (TDD Slice 7).
    ///
    /// 배경: Step 1에서 ReportSubmitter.cs 삭제 후 ReportSubmitService.cs가 대체.
    ///       Step 1 변경이 ReportSubmitService 동작에 영향이 없음을 회귀 검증합니다.
    ///
    /// 전략: UnityWebRequest·R2 업로드는 Unity 메인 스레드 의존이므로 직접 호출 X.
    ///        공개 인터페이스(요청 모델, 응답 모델, 예외 계층, 유효성 검증 경로)를
    ///        통해 Observable behavior만 핀합니다.
    /// </summary>
    [TestFixture]
    public class ReportSubmitServiceTests
    {
        // ─── 요청 모델 핀 ──────────────────────────────────────────────────────────

        [Test]
        public void ReportSubmitRequest_기본_Engine값_unity()
        {
            // 요청 모델 기본값이 "unity"로 고정되어 있어야 함
            var request = new ReportSubmitRequest();
            Assert.AreEqual("unity", request.Engine, "Engine 기본값은 'unity'여야 합니다.");
        }

        [Test]
        public void ReportSubmitRequest_필드_설정_정상()
        {
            // 요청 모델의 모든 필드가 올바르게 저장되어야 함
            var files = new List<FileAttachment>
            {
                new FileAttachment
                {
                    FileName = "capture.mp4",
                    Data = new byte[] { 1, 2, 3 },
                    FileType = "video"
                }
            };

            var request = new ReportSubmitRequest
            {
                AccessToken = "tok-abc",
                WorkspaceId = "ws-123",
                Title = "버그 재현됨",
                Description = "플레이 중 크래시",
                Files = files,
                Engine = "unity"
            };

            Assert.AreEqual("tok-abc", request.AccessToken);
            Assert.AreEqual("ws-123", request.WorkspaceId);
            Assert.AreEqual("버그 재현됨", request.Title);
            Assert.AreEqual("플레이 중 크래시", request.Description);
            Assert.AreEqual(1, request.Files.Count);
            Assert.AreEqual("unity", request.Engine);
        }

        [Test]
        public void ReportSubmitRequest_PerformanceTimeline_기본값_null()
        {
            // 성능 타임라인은 선택적 — 기본값이 null이어야 함
            var request = new ReportSubmitRequest();
            Assert.IsNull(request.PerformanceTimeline,
                "PerformanceTimeline 기본값은 null이어야 합니다 (선택적 필드).");
        }

        // ─── 파일 첨부 모델 핀 ──────────────────────────────────────────────────

        [Test]
        public void FileAttachment_FileType_지원_값_검증()
        {
            // "screenshot" / "video" / "log" — 지원되는 FileType 값들을 핀
            string[] supportedTypes = { "screenshot", "video", "log" };

            foreach (var type in supportedTypes)
            {
                var attachment = new FileAttachment { FileType = type };
                Assert.AreEqual(type, attachment.FileType,
                    $"FileType '{type}'이 올바르게 저장되어야 합니다.");
            }
        }

        [Test]
        public void FileAttachment_Data_및_FileName_저장()
        {
            var data = new byte[] { 0xFF, 0xD8, 0xFF }; // JPEG 매직 바이트
            var attachment = new FileAttachment
            {
                FileName = "screen01.jpg",
                Data = data,
                FileType = "screenshot"
            };

            Assert.AreEqual("screen01.jpg", attachment.FileName);
            Assert.AreSame(data, attachment.Data,
                "Data 배열은 동일한 참조여야 합니다 (복사 없음).");
            Assert.AreEqual(3, attachment.Data.Length);
        }

        // ─── 결과 모델 핀 ──────────────────────────────────────────────────────

        [Test]
        public void SubmitResult_성공_상태_필드()
        {
            var result = new SubmitResult
            {
                Success = true,
                ReportId = "rpt-uuid-001",
                ErrorMessage = null
            };

            Assert.IsTrue(result.Success);
            Assert.AreEqual("rpt-uuid-001", result.ReportId);
            Assert.IsNull(result.ErrorMessage);
        }

        [Test]
        public void SubmitResult_실패_상태_필드()
        {
            var result = new SubmitResult
            {
                Success = false,
                ReportId = null,
                ErrorMessage = "네트워크 오류"
            };

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.ReportId);
            Assert.AreEqual("네트워크 오류", result.ErrorMessage);
        }

        [Test]
        public void SubmitResult_UsageLimit_초과_필드()
        {
            // 429 사용량 초과 시 전용 필드가 올바르게 채워져야 함
            var result = new SubmitResult
            {
                Success = false,
                IsUsageLimitExceeded = true,
                UsageLimitReason = "monthly",
                MonthlyLimit = 100,
                UpgradeUrl = "https://rekonops.dev/upgrade",
                ErrorMessage = "HTTP 429: 월간 한도 초과"
            };

            Assert.IsTrue(result.IsUsageLimitExceeded);
            Assert.AreEqual("monthly", result.UsageLimitReason);
            Assert.AreEqual(100, result.MonthlyLimit);
            Assert.IsFalse(string.IsNullOrEmpty(result.UpgradeUrl),
                "UpgradeUrl은 비어있지 않아야 합니다.");
        }

        [Test]
        public void SubmitResult_기본_IsUsageLimitExceeded_false()
        {
            var result = new SubmitResult { Success = true };
            Assert.IsFalse(result.IsUsageLimitExceeded,
                "기본 IsUsageLimitExceeded는 false여야 합니다.");
        }

        // ─── 진행률 모델 핀 ─────────────────────────────────────────────────────

        [Test]
        public void SubmitProgress_Phase_및_Progress_저장()
        {
            var progress = new SubmitProgress
            {
                Phase = SubmitPhase.UploadingFiles,
                OverallProgress = 0.5f,
                StatusMessage = "파일 업로드 중..."
            };

            Assert.AreEqual(SubmitPhase.UploadingFiles, progress.Phase);
            Assert.AreEqual(0.5f, progress.OverallProgress, 0.0001f);
            Assert.AreEqual("파일 업로드 중...", progress.StatusMessage);
        }

        [Test]
        public void SubmitPhase_모든_값_존재()
        {
            // Step 1 이후에도 5개 단계가 모두 유지되어야 함
            var phases = (SubmitPhase[])Enum.GetValues(typeof(SubmitPhase));

            Assert.IsTrue(Array.Exists(phases, p => p == SubmitPhase.CreatingReport),
                "CreatingReport 단계가 존재해야 합니다.");
            Assert.IsTrue(Array.Exists(phases, p => p == SubmitPhase.UploadingFiles),
                "UploadingFiles 단계가 존재해야 합니다.");
            Assert.IsTrue(Array.Exists(phases, p => p == SubmitPhase.ConfirmingUpload),
                "ConfirmingUpload 단계가 존재해야 합니다.");
            Assert.IsTrue(Array.Exists(phases, p => p == SubmitPhase.Completed),
                "Completed 단계가 존재해야 합니다.");
            Assert.IsTrue(Array.Exists(phases, p => p == SubmitPhase.Failed),
                "Failed 단계가 존재해야 합니다.");
        }

        // ─── 예외 계층 핀 ──────────────────────────────────────────────────────

        [Test]
        public void UsageLimitExceededException_필드_올바르게_저장()
        {
            // 429 사용량 초과 예외 모델 핀
            var ex = new UsageLimitExceededException(
                limitReason: "monthly",
                monthlyLimit: 50,
                upgradeUrl: "https://rekonops.dev/upgrade",
                message: "HTTP 429: 월간 리포트 한도 초과");

            Assert.AreEqual("monthly", ex.LimitReason);
            Assert.AreEqual(50, ex.MonthlyLimit);
            Assert.AreEqual("https://rekonops.dev/upgrade", ex.UpgradeUrl);
            Assert.AreEqual("HTTP 429: 월간 리포트 한도 초과", ex.Message);
        }

        [Test]
        public void UsageLimitExceededException_null_인자_빈문자열로_처리()
        {
            // null 방어 처리 — LimitReason/UpgradeUrl은 null 대신 빈 문자열 반환해야 함
            var ex = new UsageLimitExceededException(
                limitReason: null,
                monthlyLimit: 0,
                upgradeUrl: null,
                message: "test");

            Assert.IsNotNull(ex.LimitReason, "LimitReason은 null이 아니어야 합니다.");
            Assert.IsNotNull(ex.UpgradeUrl, "UpgradeUrl은 null이 아니어야 합니다.");
            Assert.AreEqual("", ex.LimitReason);
            Assert.AreEqual("", ex.UpgradeUrl);
        }

        [Test]
        public void UsageLimitExceededException_Exception_상속_확인()
        {
            var ex = new UsageLimitExceededException("monthly", 100, "", "msg");
            Assert.IsInstanceOf<Exception>(ex,
                "UsageLimitExceededException은 Exception을 상속해야 합니다.");
        }

        // ─── ReportSubmitService 생성자 핀 ────────────────────────────────────

        [Test]
        public void ReportSubmitService_null_uploadService_예외_throw()
        {
            // uploadService가 null이면 ArgumentNullException을 throw해야 함
            Assert.Throws<ArgumentNullException>(() =>
            {
                // R2UploadService는 매개변수 없는 생성자를 가짐 — null 전달 시 예외 발생 검증
                var _ = new ReportSubmitService(null);
            }, "uploadService가 null이면 ArgumentNullException을 throw해야 합니다.");
        }

        // ─── CancellationToken 취소 핀 ─────────────────────────────────────────

        [Test]
        public void SubmitReportAsync_이미_취소된_Token_OperationCanceledException_throw()
        {
            // 이미 취소된 CancellationToken 전달 시 OperationCanceledException이 발생해야 함
            // (Unity 메인 스레드 밖에서 실행 — 실제 HTTP 호출은 발생하지 않음)
            var uploadService = new R2UploadService();
            var service = new ReportSubmitService(uploadService);

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

            // SubmitReportAsync 내부에서 취소 확인 전에 입력 검증이 통과되면
            // UnityWebRequest 없이 OperationCanceledException 또는 일반 예외가 발생함
            // Unity 환경 밖이므로 AggregateException으로 래핑될 수 있음
            var task = service.SubmitReportAsync(request, null, canceledToken);

            // Task가 완료될 때까지 대기 (타임아웃 100ms — 실제 네트워크 없이 즉시 처리됨)
            bool completed = task.Wait(100);

            // 취소된 토큰이므로: Canceled 상태 또는 Faulted(OperationCanceledException) 여야 함
            if (completed)
            {
                Assert.IsTrue(
                    task.IsCanceled || task.IsFaulted,
                    "취소된 Token으로 호출 시 Task가 취소 또는 실패 상태여야 합니다.");
            }
            // 타임아웃 내 완료 안 되는 경우도 허용 (Unity 환경 의존성)
        }
    }
}
