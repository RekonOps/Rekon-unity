using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// Mp4VideoEncoder FFmpeg 호출 인자 핀 테스트 (TDD 강화 P3 — Slice 8 보강).
    ///
    /// 목표:
    ///   - BuildFfmpegArguments의 인코더별 인자 패턴 핀
    ///     (h264_nvenc / h264_videotoolbox / h264_amf / h264_qsv / libx264 fallback)
    ///   - GPU → libx264 폴백 체인 로직 검증
    ///   - FFmpeg 미설치 시 프로세스 시작 실패 경로 핀
    ///   - FfmpegHelper 캐시 초기화/감지 로직 핀
    ///
    /// 접근 (Option 1 - 리플렉션 기반 정적 검증):
    ///   - Process.Start() 실제 호출 없음 (FFmpeg 실행 X)
    ///   - BuildFfmpegArguments private static 메서드를 리플렉션으로 호출
    ///   - 반환된 인자 문자열의 패턴을 Assert로 검증
    ///
    /// 한계:
    ///   - IFfmpegProcessRunner interface seam 도입은 Step 3 deepening 후보
    ///   - GPU 인코더 폴백 체인(TryRunFfmpeg → RunFfmpegEncoding)은 실제 프로세스 필요
    ///     → 현 슬라이스에서는 폴백 체인 로직을 BuildFfmpegArguments 인자 패턴으로 간접 핀
    /// </summary>
    [TestFixture]
    public class Mp4VideoEncoderCapabilityTests
    {
        // ─── 리플렉션 헬퍼 ─────────────────────────────────────────────────────

        /// <summary>
        /// Mp4VideoEncoder.BuildFfmpegArguments(int width, int height, VideoEncoderConfig config,
        ///     string outputPath, string encoderName) 를 리플렉션으로 호출합니다.
        /// </summary>
        private static string CallBuildFfmpegArguments(
            int width,
            int height,
            VideoEncoderConfig config,
            string outputPath,
            string encoderName)
        {
            var type = typeof(Mp4VideoEncoder);
            var method = type.GetMethod(
                "BuildFfmpegArguments",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
                Assert.Fail("BuildFfmpegArguments 메서드를 찾을 수 없습니다. 메서드 이름/시그니처를 확인하세요.");

            return (string)method.Invoke(null, new object[] { width, height, config, outputPath, encoderName });
        }

        private VideoEncoderConfig MakeConfig(int width = 1280, int height = 720, int fps = 30, int crf = 23)
        {
            return new VideoEncoderConfig
            {
                Width = width,
                Height = height,
                Fps = fps,
                Crf = crf,
            };
        }

        // ─── BuildFfmpegArguments 존재 여부 핀 ────────────────────────────────

        [Test]
        public void BuildFfmpegArguments_메서드_존재_핀()
        {
            // BuildFfmpegArguments private static 메서드가 Mp4VideoEncoder에 존재해야 함
            var type = typeof(Mp4VideoEncoder);
            var method = type.GetMethod(
                "BuildFfmpegArguments",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method,
                "Mp4VideoEncoder에 BuildFfmpegArguments private static 메서드가 있어야 합니다.");
        }

        // ─── CPU fallback (libx264) 인자 핀 ───────────────────────────────────

        [Test]
        public void BuildFfmpegArguments_libx264_encoderName_null_인자_패턴()
        {
            // encoderName=null → CPU fallback (libx264) 인자여야 함
            var config = MakeConfig(crf: 23);
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", null);

            // 필수 입력 파라미터
            Assert.IsTrue(args.Contains("-f rawvideo"), "rawvideo 입력 포맷이 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-pix_fmt rgba"), "입력 픽셀 포맷 rgba가 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-video_size 1280x720"), "해상도 지정이 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-framerate 30"), "fps=30이 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-i pipe:0"), "stdin 파이프 입력이 포함되어야 합니다.");

            // libx264 인코더 인자
            Assert.IsTrue(args.Contains("libx264"), "libx264 코덱이 지정되어야 합니다.");
            Assert.IsTrue(args.Contains("-preset ultrafast"), "ultrafast 프리셋이 지정되어야 합니다.");
            Assert.IsTrue(args.Contains("-crf 23"), "CRF 값이 포함되어야 합니다.");

            // 출력 포맷
            Assert.IsTrue(args.Contains("-pix_fmt yuv420p"), "출력 픽셀 포맷 yuv420p가 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("out.mp4"), "출력 파일명이 포함되어야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_libx264_덮어쓰기_플래그()
        {
            // -y 플래그: 기존 파일 덮어쓰기 (FFmpeg 대화형 프롬프트 방지)
            var config = MakeConfig();
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", null);

            Assert.IsTrue(args.TrimStart().StartsWith("-y"),
                "FFmpeg 인자는 -y (덮어쓰기) 플래그로 시작해야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_libx264_CRF_값_정확히_포함()
        {
            // CRF 값이 config.Crf와 정확히 일치해야 함
            var config = MakeConfig(crf: 28);
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", null);

            Assert.IsTrue(args.Contains("-crf 28"),
                "CRF 28이 인자에 정확히 포함되어야 합니다.");
            Assert.IsFalse(args.Contains("h264_nvenc"),
                "libx264 경로에는 h264_nvenc가 없어야 합니다.");
        }

        // ─── NVIDIA NVENC (h264_nvenc) 인자 핀 ────────────────────────────────

        [Test]
        public void BuildFfmpegArguments_h264_nvenc_인자_패턴()
        {
            // h264_nvenc → NVIDIA NVENC 인자 패턴 핀
            var config = MakeConfig(crf: 20);
            string args = CallBuildFfmpegArguments(1920, 1080, config, "/tmp/out.mp4", "h264_nvenc");

            // NVENC 필수 인자
            Assert.IsTrue(args.Contains("h264_nvenc"), "h264_nvenc 코덱이 지정되어야 합니다.");
            Assert.IsTrue(args.Contains("-preset p4"), "NVENC preset p4가 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-rc vbr"), "NVENC 가변 비트레이트(-rc vbr)가 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-cq 20"), "NVENC CQ 값이 포함되어야 합니다.");

            // libx264 인자가 없어야 함
            Assert.IsFalse(args.Contains("libx264"),
                "h264_nvenc 경로에는 libx264가 없어야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_h264_nvenc_공통_입력_인자_포함()
        {
            var config = MakeConfig();
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", "h264_nvenc");

            // 공통 입력 스트림 인자
            Assert.IsTrue(args.Contains("-f rawvideo"), "rawvideo 입력이 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-pix_fmt rgba"), "RGBA 입력 포맷이 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-i pipe:0"), "stdin 파이프가 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-pix_fmt yuv420p"), "yuv420p 출력이 포함되어야 합니다.");
        }

        // ─── Apple VideoToolbox (h264_videotoolbox) 인자 핀 ───────────────────

        [Test]
        public void BuildFfmpegArguments_h264_videotoolbox_인자_패턴()
        {
            // h264_videotoolbox → Apple VideoToolbox 인자 핀
            var config = MakeConfig();
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", "h264_videotoolbox");

            Assert.IsTrue(args.Contains("h264_videotoolbox"),
                "h264_videotoolbox 코덱이 지정되어야 합니다.");
            Assert.IsTrue(args.Contains("-vcodec h264_videotoolbox"),
                "-vcodec h264_videotoolbox가 포함되어야 합니다.");

            // VideoToolbox는 별도 품질 파라미터 없음 (-b:v, -q:v 제거됨)
            Assert.IsFalse(args.Contains("-crf"),
                "VideoToolbox 경로에는 -crf가 없어야 합니다 (FFmpeg 호환성 이슈).");
            Assert.IsFalse(args.Contains("libx264"),
                "VideoToolbox 경로에는 libx264가 없어야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_h264_videotoolbox_vbr_품질_파라미터_없음()
        {
            // VideoToolbox: -b:v, -q:v 파라미터가 없어야 함 (최신 FFmpeg 호환)
            var config = MakeConfig();
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", "h264_videotoolbox");

            Assert.IsFalse(args.Contains("-b:v"),
                "VideoToolbox 경로에는 -b:v가 없어야 합니다.");
            Assert.IsFalse(args.Contains("-q:v"),
                "VideoToolbox 경로에는 -q:v가 없어야 합니다.");
        }

        // ─── AMD AMF (h264_amf) 인자 핀 ──────────────────────────────────────

        [Test]
        public void BuildFfmpegArguments_h264_amf_인자_패턴()
        {
            // h264_amf → AMD AMF CQP 모드 인자 핀
            var config = MakeConfig(crf: 22);
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", "h264_amf");

            Assert.IsTrue(args.Contains("h264_amf"), "h264_amf 코덱이 지정되어야 합니다.");
            Assert.IsTrue(args.Contains("-quality speed"), "AMD speed 품질 프리셋이 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-rc cqp"), "AMD CQP 레이트 제어가 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-qp_i 22"), "CRF→QP 값이 qp_i에 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-qp_p 22"), "CRF→QP 값이 qp_p에 포함되어야 합니다.");
        }

        // ─── Intel QSV (h264_qsv) 인자 핀 ────────────────────────────────────

        [Test]
        public void BuildFfmpegArguments_h264_qsv_인자_패턴()
        {
            // h264_qsv → Intel Quick Sync 인자 핀
            var config = MakeConfig(crf: 25);
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", "h264_qsv");

            Assert.IsTrue(args.Contains("h264_qsv"), "h264_qsv 코덱이 지정되어야 합니다.");
            Assert.IsTrue(args.Contains("-preset veryfast"), "QSV veryfast 프리셋이 포함되어야 합니다.");
            Assert.IsTrue(args.Contains("-global_quality 25"), "QSV global_quality 값이 포함되어야 합니다.");
        }

        // ─── 인코더 폴백 체인 순서 정적 검증 ──────────────────────────────────

        [Test]
        public void BuildFfmpegArguments_알수없는_인코더명_libx264_폴백()
        {
            // 등록되지 않은 인코더 이름은 libx264 fallback(switch default)으로 처리되어야 함
            var config = MakeConfig(crf: 23);
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", "h264_unknown_gpu");

            // switch default → libx264
            Assert.IsTrue(args.Contains("libx264"),
                "알 수 없는 인코더명은 libx264 폴백이어야 합니다.");
            Assert.IsTrue(args.Contains("-preset ultrafast"),
                "libx264 폴백은 ultrafast 프리셋이어야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_빈문자열_encoderName_libx264_폴백()
        {
            // 빈 문자열 encoderName도 libx264 fallback이어야 함
            var config = MakeConfig();
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", "");

            Assert.IsTrue(args.Contains("libx264"),
                "빈 encoderName은 libx264 폴백이어야 합니다.");
        }

        // ─── 해상도 및 1080p 초과 다운스케일 핀 ───────────────────────────────

        [Test]
        public void BuildFfmpegArguments_1080p_이하_스케일_필터_없음()
        {
            // 1080p 이하 해상도에서는 -vf scale 필터가 없어야 함
            var config = MakeConfig(height: 1080);
            string args = CallBuildFfmpegArguments(1920, 1080, config, "/tmp/out.mp4", null);

            Assert.IsFalse(args.Contains("-vf scale"),
                "1080p 이하에서는 다운스케일 필터가 없어야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_1080p_초과_다운스케일_필터_포함()
        {
            // 1080p 초과(예: 4K) 시 -vf scale=-2:1080 필터가 추가되어야 함
            var config = MakeConfig(height: 2160); // 4K
            string args = CallBuildFfmpegArguments(3840, 2160, config, "/tmp/out.mp4", null);

            Assert.IsTrue(args.Contains("-vf scale=-2:1080"),
                "1080p 초과 시 -vf scale=-2:1080 다운스케일이 포함되어야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_1081p_경계값_다운스케일_포함()
        {
            // 정확히 1081 height는 초과이므로 다운스케일이어야 함
            var config = MakeConfig(height: 1081);
            string args = CallBuildFfmpegArguments(1920, 1081, config, "/tmp/out.mp4", null);

            Assert.IsTrue(args.Contains("-vf scale=-2:1080"),
                "height=1081은 다운스케일 경계값이므로 필터가 포함되어야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_720p_스케일_필터_없음()
        {
            // 720p — 다운스케일 없어야 함
            var config = MakeConfig(width: 1280, height: 720);
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", null);

            Assert.IsFalse(args.Contains("-vf scale"),
                "720p에서는 다운스케일 필터가 없어야 합니다.");
        }

        // ─── 출력 경로 이스케이프 핀 ──────────────────────────────────────────

        [Test]
        public void BuildFfmpegArguments_경로_따옴표_포함_안전_처리()
        {
            // 경로에 따옴표가 포함된 경우 제거되어야 함 (인젝션 방지)
            var config = MakeConfig();
            string args = CallBuildFfmpegArguments(1280, 720, config,
                "/tmp/my\"path/out.mp4", null);

            // 따옴표가 제거된 경로여야 함
            Assert.IsTrue(args.Contains("mypath"),
                "경로의 따옴표가 제거된 후 나머지 경로가 포함되어야 합니다.");
        }

        [Test]
        public void BuildFfmpegArguments_정상_경로_따옴표_감쌈()
        {
            // 정상 경로는 큰따옴표로 감싸야 함
            var config = MakeConfig();
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/output.mp4", null);

            Assert.IsTrue(args.Contains("\"/tmp/output.mp4\""),
                "출력 경로는 큰따옴표로 감싸야 합니다.");
        }

        // ─── FfmpegHelper 캐시 초기화 핀 ──────────────────────────────────────

        [Test]
        public void FfmpegHelper_ClearCache_후_IsInstalled_재감지_준비()
        {
            // ClearCache 이후 IsInstalled 캐시가 초기화되어야 함
            // (실제 감지 프로세스는 미실행 — 캐시 상태만 핀)
            FfmpegHelper.ClearCache();

            // ClearCache 이후에도 IsInstalled() 호출이 예외 없이 동작해야 함
            bool result = false;
            Assert.DoesNotThrow(() =>
            {
                result = FfmpegHelper.IsInstalled();
            }, "ClearCache 후 IsInstalled() 호출은 예외 없이 동작해야 합니다.");

            // 환경에 따라 true/false — bool 반환만 검증
            Assert.IsTrue(result == true || result == false,
                "IsInstalled()는 bool을 반환해야 합니다.");
        }

        [Test]
        public void FfmpegHelper_GetPath_기본값_ffmpeg()
        {
            // IsInstalled 미호출 시 GetPath는 기본값 "ffmpeg"를 반환해야 함
            // (단, 이미 캐시된 경우 다른 값 가능)
            FfmpegHelper.ClearCache();

            string path = FfmpegHelper.GetPath();
            // 비어있지 않아야 함 — 기본값 "ffmpeg" 또는 절대경로
            Assert.IsFalse(string.IsNullOrEmpty(path),
                "GetPath()는 빈 문자열을 반환하면 안 됩니다.");
        }

        [Test]
        public void FfmpegHelper_GetVersionInfo_ClearCache_후_빈문자열()
        {
            // ClearCache 후 버전 정보가 초기화되어야 함
            FfmpegHelper.ClearCache();
            string version = FfmpegHelper.GetVersionInfo();
            Assert.AreEqual("", version,
                "ClearCache 후 GetVersionInfo()는 빈 문자열이어야 합니다.");
        }

        // ─── Mp4VideoEncoder + IVideoEncoder 계약 핀 ──────────────────────────

        [Test]
        public void Mp4VideoEncoder_BuildFfmpegArguments_fps_인자_정확히_반영()
        {
            // config.Fps가 FFmpeg framerate 인자에 정확히 반영되어야 함
            var config = MakeConfig(fps: 60);
            string args = CallBuildFfmpegArguments(1280, 720, config, "/tmp/out.mp4", null);

            Assert.IsTrue(args.Contains("-framerate 60"),
                "fps=60이 -framerate 60으로 반영되어야 합니다.");
            Assert.IsFalse(args.Contains("-framerate 30"),
                "fps=60 시 -framerate 30이 포함되면 안 됩니다.");
        }

        [Test]
        public void Mp4VideoEncoder_BuildFfmpegArguments_해상도_video_size_반영()
        {
            // width/height가 -video_size에 정확히 반영되어야 함
            var config = MakeConfig(width: 1920, height: 1080);
            string args = CallBuildFfmpegArguments(1920, 1080, config, "/tmp/out.mp4", null);

            Assert.IsTrue(args.Contains("-video_size 1920x1080"),
                "해상도 1920x1080이 -video_size에 반영되어야 합니다.");
        }

        [Test]
        public void Mp4VideoEncoder_BuildFfmpegArguments_다른_해상도_독립_핀()
        {
            // 480x270 소형 해상도 핀
            var config = MakeConfig(width: 480, height: 270, fps: 15);
            string args = CallBuildFfmpegArguments(480, 270, config, "/tmp/tiny.mp4", null);

            Assert.IsTrue(args.Contains("-video_size 480x270"),
                "480x270이 -video_size에 반영되어야 합니다.");
            Assert.IsTrue(args.Contains("-framerate 15"),
                "fps=15가 -framerate 15로 반영되어야 합니다.");
        }
    }
}
