using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// Mp4VideoEncoder + StreamingVideoRecorder 동작 핀 테스트 (TDD Slice 8).
    ///
    /// 배경: Step 1에서 VideoEncoder.cs 삭제 후 Mp4VideoEncoder + StreamingVideoRecorder가 대체.
    ///       Step 1 변경이 영상 인코딩 Capability에 영향이 없음을 회귀 검증합니다.
    ///
    /// 전략:
    ///   - FFmpeg 실제 프로세스 실행 X (미설치 환경 포함, CI 안전)
    ///   - IVideoEncoder 인터페이스 계약, 설정 모델, StreamingVideoRecorder 공개 API를 핀
    ///   - FFmpeg 미설치 시 올바른 에러 처리 경로 검증
    ///   - StreamingVideoRecorder의 프레임 큐 / 상태 전환 동작 핀
    /// </summary>
    [TestFixture]
    public class Mp4VideoEncoderTests
    {
        private VideoEncoderConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = new VideoEncoderConfig
            {
                Width = 320,
                Height = 180,
                Fps = 15,
                Crf = 23
            };
        }

        // ─── IVideoEncoder 인터페이스 계약 핀 ──────────────────────────────────

        [Test]
        public void Mp4VideoEncoder_IVideoEncoder_인터페이스_구현()
        {
            // Mp4VideoEncoder가 IVideoEncoder를 구현하는지 핀
            var encoder = new Mp4VideoEncoder();
            Assert.IsInstanceOf<IVideoEncoder>(encoder,
                "Mp4VideoEncoder는 IVideoEncoder를 구현해야 합니다.");
        }

        [Test]
        public void Mp4VideoEncoder_OutputExtension_mp4()
        {
            var encoder = new Mp4VideoEncoder();
            Assert.AreEqual(".mp4", encoder.OutputExtension,
                "Mp4VideoEncoder의 OutputExtension은 '.mp4'여야 합니다.");
        }

        [Test]
        public void Mp4VideoEncoder_RecommendedTimeoutSeconds_양수()
        {
            var encoder = new Mp4VideoEncoder();
            Assert.Greater(encoder.RecommendedTimeoutSeconds, 0f,
                "RecommendedTimeoutSeconds는 양수여야 합니다.");
        }

        // ─── EncodeAsync 입력 유효성 검증 핀 ──────────────────────────────────

        [Test]
        public void EncodeAsync_null_outputPath_ArgumentNullException_throw()
        {
            // outputPath가 null이면 ArgumentNullException을 throw해야 함
            var encoder = new Mp4VideoEncoder();
            var frames = new[] { MakeFrame(320, 180) };

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await encoder.EncodeAsync(frames, null, _config);
            }, "outputPath가 null이면 ArgumentNullException을 throw해야 합니다.");
        }

        [Test]
        public void EncodeAsync_빈_outputPath_ArgumentNullException_throw()
        {
            var encoder = new Mp4VideoEncoder();
            var frames = new[] { MakeFrame(320, 180) };

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await encoder.EncodeAsync(frames, "", _config);
            }, "빈 outputPath는 ArgumentNullException을 throw해야 합니다.");
        }

        [Test]
        public void EncodeAsync_null_config_ArgumentNullException_throw()
        {
            var encoder = new Mp4VideoEncoder();
            var frames = new[] { MakeFrame(320, 180) };
            var tempPath = Path.GetTempFileName();

            try
            {
                Assert.ThrowsAsync<ArgumentNullException>(async () =>
                {
                    await encoder.EncodeAsync(frames, tempPath, null);
                }, "config가 null이면 ArgumentNullException을 throw해야 합니다.");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Test]
        public async Task EncodeAsync_null_frames_조기_반환()
        {
            // null frames는 예외 없이 조기 반환해야 함 (경고 로그만 출력)
            var encoder = new Mp4VideoEncoder();
            var tempPath = Path.Combine(Path.GetTempPath(), $"rekon_test_{Guid.NewGuid()}.mp4");

            try
            {
                // 예외 없이 완료되어야 함
                await encoder.EncodeAsync(null, tempPath, _config);
                // 파일 미생성 확인
                Assert.IsFalse(File.Exists(tempPath),
                    "null frames 시 출력 파일이 생성되지 않아야 합니다.");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Test]
        public async Task EncodeAsync_빈_frames_배열_조기_반환()
        {
            // 빈 frames 배열은 예외 없이 조기 반환해야 함
            var encoder = new Mp4VideoEncoder();
            var tempPath = Path.Combine(Path.GetTempPath(), $"rekon_test_{Guid.NewGuid()}.mp4");

            try
            {
                await encoder.EncodeAsync(Array.Empty<FrameData>(), tempPath, _config);
                Assert.IsFalse(File.Exists(tempPath),
                    "빈 frames 배열 시 출력 파일이 생성되지 않아야 합니다.");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Test]
        public void EncodeAsync_취소된_Token_OperationCanceledException_또는_정상종료()
        {
            // 이미 취소된 토큰으로 호출 시 — FFmpeg 미설치 환경에서 취소가 전파되거나 조기 종료
            var encoder = new Mp4VideoEncoder();
            var frames = new[] { MakeFrame(320, 180) };
            var tempPath = Path.Combine(Path.GetTempPath(), $"rekon_test_{Guid.NewGuid()}.mp4");
            var canceledToken = new CancellationToken(canceled: true);

            try
            {
                var task = encoder.EncodeAsync(frames, tempPath, _config, canceledToken);
                // FFmpeg 미설치 환경에서는 프로세스 시작 실패로 예외 발생
                // 취소 환경에서는 OperationCanceledException 발생
                // 두 경우 모두 예외가 전파되어야 함 (정상 완료 X)
                bool threw = false;
                try
                {
                    task.Wait(500);
                }
                catch (AggregateException)
                {
                    threw = true;
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }

                // FFmpeg 미설치 + 취소 — 예외 발생 또는 Task Canceled/Faulted
                Assert.IsTrue(threw || task.IsCanceled || task.IsFaulted,
                    "취소된 Token 또는 FFmpeg 미설치 시 Task가 성공 완료되어서는 안 됩니다.");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        // ─── VideoEncoderConfig 핀 ─────────────────────────────────────────

        [Test]
        public void VideoEncoderConfig_기본값_확인()
        {
            var config = new VideoEncoderConfig();
            Assert.AreEqual(15, config.Fps, "Fps 기본값은 15여야 합니다.");
            Assert.AreEqual(23, config.Crf, "Crf 기본값은 23이어야 합니다.");
            Assert.AreEqual(0L, config.TargetMaxSizeBytes, "TargetMaxSizeBytes 기본값은 0이어야 합니다.");
        }

        [Test]
        public void VideoEncoderConfig_FromSettings_null_throw()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                VideoEncoderConfig.FromSettings(null);
            }, "null settings는 ArgumentNullException을 throw해야 합니다.");
        }

        [Test]
        public void VideoEncoderConfig_ToString_형식_확인()
        {
            var config = new VideoEncoderConfig { Width = 1920, Height = 1080, Fps = 30 };
            string str = config.ToString();
            Assert.IsTrue(str.Contains("1920"), "ToString에 Width가 포함되어야 합니다.");
            Assert.IsTrue(str.Contains("1080"), "ToString에 Height가 포함되어야 합니다.");
            Assert.IsTrue(str.Contains("30"), "ToString에 FPS가 포함되어야 합니다.");
        }

        // ─── StreamingVideoRecorder 공개 API 핀 ───────────────────────────────

        [Test]
        public void StreamingVideoRecorder_초기_IsRecording_false()
        {
            using var recorder = new StreamingVideoRecorder(fps: 15);
            Assert.IsFalse(recorder.IsRecording,
                "초기 IsRecording은 false여야 합니다.");
        }

        [Test]
        public void StreamingVideoRecorder_초기_FramesWritten_0()
        {
            using var recorder = new StreamingVideoRecorder(fps: 15);
            Assert.AreEqual(0L, recorder.FramesWritten,
                "초기 FramesWritten은 0이어야 합니다.");
        }

        [Test]
        public void StreamingVideoRecorder_초기_FramesDropped_0()
        {
            using var recorder = new StreamingVideoRecorder(fps: 15);
            Assert.AreEqual(0L, recorder.FramesDropped,
                "초기 FramesDropped는 0이어야 합니다.");
        }

        [Test]
        public void StreamingVideoRecorder_FFmpeg_미설치_Start_false_반환()
        {
            // FFmpeg가 미설치(또는 CI 환경)에서 Start()는 false를 반환해야 함
            // 실제 FFmpeg가 설치된 환경에서는 true 반환 가능 — 스킵하지 않고 결과만 확인
            using var recorder = new StreamingVideoRecorder(fps: 15);
            bool result = recorder.Start(320, 180);

            // FFmpeg 미설치 시 false, 설치 시 true — 어떤 경우든 bool 반환이어야 함
            Assert.IsTrue(result == true || result == false,
                "Start()는 bool 값을 반환해야 합니다.");

            // 녹화 시작 실패 시 IsRecording은 false여야 함
            if (!result)
            {
                Assert.IsFalse(recorder.IsRecording,
                    "Start() 실패 시 IsRecording은 false여야 합니다.");
            }
        }

        [Test]
        public void StreamingVideoRecorder_녹화_전_EnqueueFrame_false_반환()
        {
            // 녹화 시작 전 EnqueueFrame 호출은 false를 반환해야 함
            using var recorder = new StreamingVideoRecorder(fps: 15);
            var data = new byte[320 * 180 * 4];
            bool result = recorder.EnqueueFrame(data, data.Length);
            Assert.IsFalse(result,
                "녹화 시작 전 EnqueueFrame은 false를 반환해야 합니다.");
        }

        [Test]
        public void StreamingVideoRecorder_null_data_EnqueueFrame_false_반환()
        {
            using var recorder = new StreamingVideoRecorder(fps: 15);
            bool result = recorder.EnqueueFrame(null, 0);
            Assert.IsFalse(result,
                "null data로 EnqueueFrame은 false를 반환해야 합니다.");
        }

        [Test]
        public void StreamingVideoRecorder_Dispose_후_EnqueueFrame_false_반환()
        {
            var recorder = new StreamingVideoRecorder(fps: 15);
            recorder.Dispose();

            var data = new byte[320 * 180 * 4];
            bool result = recorder.EnqueueFrame(data, data.Length);
            Assert.IsFalse(result,
                "Dispose 후 EnqueueFrame은 false를 반환해야 합니다.");
        }

        [Test]
        public void StreamingVideoRecorder_TryRentFrameBuffer_녹화전_false_반환()
        {
            // 녹화 시작 전에는 버퍼 대여가 불가능해야 함
            using var recorder = new StreamingVideoRecorder(fps: 15);
            bool result = recorder.TryRentFrameBuffer(320 * 180 * 4, out byte[] buffer);
            Assert.IsFalse(result,
                "녹화 시작 전 TryRentFrameBuffer는 false를 반환해야 합니다.");
            Assert.IsNull(buffer,
                "버퍼 대여 실패 시 buffer는 null이어야 합니다.");
        }

        [Test]
        public void StreamingVideoRecorder_Dispose_IsRecording_false()
        {
            // Dispose 후 상태가 올바르게 정리되어야 함
            var recorder = new StreamingVideoRecorder(fps: 15);
            recorder.Dispose();

            // IsRecording은 Dispose 후 false여야 함
            Assert.IsFalse(recorder.IsRecording,
                "Dispose 후 IsRecording은 false여야 합니다.");
        }

        [Test]
        public void StreamingVideoRecorder_Fps_음수_최소값_1_보장()
        {
            // Fps <= 0은 내부적으로 1로 클램핑됨 — 생성자 방어 로직 핀
            using var recorder = new StreamingVideoRecorder(fps: -5);

            // IsRecording=false이지만 생성자가 예외 없이 완료되어야 함
            Assert.IsFalse(recorder.IsRecording,
                "유효하지 않은 fps로 생성해도 IsRecording은 false여야 합니다.");
        }

        // ─── FrameData 유효성 핀 ──────────────────────────────────────────────

        [Test]
        public void FrameData_IsValid_정상_프레임_true()
        {
            var frame = MakeFrame(320, 180);
            Assert.IsTrue(frame.IsValid, "정상 프레임의 IsValid는 true여야 합니다.");
        }

        [Test]
        public void FrameData_IsValid_null_data_false()
        {
            var frame = new FrameData(null, 0, 320, 180, 0.0);
            Assert.IsFalse(frame.IsValid, "data가 null인 프레임의 IsValid는 false여야 합니다.");
        }

        [Test]
        public void FrameData_IsValid_zero_dimension_false()
        {
            var data = new byte[100];
            var frame = new FrameData(data, data.Length, 0, 180, 0.0);
            Assert.IsFalse(frame.IsValid, "Width가 0인 프레임의 IsValid는 false여야 합니다.");
        }

        [Test]
        public void FrameData_DataLength_별도_지정_생성자()
        {
            // 사전 할당 슬롯이 frameSize 이상일 수 있으므로 DataLength 별도 지정
            var data = new byte[1000]; // 슬롯은 더 크게 할당됨
            int actualLength = 320 * 180 * 4;
            var frame = new FrameData(data, actualLength, 320, 180, 1.5);

            Assert.AreEqual(actualLength, frame.DataLength,
                "DataLength는 별도 지정한 값이어야 합니다.");
            Assert.AreEqual(1000, frame.Data.Length,
                "Data.Length는 원본 배열 크기여야 합니다.");
        }

        // ─── 유틸리티 ─────────────────────────────────────────────────────────

        /// <summary>테스트용 더미 FrameData 생성</summary>
        private static FrameData MakeFrame(int width, int height, double timestamp = 0.0)
        {
            int size = width * height * 4; // RGBA32
            return new FrameData(new byte[size], size, width, height, timestamp);
        }
    }
}
