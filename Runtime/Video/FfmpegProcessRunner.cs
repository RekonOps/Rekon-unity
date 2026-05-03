#if UNITY_STANDALONE || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// IFfmpegProcessRunner 의 실제 구현체.
    /// System.Diagnostics.Process 를 사용하여 FFmpeg 프로세스를 실행합니다.
    ///
    /// 기존 Mp4VideoEncoder.RunFfmpegEncoding 의 Process.Start() 로직을 여기에 위임합니다.
    /// 동작 변화 없음 — seam 분리만 수행.
    /// </summary>
    public class FfmpegProcessRunner : IFfmpegProcessRunner
    {
        // FFmpeg 프로세스 최대 대기 시간 (밀리초): 5분
        private const int FfmpegTimeoutMs = 5 * 60 * 1000;

        // stderr 에서 유지할 마지막 줄 수
        private const int StderrTailLines = 10;

        // 마지막 실행 stderr 저장
        private string _lastStderr = "";

        /// <inheritdoc/>
        public string GetLastStderr() => _lastStderr;

        /// <inheritdoc/>
        public Task<int> RunAsync(
            string ffmpegPath,
            string args,
            byte[] stdinData,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => RunInternal(ffmpegPath, args, stdinData, null, cancellationToken), cancellationToken);
        }

        /// <inheritdoc/>
        public Task<int> RunWithFramesAsync(
            string ffmpegPath,
            string args,
            FrameData[] frames,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => RunInternal(ffmpegPath, args, null, frames, cancellationToken), cancellationToken);
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        private int RunInternal(
            string ffmpegPath,
            string args,
            byte[] stdinData,
            FrameData[] frames,
            CancellationToken cancellationToken)
        {
            _lastStderr = "";

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var stderrLines = new Queue<string>();
            bool hasError = false;

            using (var process = new Process())
            {
                process.StartInfo = startInfo;

                // stderr 비동기 읽기 (파이프 버퍼 포화로 인한 교착상태 방지)
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    lock (stderrLines)
                    {
                        stderrLines.Enqueue(e.Data);
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
                    UnityEngine.Debug.LogError(
                        $"[Rekon] FFmpeg 프로세스 시작 실패: {ex.Message}\n" +
                        $"FFmpeg 경로: {ffmpegPath}\n" +
                        "FfmpegHelper.IsInstalled()로 설치 여부를 확인하세요.");
                    throw;
                }

                // CancellationToken 등록: 취소 시 FFmpeg 프로세스 강제 종료
                using var ctReg = cancellationToken.Register(() =>
                {
                    try { process.Kill(); } catch { /* 무시 */ }
                });

                // stderr 비동기 읽기 시작
                process.BeginErrorReadLine();

                // stdin 데이터 전송
                try
                {
                    using (var stdin = process.StandardInput.BaseStream)
                    {
                        if (frames != null)
                        {
                            // 프레임 스트리밍 모드
                            int writtenFrames = 0;
                            for (int i = 0; i < frames.Length; i++)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                if (process.HasExited)
                                {
                                    UnityEngine.Debug.LogWarning(
                                        "[Rekon] FfmpegProcessRunner: FFmpeg 프로세스가 예기치 않게 종료되었습니다. stdin 쓰기를 중단합니다.");
                                    break;
                                }

                                var frame = frames[i];
                                if (!frame.IsValid)
                                {
                                    UnityEngine.Debug.LogWarning(
                                        $"[Rekon] FfmpegProcessRunner: 프레임 {i}번 건너뜀 (유효하지 않은 데이터).");
                                    continue;
                                }

                                try
                                {
                                    stdin.Write(frame.Data, 0, frame.DataLength);
                                    writtenFrames++;
                                }
                                catch (IOException ioEx)
                                {
                                    UnityEngine.Debug.LogWarning(
                                        $"[Rekon] FfmpegProcessRunner: stdin 쓰기 중 IOException (FFmpeg 종료 가능성): {ioEx.Message}");
                                    break;
                                }
                            }

                            UnityEngine.Debug.Log(
                                $"[Rekon] FFmpeg stdin 전송 완료: {writtenFrames}/{frames.Length}프레임");
                        }
                        else if (stdinData != null)
                        {
                            // 단일 바이트 배열 모드
                            stdin.Write(stdinData, 0, stdinData.Length);
                        }
                        // stdin 없으면 그냥 닫음
                    }
                }
                catch (OperationCanceledException)
                {
                    UnityEngine.Debug.LogWarning("[Rekon] FfmpegProcessRunner: 취소 요청으로 실행을 중단합니다.");
                    hasError = true;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[Rekon] FFmpeg stdin 쓰기 중 오류: {ex.Message}");
                    hasError = true;
                    try { process.Kill(); } catch { /* 무시 */ }
                }

                // 프로세스 종료 대기 (최대 5분)
                bool exited = process.WaitForExit(FfmpegTimeoutMs);

                if (!exited)
                {
                    UnityEngine.Debug.LogError(
                        $"[Rekon] FFmpeg 프로세스 타임아웃 ({FfmpegTimeoutMs / 1000}초 초과). 강제 종료합니다.");
                    try { process.Kill(); } catch { /* 무시 */ }
                    hasError = true;
                }

                // stderr 비동기 읽기 완료 대기 (최대 1초)
                try { process.WaitForExit(1000); } catch { /* 무시 */ }

                // stderr 저장
                lock (stderrLines)
                {
                    _lastStderr = string.Join("\n", stderrLines);
                }

                if (hasError)
                    return -1;

                if (exited && process.ExitCode != 0)
                {
                    UnityEngine.Debug.LogError(
                        $"[Rekon] FFmpeg 비정상 종료 (ExitCode={process.ExitCode}).\n" +
                        $"FFmpeg stderr (마지막 {StderrTailLines}줄):\n{_lastStderr}");
                }

                return exited ? process.ExitCode : -1;
            }
        }
    }
}
#endif
