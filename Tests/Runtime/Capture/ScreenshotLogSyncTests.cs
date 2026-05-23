using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// 스크린샷 캡처 시각(realtimeSinceStartup) 로그 마커 싱크 관련 단위 테스트.
    ///
    /// 검증 범위:
    ///   U1. ScreenshotEntry.CaptureRealtime 필드가 올바르게 기록되는지
    ///   U2. 스크린샷 경로 team_pro 시 ReplayLogCollector 바인딩(활성) 여부
    ///   U3. FileAttachment.CapturedTAbs 직렬화 - team_pro(포함) / free(미포함)
    ///
    /// 어셈블리 경계: Rekon.Runtime ↔ Rekon.Tests 별도 어셈블리.
    /// InternalsVisibleTo 없음 → 테스트가 호출하는 모든 멤버는 public 필수.
    /// </summary>
    [TestFixture]
    public class ScreenshotLogSyncTests
    {
        // ──────────────────────────────────────────────────────────────────────
        // U1. ScreenshotEntry.CaptureRealtime
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void ScreenshotEntry_CaptureRealtime_생성자로_설정()
        {
            // 3-파라미터 생성자: pngBytes + timestamp + captureRealtime
            double expectedRealtime = 123.456;
            var entry = new ScreenshotEntry(new byte[] { 1, 2, 3 }, DateTime.UtcNow, expectedRealtime);

            Assert.AreEqual(expectedRealtime, entry.CaptureRealtime, 1e-9,
                "CaptureRealtime이 생성자 값과 일치해야 합니다.");
        }

        [Test]
        public void ScreenshotEntry_기존_2파라미터_생성자_CaptureRealtime_기본값은_0()
        {
            // 기존 2-파라미터 생성자: captureRealtime 미지정 → 0.0 (기본값, 하위 호환)
            var entry = new ScreenshotEntry(new byte[] { 1, 2 }, DateTime.UtcNow);

            Assert.AreEqual(0.0, entry.CaptureRealtime, 1e-9,
                "기존 생성자 호출 시 CaptureRealtime 기본값은 0이어야 합니다.");
        }

        [Test]
        public void ScreenshotEntry_CaptureRealtime_정밀도_보존()
        {
            // realtimeSinceStartupAsDouble 은 double — float 다운캐스트 없이 보존
            double ts = 98765.123456789;
            var entry = new ScreenshotEntry(new byte[] { 0 }, DateTime.UtcNow, ts);

            // double.R 포맷 왕복 정밀도 검증
            Assert.AreEqual(ts, entry.CaptureRealtime, 1e-9,
                "CaptureRealtime은 double 정밀도를 보존해야 합니다.");
        }

        [Test]
        public void ScreenshotQueue_Enqueue_CaptureRealtime_저장()
        {
            // ScreenshotQueue.Enqueue(bytes, timestamp, captureRealtime) 오버로드 검증
            var queue = new ScreenshotQueue();
            double expectedRealtime = 42.5;

            queue.Enqueue(new byte[] { 1, 2 }, DateTime.UtcNow, expectedRealtime);

            var entries = queue.PeekAll();
            Assert.AreEqual(1, entries.Length, "Enqueue 후 큐에 1개가 있어야 합니다.");
            Assert.AreEqual(expectedRealtime, entries[0].CaptureRealtime, 1e-9,
                "Enqueue 시 CaptureRealtime이 올바르게 저장되어야 합니다.");
        }

        [Test]
        public void ScreenshotQueue_기존_2파라미터_Enqueue_CaptureRealtime_0()
        {
            // 기존 Enqueue(bytes, timestamp) 호출 시 captureRealtime = 0 (하위 호환)
            var queue = new ScreenshotQueue();
            queue.Enqueue(new byte[] { 1, 2 }, DateTime.UtcNow);

            var entries = queue.PeekAll();
            Assert.AreEqual(0.0, entries[0].CaptureRealtime, 1e-9,
                "기존 Enqueue 오버로드는 CaptureRealtime = 0 이어야 합니다.");
        }

        [Test]
        public void ScreenshotQueue_DrainAll_CaptureRealtime_포함()
        {
            var queue = new ScreenshotQueue();
            double rt1 = 10.0, rt2 = 20.0;
            queue.Enqueue(new byte[] { 1 }, DateTime.UtcNow, rt1);
            queue.Enqueue(new byte[] { 2 }, DateTime.UtcNow, rt2);

            var drained = queue.DrainAll();

            Assert.AreEqual(rt1, drained[0].CaptureRealtime, 1e-9, "첫 항목 CaptureRealtime 검증.");
            Assert.AreEqual(rt2, drained[1].CaptureRealtime, 1e-9, "두 번째 항목 CaptureRealtime 검증.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // U2. CaptureOrchestrator 스크린샷 경로 team_pro ReplayLogCollector 바인딩
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void CaptureOrchestrator_BindScreenshotReplayLogCollector_team_pro_바인딩()
        {
            // CaptureOrchestrator.BindScreenshotReplayLogCollector(collector) 메서드가 존재하고
            // 예외 없이 호출될 수 있어야 합니다.
            // (Unity 런타임 의존 없는 seam 검증)
            var collector = new ReplayLogCollector();
            var settings = CreateMinimalSettings("team_pro");

            var orchestrator = new CaptureOrchestrator(
                screenshotCapturer: new StubScreenshotCapturer(),
                logCollector: new StubLogCollector(),
                logSerializer: new LogSerializer(),
                stateCollector: new StubStateSnapshotCollector(),
                frameBuffer: null,
                videoEncoder: null,
                videoConfig: null,
                settings: settings,
                screenshotQueue: new ScreenshotQueue()
            );

            // 예외 없이 바인딩 가능해야 합니다
            Assert.DoesNotThrow(() => orchestrator.BindScreenshotReplayLogCollector(collector),
                "BindScreenshotReplayLogCollector 호출이 예외 없이 완료되어야 합니다.");

            collector.Dispose();
        }

        [Test]
        public void CaptureOrchestrator_BindScreenshotReplayLogCollector_null_허용()
        {
            // null 전달 시 기존 수집기 해제 (fallback)
            var settings = CreateMinimalSettings("team_pro");
            var orchestrator = new CaptureOrchestrator(
                screenshotCapturer: new StubScreenshotCapturer(),
                logCollector: new StubLogCollector(),
                logSerializer: new LogSerializer(),
                stateCollector: new StubStateSnapshotCollector(),
                frameBuffer: null,
                videoEncoder: null,
                videoConfig: null,
                settings: settings,
                screenshotQueue: new ScreenshotQueue()
            );

            Assert.DoesNotThrow(() => orchestrator.BindScreenshotReplayLogCollector(null),
                "null 전달 시도 예외가 발생하지 않아야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // U3. FileAttachment.CapturedTAbs 직렬화 (team_pro / free 분기)
        // ──────────────────────────────────────────────────────────────────────

        [Test]
        public void FileAttachment_CapturedTAbs_기본값은_null()
        {
            var attachment = new FileAttachment
            {
                FileName = "screenshot_01.png",
                Data     = new byte[] { 1 },
                FileType = "screenshot"
            };

            Assert.IsNull(attachment.CapturedTAbs,
                "CapturedTAbs 미설정 시 null이어야 합니다.");
        }

        [Test]
        public void FileAttachment_CapturedTAbs_설정_및_읽기()
        {
            var attachment = new FileAttachment
            {
                FileName     = "screenshot_01.png",
                Data         = new byte[] { 1 },
                FileType     = "screenshot",
                CapturedTAbs = 123.456
            };

            Assert.IsNotNull(attachment.CapturedTAbs, "CapturedTAbs가 설정되어야 합니다.");
            Assert.AreEqual(123.456, attachment.CapturedTAbs.Value, 1e-9);
        }

        [Test]
        public void ReportSubmitService_CreateReport_files_JSON_team_pro_captured_t_abs_포함()
        {
            // team_pro 플랜 + 스크린샷 FileAttachment + CapturedTAbs 있음
            // → files JSON에 "captured_t_abs" 필드가 포함되어야 합니다.
            double captureRealtime = 42.123;
            var files = new List<FileAttachment>
            {
                new FileAttachment
                {
                    FileName     = "screenshot_01.png",
                    Data         = new byte[] { 1, 2, 3 },
                    FileType     = "screenshot",
                    CapturedTAbs = captureRealtime
                }
            };

            string json = FilesJsonBuilder.Build(files, isTeamPro: true);

            StringAssert.Contains("\"captured_t_abs\"", json,
                "team_pro 스크린샷에 captured_t_abs 필드가 포함되어야 합니다.");
            StringAssert.Contains(
                captureRealtime.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                json,
                "captured_t_abs 값이 정확해야 합니다.");
        }

        [Test]
        public void ReportSubmitService_CreateReport_files_JSON_free_플랜_captured_t_abs_미포함()
        {
            // free 플랜 → CapturedTAbs 있어도 JSON에 포함하지 않습니다
            var files = new List<FileAttachment>
            {
                new FileAttachment
                {
                    FileName     = "screenshot_01.png",
                    Data         = new byte[] { 1 },
                    FileType     = "screenshot",
                    CapturedTAbs = 99.9
                }
            };

            string json = FilesJsonBuilder.Build(files, isTeamPro: false);

            StringAssert.DoesNotContain("captured_t_abs", json,
                "free 플랜에서는 captured_t_abs 가 포함되지 않아야 합니다.");
        }

        [Test]
        public void ReportSubmitService_CreateReport_files_JSON_team_pro_비스크린샷_captured_t_abs_미포함()
        {
            // team_pro라도 video/log 파일은 captured_t_abs 미포함
            var files = new List<FileAttachment>
            {
                new FileAttachment
                {
                    FileName     = "logs.jsonl",
                    Data         = new byte[] { 1 },
                    FileType     = "log",
                    CapturedTAbs = null
                },
                new FileAttachment
                {
                    FileName     = "video.mp4",
                    Data         = new byte[] { 1 },
                    FileType     = "video",
                    CapturedTAbs = null
                }
            };

            string json = FilesJsonBuilder.Build(files, isTeamPro: true);

            StringAssert.DoesNotContain("captured_t_abs", json,
                "스크린샷 외 파일에는 captured_t_abs 가 없어야 합니다.");
        }

        [Test]
        public void ReportSubmitService_CreateReport_files_JSON_team_pro_복수_스크린샷_각_t_abs_포함()
        {
            // team_pro 복수 스크린샷: 각각 다른 captured_t_abs 값
            var files = new List<FileAttachment>
            {
                new FileAttachment
                {
                    FileName     = "screenshot_01.png",
                    Data         = new byte[] { 1 },
                    FileType     = "screenshot",
                    CapturedTAbs = 10.5
                },
                new FileAttachment
                {
                    FileName     = "screenshot_02.png",
                    Data         = new byte[] { 2 },
                    FileType     = "screenshot",
                    CapturedTAbs = 25.0
                }
            };

            string json = FilesJsonBuilder.Build(files, isTeamPro: true);

            // 두 값 모두 포함되어야 합니다
            StringAssert.Contains(
                (10.5).ToString("R", System.Globalization.CultureInfo.InvariantCulture), json,
                "첫 번째 스크린샷 captured_t_abs 값이 포함되어야 합니다.");
            StringAssert.Contains(
                (25.0).ToString("R", System.Globalization.CultureInfo.InvariantCulture), json,
                "두 번째 스크린샷 captured_t_abs 값이 포함되어야 합니다.");
        }

        [Test]
        public void ReportSubmitService_CreateReport_files_JSON_team_pro_CapturedTAbs_null이면_미포함()
        {
            // team_pro 스크린샷이지만 CapturedTAbs = null이면 captured_t_abs 미포함
            var files = new List<FileAttachment>
            {
                new FileAttachment
                {
                    FileName     = "screenshot_01.png",
                    Data         = new byte[] { 1 },
                    FileType     = "screenshot",
                    CapturedTAbs = null
                }
            };

            string json = FilesJsonBuilder.Build(files, isTeamPro: true);

            StringAssert.DoesNotContain("captured_t_abs", json,
                "CapturedTAbs = null 이면 captured_t_abs 필드가 없어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────────────
        // 헬퍼: 최소 RekonSettings 생성
        // ──────────────────────────────────────────────────────────────────────

        private static RekonSettings CreateMinimalSettings(string plan)
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<RekonSettings>();
            settings.currentPlan = plan;
            return settings;
        }

        // ──────────────────────────────────────────────────────────────────────
        // 스텁 클래스 (Unity 런타임 의존 없이 CaptureOrchestrator 생성자 충족)
        // ──────────────────────────────────────────────────────────────────────

        private class StubScreenshotCapturer : IScreenshotCapturer
        {
            public System.Threading.Tasks.Task<byte[]> CaptureAsync()
                => System.Threading.Tasks.Task.FromResult(new byte[] { 1, 2, 3 });

            public System.Threading.Tasks.Task SaveAsync(byte[] pngBytes, string path)
                => System.Threading.Tasks.Task.CompletedTask;
        }

        private class StubLogCollector : ILogCollector
        {
            public int Count => 0;
            public LogEntry[] GetEntries() => Array.Empty<LogEntry>();
        }

        private class StubStateSnapshotCollector : IStateSnapshotCollector
        {
            public System.Threading.Tasks.Task<StateSnapshot> CollectAsync()
                => System.Threading.Tasks.Task.FromResult(new StateSnapshot());
        }
    }
}
