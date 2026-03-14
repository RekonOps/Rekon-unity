#if UNITY_STANDALONE || UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugBeacon
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
        // FFmpeg 프로세스 최대 대기 시간 (밀리초): 5분
        private const int FfmpegTimeoutMs = 5 * 60 * 1000;

        // stderr에서 출력할 마지막 줄 수
        private const int StderrTailLines = 10;

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
                UnityEngine.Debug.LogWarning("[BugBeacon] Mp4VideoEncoder: 인코딩할 프레임이 없습니다.");
                return;
            }

            UnityEngine.Debug.Log($"[BugBeacon] MP4 인코딩 시작: {outputPath} ({frames.Length}프레임, {config})");

            await Task.Run(() => RunFfmpegEncoding(frames, outputPath, config, cancellationToken), cancellationToken);
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        private void RunFfmpegEncoding(FrameData[] frames, string outputPath, VideoEncoderConfig config, CancellationToken cancellationToken)
        {
            // 1. 출력 디렉토리 확인/생성
            string outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 2. FFmpeg 인수 구성
            //    - -y: 출력 파일 덮어쓰기
            //    - -f rawvideo: 입력 포맷 지정
            //    - -pix_fmt rgba: 입력 픽셀 포맷 (Unity RGBA32)
            //    - -video_size WxH: 프레임 해상도
            //    - -framerate N: 입력 프레임레이트
            //    - -i pipe:0: stdin에서 읽기
            //    - -vcodec libx264: H.264 인코더 사용
            //    - -pix_fmt yuv420p: 광범위한 플레이어 호환성을 위한 출력 픽셀 포맷
            //    - -preset ultrafast: 인코딩 속도 우선 (캡처 도구이므로 실시간성 중요)
            //    - -crf 23: 화질 설정 (0=무손실, 51=최저화질, 기본값 23)
            string ffmpegArgs = BuildFfmpegArguments(config, outputPath);
            string ffmpegPath = FfmpegHelper.GetPath();

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // stderr 수집용 버퍼
            var stderrLines = new System.Collections.Generic.Queue<string>();
            bool hasError = false;

            using (var process = new Process())
            {
                process.StartInfo = startInfo;

                // stderr 비동기 읽기 (동기로 읽으면 파이프 버퍼 포화로 교착상태 발생 가능)
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    lock (stderrLines)
                    {
                        stderrLines.Enqueue(e.Data);
                        // 최대 StderrTailLines 줄만 유지
                        while (stderrLines.Count > StderrTailLines)
                            stderrLines.Dequeue();
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[BugBeacon] FFmpeg 프로세스 시작 실패: {ex.Message}\n" +
                                               $"FFmpeg 경로: {ffmpegPath}\n" +
                                               "FfmpegHelper.IsInstalled()로 설치 여부를 확인하세요.");
                    throw;
                }

                // CancellationToken 등록: 취소 시 FFmpeg 프로세스 강제 종료
                // using var로 스코프 종료 시 자동 해제하여 메모리 누수 방지
                using var ctReg = cancellationToken.Register(() =>
                {
                    try { process.Kill(); } catch { /* 무시 */ }
                });

                // stderr 비동기 읽기 시작
                process.BeginErrorReadLine();

                // 3. 각 프레임의 Data를 stdin에 순차 write
                try
                {
                    using (var stdin = process.StandardInput.BaseStream)
                    {
                        int writtenFrames = 0;
                        for (int i = 0; i < frames.Length; i++)
                        {
                            // 취소 요청 시 루프 탈출
                            cancellationToken.ThrowIfCancellationRequested();

                            // FFmpeg 프로세스가 이미 종료된 경우 루프 탈출
                            if (process.HasExited)
                            {
                                UnityEngine.Debug.LogWarning("[BugBeacon] Mp4VideoEncoder: FFmpeg 프로세스가 예기치 않게 종료되었습니다. stdin 쓰기를 중단합니다.");
                                break;
                            }

                            var frame = frames[i];

                            // 유효하지 않은 프레임은 건너뜀
                            if (!frame.IsValid)
                            {
                                UnityEngine.Debug.LogWarning(
                                    $"[BugBeacon] Mp4VideoEncoder: 프레임 {i}번 건너뜀 (유효하지 않은 데이터).");
                                continue;
                            }

                            try
                            {
                                stdin.Write(frame.Data, 0, frame.Data.Length);
                                writtenFrames++;
                            }
                            catch (IOException ioEx)
                            {
                                // FFmpeg 프로세스 종료로 인한 파이프 깨짐 처리
                                UnityEngine.Debug.LogWarning($"[BugBeacon] Mp4VideoEncoder: stdin 쓰기 중 IOException (FFmpeg 종료 가능성): {ioEx.Message}");
                                break;
                            }
                        }

                        UnityEngine.Debug.Log($"[BugBeacon] FFmpeg stdin 전송 완료: {writtenFrames}/{frames.Length}프레임");
                    }
                    // stdin.Close()는 using 블록 종료 시 자동 호출됨
                }
                catch (OperationCanceledException)
                {
                    UnityEngine.Debug.LogWarning("[BugBeacon] Mp4VideoEncoder: 취소 요청으로 인코딩을 중단합니다.");
                    hasError = true;
                    // CancellationToken.Register에서 process.Kill()이 이미 호출됨
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[BugBeacon] FFmpeg stdin 쓰기 중 오류: {ex.Message}");
                    hasError = true;

                    try { process.Kill(); } catch { /* 무시 */ }
                }

                // 4. 프로세스 종료 대기 (최대 5분)
                bool exited = process.WaitForExit(FfmpegTimeoutMs);

                if (!exited)
                {
                    UnityEngine.Debug.LogError($"[BugBeacon] FFmpeg 프로세스 타임아웃 ({FfmpegTimeoutMs / 1000}초 초과). 강제 종료합니다.");
                    try { process.Kill(); } catch { /* 무시 */ }
                    hasError = true;
                }

                // stderr 비동기 읽기 완료 대기 (최대 1초)
                try { process.WaitForExit(1000); } catch { /* 무시 */ }

                if (!hasError && exited && process.ExitCode != 0)
                {
                    hasError = true;

                    // stderr 마지막 몇 줄을 에러 로그에 출력
                    string stderrSummary;
                    lock (stderrLines)
                    {
                        stderrSummary = string.Join("\n", stderrLines);
                    }

                    UnityEngine.Debug.LogError(
                        $"[BugBeacon] FFmpeg 비정상 종료 (ExitCode={process.ExitCode}).\n" +
                        $"FFmpeg stderr (마지막 {StderrTailLines}줄):\n{stderrSummary}");
                }

                // 5. 에러 시 손상된 파일 정리
                if (hasError && File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { /* 무시 */ }
                }

                // 6. 생성된 파일 존재/크기 확인 로그
                if (!hasError)
                {
                    if (File.Exists(outputPath))
                    {
                        long fileSize = new FileInfo(outputPath).Length;
                        UnityEngine.Debug.Log(
                            $"[BugBeacon] MP4 인코딩 완료: {outputPath} " +
                            $"({frames.Length}프레임, {fileSize / 1024.0:F1} KB)");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[BugBeacon] MP4 인코딩 완료했으나 출력 파일이 존재하지 않습니다: {outputPath}");
                    }
                }
                else
                {
                    // 비정상 종료 시 파일이 없으면 경고만 출력 (예외 미발생)
                    if (!File.Exists(outputPath))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[BugBeacon] FFmpeg 인코딩 실패로 출력 파일이 생성되지 않았습니다: {outputPath}");
                    }
                }
            }
        }

        private static string BuildFfmpegArguments(VideoEncoderConfig config, string outputPath)
        {
            // 경로에 포함된 따옴표 문자 제거 후 인용 처리
            string safeOutputPath = outputPath.Replace("\"", "");

            // -vf vflip: Unity 텍스처는 하단 원점(Y-up)이라 상하 반전 보정 필요
            return $"-y -f rawvideo -pix_fmt rgba -video_size {config.Width}x{config.Height} " +
                   $"-framerate {config.Fps} -i pipe:0 " +
                   $"-vf vflip -vcodec libx264 -pix_fmt yuv420p -preset ultrafast -crf {config.Crf} " +
                   $"\"{safeOutputPath}\"";
        }
    }
}
#endif
