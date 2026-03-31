using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 실시간 스트리밍 영상 녹화기.
    /// FFmpeg를 백그라운드 프로세스로 실행하여 프레임을 실시간 인코딩합니다.
    /// 메모리 사용량: ~30MB (ConcurrentQueue에 3~8프레임만 보관)
    /// </summary>
    public class StreamingVideoRecorder : IDisposable
    {
        // 설정
        private readonly int _fps;
        private readonly string _gpuEncoder;
        private int _width;
        private int _height;

        // FFmpeg 프로세스
        private Process _ffmpegProcess;
        private Stream _stdin;
        private string _rollingFilePath;

        // 프레임 큐 (bounded, 게임 프레임 보호)
        private readonly ConcurrentQueue<FramePacket> _frameQueue = new ConcurrentQueue<FramePacket>();
        private const int MaxQueueSize = 8;
        private volatile int _queueCount;

        // 쓰기 스레드
        private Thread _writerThread;
        private volatile bool _isRecording;
        private volatile bool _disposed;

        // 통계
        private long _framesWritten;
        private long _framesDropped;

        // 프레임 패킷 (byte[] 재사용을 위해 구조체 사용)
        private struct FramePacket
        {
            public byte[] Data;
            public int Length;
        }

        public bool IsRecording => _isRecording;
        public long FramesWritten => _framesWritten;
        public long FramesDropped => _framesDropped;

        public StreamingVideoRecorder(int fps, string gpuEncoder = null)
        {
            _fps = Mathf.Max(1, fps);
            _gpuEncoder = gpuEncoder;
        }

        /// <summary>
        /// FFmpeg 프로세스를 시작하고 녹화를 시작합니다.
        /// </summary>
        public bool Start(int width, int height)
        {
            if (_isRecording) return true;

            _width = width;
            _height = height;

            // 임시 파일 경로
            _rollingFilePath = Path.Combine(
                Application.temporaryCachePath,
                "Rekon",
                $"rolling_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            var dir = Path.GetDirectoryName(_rollingFilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // FFmpeg 프로세스 시작
            if (!StartFfmpegProcess()) return false;

            // 쓰기 스레드 시작
            _isRecording = true;
            _framesWritten = 0;
            _framesDropped = 0;
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "RekonVideoWriter" };
            _writerThread.Start();

            Debug.Log($"[Rekon] 스트리밍 녹화 시작: {_width}x{_height}@{_fps}fps → {_rollingFilePath}");
            return true;
        }

        /// <summary>
        /// 프레임을 큐에 추가합니다. 큐가 가득 차면 드랍 (게임 프레임 보호).
        /// 메인 스레드에서 호출됩니다.
        /// </summary>
        public void EnqueueFrame(byte[] data, int length)
        {
            if (!_isRecording || _disposed) return;

            // 큐 가득 차면 드랍 (게임 프레임 보호 > 녹화 프레임 보존)
            if (_queueCount >= MaxQueueSize)
            {
                Interlocked.Increment(ref _framesDropped);
                return;
            }

            _frameQueue.Enqueue(new FramePacket { Data = data, Length = length });
            Interlocked.Increment(ref _queueCount);
        }

        /// <summary>
        /// 녹화를 중지하고 FFmpeg를 종료합니다.
        /// 마지막 N초를 추출한 파일 경로를 반환합니다.
        /// </summary>
        public async Task<string> StopAndExtractAsync(int lastSeconds)
        {
            if (!_isRecording) return null;
            _isRecording = false;

            // stdin 닫기 → FFmpeg 정상 종료
            try { _stdin?.Close(); } catch { }

            // 쓰기 스레드 종료 대기 (Restart 경쟁 방지)
            if (_writerThread != null)
            {
                if (!_writerThread.Join(5000))
                    Debug.LogWarning("[Rekon] 쓰기 스레드 종료 타임아웃 (5초). 강제 진행.");
                _writerThread = null;
            }

            // FFmpeg 종료 대기
            if (_ffmpegProcess != null)
            {
                if (!_ffmpegProcess.HasExited && !_ffmpegProcess.WaitForExit(10000))
                    try { _ffmpegProcess.Kill(); } catch { }
                try { _ffmpegProcess.Dispose(); } catch { }
                _ffmpegProcess = null;
            }

            Debug.Log($"[Rekon] 스트리밍 녹화 종료: {_framesWritten}프레임 기록, {_framesDropped}프레임 드랍");

            // rolling.mp4에서 마지막 N초 추출 (stream copy, 매우 빠름)
            if (!File.Exists(_rollingFilePath)) return null;

            // lastSeconds가 0이면 전체 파일 반환 (해상도 변경 등 내부 재시작 시)
            if (lastSeconds <= 0)
            {
                string rollingPath = _rollingFilePath;
                _rollingFilePath = null;
                return rollingPath;
            }

            string outputPath = Path.Combine(
                Path.GetDirectoryName(_rollingFilePath),
                $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            bool trimSuccess = await TrimLastSecondsAsync(_rollingFilePath, outputPath, lastSeconds);

            // rolling 파일 정리
            try { File.Delete(_rollingFilePath); } catch { }

            return trimSuccess ? outputPath : null;
        }

        /// <summary>
        /// 녹화를 중지하고 새로 시작합니다 (트리거 후 계속 녹화).
        /// </summary>
        public bool Restart()
        {
            // 기존 큐 비우기
            while (_frameQueue.TryDequeue(out _))
                Interlocked.Decrement(ref _queueCount);

            return Start(_width, _height);
        }

        // ─── 내부 메서드 ───

        private bool StartFfmpegProcess()
        {
            string ffmpegPath = FfmpegHelper.GetPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                Debug.LogError("[Rekon] FFmpeg를 찾을 수 없습니다.");
                return false;
            }

            // 인코더 결정: GPU 우선 → CPU 폴백
            string encoder = !string.IsNullOrEmpty(_gpuEncoder) ? _gpuEncoder : "libx264";
            string encoderArgs = GetEncoderArgs(encoder);

            string args = $"-y -f rawvideo -pix_fmt rgba -video_size {_width}x{_height} " +
                          $"-framerate {_fps} -i pipe:0 " +
                          $"{encoderArgs} " +
                          $"-movflags +faststart \"{_rollingFilePath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            };

            try
            {
                _ffmpegProcess = Process.Start(startInfo);
                _stdin = _ffmpegProcess.StandardInput.BaseStream;

                // stderr 비동기 읽기 (데드락 방지)
                _ffmpegProcess.BeginErrorReadLine();

                Debug.Log($"[Rekon] FFmpeg 시작: encoder={encoder}, {_width}x{_height}@{_fps}fps");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] FFmpeg 시작 실패: {ex.Message}");
                return false;
            }
        }

        private static string GetEncoderArgs(string encoder)
        {
            // 하드웨어 인코더별 최적 설정
            switch (encoder)
            {
                case "h264_nvenc":        return "-c:v h264_nvenc -preset p4 -rc vbr -cq 23 -pix_fmt yuv420p";
                case "h264_amf":          return "-c:v h264_amf -quality speed -rc cqp -qp_i 23 -qp_p 23 -pix_fmt yuv420p";
                case "h264_videotoolbox": return "-c:v h264_videotoolbox -q:v 65 -pix_fmt yuv420p";
                case "h264_qsv":          return "-c:v h264_qsv -preset veryfast -global_quality 23 -pix_fmt yuv420p";
                default:                  return "-c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p";
            }
        }

        private void WriterLoop()
        {
            while (_isRecording || _queueCount > 0)
            {
                if (_frameQueue.TryDequeue(out var packet))
                {
                    Interlocked.Decrement(ref _queueCount);

                    try
                    {
                        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                        {
                            _stdin.Write(packet.Data, 0, packet.Length);
                            Interlocked.Increment(ref _framesWritten);
                        }
                    }
                    catch (IOException)
                    {
                        // FFmpeg 프로세스가 종료됨 — 정상 (StopAndExtract 시)
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Rekon] FFmpeg 쓰기 오류: {ex.Message}");
                        break;
                    }
                }
                else
                {
                    // 큐가 비어있으면 1ms 대기 (CPU 사용량 최소화)
                    Thread.Sleep(1);
                }
            }
        }

        private static async Task<bool> TrimLastSecondsAsync(string inputPath, string outputPath, int lastSeconds)
        {
            string ffmpegPath = FfmpegHelper.GetPath();
            if (string.IsNullOrEmpty(ffmpegPath)) return false;

            // -sseof: 파일 끝에서 N초 전부터 추출 (stream copy, 매우 빠름)
            string args = $"-y -sseof -{lastSeconds} -i \"{inputPath}\" -c copy \"{outputPath}\"";

            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                    };

                    using var process = Process.Start(startInfo);
                    process.BeginErrorReadLine();
                    bool exited = process.WaitForExit(30000);
                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    return process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Rekon] FFmpeg trim 실패: {ex.Message}");
                    return false;
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _isRecording = false;

            try { _stdin?.Close(); } catch { }
            try { _writerThread?.Join(3000); } catch { }
            try { if (_ffmpegProcess != null && !_ffmpegProcess.HasExited) _ffmpegProcess.Kill(); } catch { }
            try { _ffmpegProcess?.Dispose(); } catch { }

            // rolling 파일 정리
            try { if (!string.IsNullOrEmpty(_rollingFilePath) && File.Exists(_rollingFilePath)) File.Delete(_rollingFilePath); } catch { }
        }
    }
}
