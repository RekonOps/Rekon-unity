#if UNITY_STANDALONE || UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// FFmpeg를 사용하여 FrameData 배열을 MP4 파일로 인코딩하는 클래스.
    ///
    /// 동작 방식:
    ///   FFmpeg 프로세스를 시작하고 stdin 파이프를 통해 raw RGBA 프레임 데이터를 순차 전송합니다.
    ///   FFmpeg는 libx264 코덱을 사용하여 yuv420p 픽셀 포맷의 MP4 파일을 생성합니다.
    ///
    /// 사전 조건:
    ///   시스템 PATH에 FFmpeg가 설치되어 있어야 합니다. FfmpegHelper.IsInstalled()로 확인하세요.
    ///
    /// 스레드:
    ///   Task.Run을 사용하여 모든 I/O를 백그라운드 스레드에서 처리합니다.
    ///   메인 스레드(Unity 게임 루프)를 블로킹하지 않습니다.
    /// </summary>
    public class Mp4VideoEncoder : IVideoEncoder
    {
        // IFfmpegProcessRunner seam — 테스트 시 Mock으로 대체 가능
        private readonly IFfmpegProcessRunner _runner;

        /// <summary>
        /// 기본 생성자. FfmpegProcessRunner 를 사용합니다 (기존 caller 호환).
        /// </summary>
        public Mp4VideoEncoder() : this(new FfmpegProcessRunner()) { }

        /// <summary>
        /// IFfmpegProcessRunner 주입 생성자. 테스트 시 Mock 주입에 사용합니다.
        /// </summary>
        /// <param name="runner">FFmpeg 프로세스 실행 구현체</param>
        public Mp4VideoEncoder(IFfmpegProcessRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        /// <summary>출력 파일 확장자.</summary>
        public string OutputExtension => ".mp4";

        /// <summary>인코딩 시 권장 타임아웃 (초)</summary>
        public float RecommendedTimeoutSeconds => 180f;

        /// <summary>
        /// 프레임 배열을 MP4 파일로 비동기 인코딩합니다.
        /// </summary>
        /// <param name="frames">인코딩할 프레임 배열 (시간순 정렬됨, RGBA32 포맷)</param>
        /// <param name="outputPath">출력 MP4 파일 경로 (예: /path/to/video.mp4)</param>
        /// <param name="config">인코딩 설정 (Width, Height, Fps, BitrateMbps)</param>
        /// <param name="cancellationToken">취소 토큰</param>
        public async Task EncodeAsync(FrameData[] frames, string outputPath, VideoEncoderConfig config, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentNullException(nameof(outputPath));

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (frames == null || frames.Length == 0)
            {
                UnityEngine.Debug.LogWarning("[Rekon] Mp4VideoEncoder: 인코딩할 프레임이 없습니다.");
                return;
            }

            UnityEngine.Debug.Log($"[Rekon] MP4 인코딩 시작: {outputPath} ({frames.Length}프레임, {config})");

            await Task.Run(() =>
            {
                // GPU 인코더를 먼저 시도하고, 실패 시 CPU(libx264) fallback
                string gpuEncoder = FfmpegHelper.GetGpuEncoder();
                bool success = false;

                if (!string.IsNullOrEmpty(gpuEncoder))
                {
                    UnityEngine.Debug.Log($"[Rekon] GPU 인코더 시도: {gpuEncoder}");
                    success = TryRunFfmpeg(frames, outputPath, config, gpuEncoder, cancellationToken);

                    if (!success)
                    {
                        UnityEngine.Debug.LogWarning($"[Rekon] GPU 인코더 실패 ({gpuEncoder}). CPU 인코더(libx264)로 재시도합니다.");
                        // 손상된 중간 파일 정리
                        if (System.IO.File.Exists(outputPath))
                        {
                            try { System.IO.File.Delete(outputPath); } catch { /* 무시 */ }
                        }
                    }
                }

                if (!success)
                {
                    // CPU fallback (libx264) — encoderName null이면 libx264 사용
                    UnityEngine.Debug.Log("[Rekon] CPU 인코더(libx264)로 인코딩합니다.");
                    RunFfmpegEncoding(frames, outputPath, config, cancellationToken, null);
                }
            }, cancellationToken);
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// GPU 인코더로 인코딩을 시도합니다.
        /// 성공(exit code 0, 파일 생성)이면 true, 실패 시 false 반환.
        /// </summary>
        private bool TryRunFfmpeg(FrameData[] frames, string outputPath, VideoEncoderConfig config, string encoderName, CancellationToken cancellationToken)
        {
            try
            {
                RunFfmpegEncoding(frames, outputPath, config, cancellationToken, encoderName);
                // RunFfmpegEncoding 내부에서 hasError 시 파일을 삭제하므로, 파일 존재 여부로 성공 판단
                return File.Exists(outputPath);
            }
            catch (OperationCanceledException)
            {
                // 취소는 상위로 전파
                throw;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Rekon] TryRunFfmpeg({encoderName}) 예외: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void RunFfmpegEncoding(FrameData[] frames, string outputPath, VideoEncoderConfig config, CancellationToken cancellationToken, string encoderName = null)
        {
            // 1. 출력 디렉토리 확인/생성
            string outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 2. FFmpeg 인수 구성 (프레임의 실제 해상도 사용, 화면 크기에 따라 동적)
            int frameWidth = frames[0].Width;
            int frameHeight = frames[0].Height;
            string ffmpegArgs = BuildFfmpegArguments(frameWidth, frameHeight, config, outputPath, encoderName);
            string ffmpegPath = FfmpegHelper.GetPath();

            // 3. _runner를 통해 FFmpeg 프로세스 실행 (seam 위임)
            //    RunWithFramesAsync 는 내부에서 Task.Run 을 사용하므로 .GetAwaiter().GetResult() 로 동기 대기
            int exitCode;
            try
            {
                exitCode = _runner.RunWithFramesAsync(ffmpegPath, ffmpegArgs, frames, cancellationToken)
                                  .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                UnityEngine.Debug.LogWarning("[Rekon] Mp4VideoEncoder: 취소 요청으로 인코딩을 중단합니다.");
                if (File.Exists(outputPath))
                    try { File.Delete(outputPath); } catch { /* 무시 */ }
                throw;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Rekon] FFmpeg 실행 중 오류: {ex.Message}");
                if (File.Exists(outputPath))
                    try { File.Delete(outputPath); } catch { /* 무시 */ }
                throw;
            }

            bool hasError = exitCode != 0;

            // 4. 에러 시 손상된 파일 정리
            if (hasError && File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* 무시 */ }
            }

            // 5. 생성된 파일 존재/크기 확인 로그
            if (!hasError)
            {
                if (File.Exists(outputPath))
                {
                    long fileSize = new FileInfo(outputPath).Length;
                    UnityEngine.Debug.Log(
                        $"[Rekon] MP4 인코딩 완료: {outputPath} " +
                        $"({frames.Length}프레임, {fileSize / 1024.0:F1} KB)");
                }
                else
                {
                    UnityEngine.Debug.LogWarning(
                        $"[Rekon] MP4 인코딩 완료했으나 출력 파일이 존재하지 않습니다: {outputPath}");
                }
            }
            else
            {
                // 비정상 종료 시 파일이 없으면 경고만 출력 (예외 미발생)
                if (!File.Exists(outputPath))
                {
                    UnityEngine.Debug.LogWarning(
                        $"[Rekon] FFmpeg 인코딩 실패로 출력 파일이 생성되지 않았습니다: {outputPath}");
                }
            }
        }

        /// <summary>
        /// 인코더별 최적 인수를 포함한 FFmpeg 명령줄 인수를 구성합니다.
        /// encoderName이 null이면 libx264 CPU 인코더를 사용합니다.
        /// </summary>
        private static string BuildFfmpegArguments(int width, int height, VideoEncoderConfig config, string outputPath, string encoderName)
        {
            // 경로에 포함된 따옴표 문자 제거 후 인용 처리
            string safeOutputPath = outputPath.Replace("\"", "");

            // 인코더별 최적 인수 선택 (모든 인코더에 -pix_fmt yuv420p 통일)
            string encoderArgs = encoderName switch
            {
                // NVIDIA NVENC: 가변 비트레이트 + CQ 품질 제어
                "h264_nvenc"        => $"-vcodec h264_nvenc -preset p4 -rc vbr -cq {config.Crf}",
                // AMD AMF: CQP 고정 품질 모드
                "h264_amf"          => $"-vcodec h264_amf -quality speed -rc cqp -qp_i {config.Crf} -qp_p {config.Crf}",
                // Apple VideoToolbox: 기본 품질 사용 (-b:v, -q:v 제거 — FFmpeg 최신 버전 호환)
                "h264_videotoolbox" => $"-vcodec h264_videotoolbox",
                // Intel Quick Sync: global_quality로 품질 제어
                "h264_qsv"          => $"-vcodec h264_qsv -preset veryfast -global_quality {config.Crf}",
                // CPU fallback (libx264): 기존 동작과 동일
                _                   => $"-vcodec libx264 -preset ultrafast -crf {config.Crf}",
            };

            // 입력이 1080p 초과 시 다운스케일 (원본 비율 유지, 2의 배수 보장)
            string scaleFilter = (height > 1080)
                ? "-vf scale=-2:1080"
                : "";

            // Y축 반전은 호출 측에서 데이터를 역순 전달하여 처리
            // (Mp4VideoEncoder는 레거시 링버퍼 경로이며, FrameRingBuffer.GetFrames()에서 처리)
            return $"-y -f rawvideo -pix_fmt rgba -video_size {width}x{height} " +
                   $"-framerate {config.Fps} -i pipe:0 " +
                   $"{scaleFilter} -pix_fmt yuv420p " +
                   $"{encoderArgs} " +
                   $"\"{safeOutputPath}\"";
        }

        /// <summary>
        /// libx264의 CRF 값(0~51)을 VideoToolbox의 q 값(1~100)으로 변환합니다.
        /// CRF가 낮을수록 고화질 → q가 높을수록 고화질.
        /// </summary>
        private static int MapCrfToVideoToolboxQ(int crf)
        {
            return Mathf.Clamp(100 - Mathf.RoundToInt(crf * (99f / 51f)), 1, 100);
        }
    }
}
#endif
