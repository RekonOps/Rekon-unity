using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private string _gpuEncoder;
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

        // 프레임 버퍼 풀 (ConcurrentBag)
        private ConcurrentBag<byte[]> _frameBufferPool;
        private int _frameBufferSize;

        // 쓰기 스레드
        private Thread _writerThread;
        private volatile bool _isRecording;
        private volatile bool _disposed;

        // 통계
        private long _framesWritten;
        private long _framesDropped;

        // 영상-로그 싱크: 현재 rolling 세션이 시작된 realtime 시각.
        //   Start() 단일 지점에서만 갱신(호출처 모두 메인스레드: FrameCapturer.Update / 해상도변경 / Restart 연속체).
        //   에디터 저캡처(예: 30fps 목표인데 실제 ~16fps)로 인코딩 길이가 wall 시간보다 짧아지는 문제를
        //   추출 시 itsscale 로 보정하기 위한 기준점.
        private double _recordingStartRealtime;

        // FFmpeg stderr 수집 (디버깅용)
        private readonly Queue<string> _stderrLines = new Queue<string>();
        private const int StderrTailLines = 10;


        // 프레임 패킷 (byte[] 재사용을 위해 구조체 사용)
        private struct FramePacket
        {
            public byte[] Data;
            public int Length;
        }

        public bool IsRecording => _isRecording;
        public long FramesWritten => _framesWritten;
        public long FramesDropped => _framesDropped;

        /// <summary>현재 rolling 세션이 시작된 realtime 시각(로그 t_abs 와 동일 축). 영상-로그 싱크용.</summary>
        public double RecordingStartRealtime => _recordingStartRealtime;

        public StreamingVideoRecorder(int fps, string gpuEncoder = null)
        {
            _fps = Mathf.Max(1, fps);
            _gpuEncoder = gpuEncoder;
        }

        /// <summary>
        /// FFmpeg stdin용 버퍼를 대여합니다.
        /// </summary>
        /// <param name="requiredLength">필요한 byte 수</param>
        /// <param name="buffer">대여한 버퍼</param>
        /// <returns>대여 성공 여부</returns>
        public bool TryRentFrameBuffer(int requiredLength, out byte[] buffer)
        {
            buffer = null;
            if (_disposed || !_isRecording || _frameBufferPool == null)
                return false;

            if (requiredLength <= 0 || _frameBufferSize != requiredLength)
                return false;

            return _frameBufferPool.TryTake(out buffer);
        }

        /// <summary>
        /// FFmpeg 프로세스를 시작하고 녹화를 시작합니다.
        /// </summary>
        public bool Start(int width, int height)
        {
            if (_isRecording) return true;

            _width = width;
            _height = height;

            // 이전 세션의 stderr 로그 초기화
            lock (_stderrLines) { _stderrLines.Clear(); }

            int frameBufferSize = _width * _height * 4;
            InitializeFrameBufferPool(frameBufferSize);

            // 임시 파일 경로
            _rollingFilePath = Path.Combine(
                Application.temporaryCachePath,
                "Rekon",
                $"rolling_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

            var dir = Path.GetDirectoryName(_rollingFilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // FFmpeg 프로세스 시작
            if (!StartFfmpegProcess())
            {
                // GPU 인코더 실패 시 libx264로 폴백 (1회만)
                if (!string.IsNullOrEmpty(_gpuEncoder))
                {
                    string stderrFallback;
                    lock (_stderrLines) { stderrFallback = string.Join("\n", _stderrLines); }
                    Debug.LogWarning($"[Rekon] {_gpuEncoder} 인코딩 시작 실패, libx264로 폴백. stderr:\n{stderrFallback}");

                    // 실패한 프로세스 정리 (종료 완료 대기 후 해제)
                    try
                    {
                        if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                        {
                            _ffmpegProcess.Kill();
                            _ffmpegProcess.WaitForExit(3000);
                        }
                    }
                    catch { }
                    try { _ffmpegProcess?.Dispose(); } catch { }
                    _ffmpegProcess = null;
                    _stdin = null;

                    _gpuEncoder = null; // 이번 세션에서 libx264 사용
                    lock (_stderrLines) { _stderrLines.Clear(); }
                    if (!StartFfmpegProcess()) return false;
                }
                else
                {
                    return false;
                }
            }

            // 쓰기 스레드 시작
            _isRecording = true;
            _framesWritten = 0;
            _framesDropped = 0;
            // rolling 세션 시작 시각 기록(메인스레드). 추출 시 wall 길이 = trigger - 이 값.
            _recordingStartRealtime = Time.realtimeSinceStartupAsDouble;
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "RekonVideoWriter" };
            _writerThread.Start();

            Debug.Log($"[Rekon] 스트리밍 녹화 시작: {_width}x{_height}@{_fps}fps → {_rollingFilePath}");
            return true;
        }

        /// <summary>
        /// 프레임을 큐에 추가합니다. 큐가 가득 차면 드랍 (게임 프레임 보호).
        /// 메인 스레드에서 호출됩니다.
        /// </summary>
        public bool EnqueueFrame(byte[] data, int length)
        {
            if (_disposed || !_isRecording)
            {
                TryReturnFrameBuffer(data);
                return false;
            }

            if (data == null || length <= 0 || _frameBufferPool == null || _frameBufferSize <= 0)
            {
                TryReturnFrameBuffer(data);
                return false;
            }

            // 잘못된 길이/크기 데이터는 누수 방지를 위해 즉시 반환
            if (length > data.Length || _frameBufferSize != data.Length)
            {
                TryReturnFrameBuffer(data);
                return false;
            }

            // 큐 가득 차면 드랍 (게임 프레임 보호 > 녹화 프레임 보존)
            if (_queueCount >= MaxQueueSize)
            {
                Interlocked.Increment(ref _framesDropped);
                TryReturnFrameBuffer(data);
                return false;
            }

            _frameQueue.Enqueue(new FramePacket { Data = data, Length = length });
            Interlocked.Increment(ref _queueCount);
            return true;
        }

        /// <summary>
        /// 녹화를 중지하고 FFmpeg를 종료합니다.
        /// 마지막 N초를 추출한 파일 경로를 반환합니다.
        /// </summary>
        /// <param name="lastSeconds">추출할 마지막 구간 길이(초). 0 이하면 전체 파일 반환.</param>
        /// <param name="wallSpanSeconds">
        ///   rolling 세션의 실제 wall 시간(초). &gt;0 이고 lastSeconds 이내이면 itsscale 로
        ///   인코딩 길이를 wall 길이에 맞게 늘려(재인코딩 0) 에디터 저캡처 빨리감기를 보정한다.
        ///   0 이면 기존 -sseof 추출(보정 없음).
        /// </param>
        public async Task<string> StopAndExtractAsync(int lastSeconds, double wallSpanSeconds = 0)
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

            // 종료되지 못한 큐는 모두 반납
            ReturnQueuedFrameBuffers();

            // FFmpeg 종료 대기
            if (_ffmpegProcess != null)
            {
                if (!_ffmpegProcess.HasExited && !_ffmpegProcess.WaitForExit(10000))
                    try { _ffmpegProcess.Kill(); } catch { }

                // FFmpeg 비정상 종료 시 stderr 출력
                if (_ffmpegProcess.HasExited && _ffmpegProcess.ExitCode != 0)
                {
                    string stderrExit;
                    lock (_stderrLines) { stderrExit = string.Join("\n", _stderrLines); }
                    Debug.LogWarning($"[Rekon] FFmpeg 비정상 종료 (code={_ffmpegProcess.ExitCode}). stderr:\n{stderrExit}");
                }

                try { _ffmpegProcess.Dispose(); } catch { }
                _ffmpegProcess = null;
            }

            Debug.Log($"[Rekon] 스트리밍 녹화 종료: {_framesWritten}프레임 기록, {_framesDropped}프레임 드랍");

            // rolling.mp4에서 마지막 N초 추출 (stream copy, 매우 빠름)
            if (!File.Exists(_rollingFilePath))
            {
                string stderrMissing;
                lock (_stderrLines) { stderrMissing = string.Join("\n", _stderrLines); }
                if (!string.IsNullOrEmpty(stderrMissing))
                    Debug.LogWarning($"[Rekon] FFmpeg 영상 파일 미생성. stderr:\n{stderrMissing}");
                else
                    Debug.LogWarning("[Rekon] FFmpeg 영상 파일 미생성 (stderr 없음)");
                return null;
            }

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

            // 인코딩 길이(초): 고정 framerate(CFR) 인코딩이라 프레임수/fps = 컨테이너 nominal duration.
            //   ffprobe 없이 메모리 카운터로 결정적으로 산출(외부 프로세스 0, 실패 경로 없음).
            double encodedSeconds = _framesWritten / (double)Mathf.Max(1, _fps);

            // 스트레치 적용 조건(메타데이터 산출부와 동일한 공유 기준):
            //   wall 길이가 유효(>0.5s)하고 buffer(lastSeconds) 이내일 때만 whole-file itsscale.
            //   (세션이 buffer 보다 길면 마지막 N초만 필요 → whole-file 스트레치 불가 → 기존 -sseof 폴백)
            //   encodedSeconds>0.1 은 프레임 0 인 degenerate 케이스 방어.
            bool canStretch = wallSpanSeconds > 0.5
                              && wallSpanSeconds <= lastSeconds + 1.0
                              && encodedSeconds > 0.1;

            bool extractSuccess;
            if (canStretch)
            {
                // K = wall / encoded (저캡처면 encoded < wall → K ≥ 1, 영상이 느려져 실시간 길이로 복원).
                double k = wallSpanSeconds / encodedSeconds;
                Debug.Log($"[Rekon] 영상 스트레치 보정: wall={wallSpanSeconds:F2}s, encoded={encodedSeconds:F2}s, K={k:F3}");
                extractSuccess = await StretchAndExtractAsync(_rollingFilePath, outputPath, k);
                if (!extractSuccess)
                {
                    // fail-safe: itsscale 실패/무효 mp4 → 기존 -sseof 추출로 폴백
                    Debug.LogWarning("[Rekon] itsscale 스트레치 실패 — -sseof 추출로 폴백");
                    extractSuccess = await TrimLastSecondsAsync(_rollingFilePath, outputPath, lastSeconds);
                }
            }
            else
            {
                extractSuccess = await TrimLastSecondsAsync(_rollingFilePath, outputPath, lastSeconds);
            }

            // rolling 파일 정리
            try { File.Delete(_rollingFilePath); } catch { }

            return extractSuccess ? outputPath : null;
        }

        /// <summary>
        /// 녹화를 중지하고 새로 시작합니다 (트리거 후 계속 녹화).
        /// </summary>
        public bool Restart()
        {
            // 기존 큐 비우기 (비우는 과정에서 풀 반환)
            while (_frameQueue.TryDequeue(out var droppedPacket))
            {
                TryReturnFrameBuffer(droppedPacket.Data);
                Interlocked.Decrement(ref _queueCount);
            }
            Interlocked.Exchange(ref _queueCount, 0);

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

            // 입력이 1080p 초과 시 다운스케일 (원본 비율 유지, 2의 배수 보장)
            string scaleFilter = (_height > 1080)
                ? "-vf scale=-2:1080"
                : "";

            string args = $"-y -f rawvideo -pix_fmt rgba -video_size {_width}x{_height} " +
                          $"-framerate {_fps} -i pipe:0 " +
                          $"{scaleFilter} -pix_fmt yuv420p " +
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

                // stderr 수집 핸들러 등록 (디버깅용 rolling 버퍼)
                _ffmpegProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (_stderrLines)
                        {
                            _stderrLines.Enqueue(e.Data);
                            while (_stderrLines.Count > StderrTailLines)
                                _stderrLines.Dequeue();
                        }
                    }
                };

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
                case "h264_nvenc": return "-c:v h264_nvenc -preset p4 -rc vbr -cq 23";
                case "h264_amf": return "-c:v h264_amf -quality speed -rc cqp -qp_i 23 -qp_p 23";
                case "h264_videotoolbox": return "-c:v h264_videotoolbox";
                case "h264_qsv": return "-c:v h264_qsv -preset veryfast -global_quality 23";
                default: return "-c:v libx264 -preset ultrafast -crf 23";
            }
        }

        private void InitializeFrameBufferPool(int frameBufferSize)
        {
            if (frameBufferSize <= 0)
                return;

            if (_frameBufferPool != null && _frameBufferSize == frameBufferSize)
                return;

            _frameBufferSize = frameBufferSize;
            _frameBufferPool = new ConcurrentBag<byte[]>();
            for (int i = 0; i < MaxQueueSize; i++)
            {
                _frameBufferPool.Add(new byte[frameBufferSize]);
            }
        }

        private void TryReturnFrameBuffer(byte[] buffer)
        {
            if (_disposed || _frameBufferPool == null || buffer == null)
                return;

            if (_frameBufferSize != buffer.Length)
                return;

            if (_frameBufferPool.Count < MaxQueueSize)
            {
                _frameBufferPool.Add(buffer);
            }
        }

        private void ReturnQueuedFrameBuffers()
        {
            while (_frameQueue.TryDequeue(out var packet))
            {
                TryReturnFrameBuffer(packet.Data);
            }

            Interlocked.Exchange(ref _queueCount, 0);
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
                            if (packet.Data != null && packet.Length > 0)
                            {
                                // AsyncGPUReadback 데이터는 플랫폼 무관하게 top-down
                                // → 변환 없이 그대로 FFmpeg에 전달
                                int bytesToWrite = Math.Min(packet.Length, packet.Data.Length);
                                _stdin.Write(packet.Data, 0, bytesToWrite);
                                Interlocked.Increment(ref _framesWritten);
                            }
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
                    finally
                    {
                        TryReturnFrameBuffer(packet.Data);
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

        /// <summary>
        /// rolling 파일 전체를 -itsscale 로 K배 늘려 추출합니다(재인코딩 없음, stream copy).
        ///   에디터 저캡처로 인코딩 길이가 wall 시간보다 짧을 때 실시간 속도로 복원하는 용도.
        ///   whole-file 대상이라 -sseof 는 사용하지 않습니다(세션 ≤ buffer 일 때만 호출됨).
        ///   K 는 로케일 무관하게 InvariantCulture(소수점) 로 포맷합니다.
        /// </summary>
        private static async Task<bool> StretchAndExtractAsync(string inputPath, string outputPath, double k)
        {
            string ffmpegPath = FfmpegHelper.GetPath();
            if (string.IsNullOrEmpty(ffmpegPath)) return false;

            // -itsscale: 입력 타임스탬프를 K배로 스케일(컨테이너 duration 만 늘림, 프레임 데이터 무변경).
            string kStr = k.ToString("0.000000", CultureInfo.InvariantCulture);
            string args = $"-y -itsscale {kStr} -i \"{inputPath}\" -c copy \"{outputPath}\"";

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

                    // 정상 종료 + 유효한 출력(존재 + 비어있지 않음) 까지 확인 → 무효 mp4 면 false(폴백 유도)
                    if (process.ExitCode != 0) return false;
                    try { return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0; }
                    catch { return false; }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Rekon] FFmpeg itsscale 실패: {ex.Message}");
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

            // 종료되지 못한 큐 반환
            ReturnQueuedFrameBuffers();

            try { if (_ffmpegProcess != null && !_ffmpegProcess.HasExited) _ffmpegProcess.Kill(); } catch { }
            try { _ffmpegProcess?.Dispose(); } catch { }

            // rolling 파일 정리
            try { if (!string.IsNullOrEmpty(_rollingFilePath) && File.Exists(_rollingFilePath)) File.Delete(_rollingFilePath); } catch { }

            _frameBufferPool = null;
            _frameBufferSize = 0;
        }
    }
}
