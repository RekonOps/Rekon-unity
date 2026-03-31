using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 30초 단위 슬라이딩 윈도우로 영상 세그먼트를 디스크에 플러시합니다.
    /// 최대 2세그먼트(총 60초)를 유지하며, 초과분은 삭제합니다.
    ///
    /// 세그먼트 디렉토리 구조:
    ///   basePath/
    ///     seg_000001/  (가장 최신)
    ///       frame_*.raw
    ///       metadata.json
    ///     seg_000000/  (이전)
    ///       ...
    ///
    /// 스레드 안전성:
    ///   FlushSegmentAsync는 한 번에 하나만 실행되어야 합니다.
    /// </summary>
    public class VideoSegmentWriter : IDisposable
    {
        private const int MaxSegments = 2;
        private const float SegmentDurationSeconds = 30f;

        private readonly string _basePath;
        private readonly FrameRingBuffer _ringBuffer;
        private readonly IVideoEncoder _encoder;
        private readonly VideoEncoderConfig _config;

        private int _segmentIndex;
        private float _lastFlushTime;
        private bool _disposed;

        // 현재 유지 중인 세그먼트 디렉토리 목록 (오래된 것부터)
        private readonly Queue<string> _segmentPaths = new Queue<string>();

        private readonly object _flushLock = new object();
        private Task _currentFlushTask = Task.CompletedTask;

        /// <summary>
        /// VideoSegmentWriter를 초기화합니다.
        /// </summary>
        /// <param name="basePath">세그먼트 파일을 저장할 기본 경로</param>
        /// <param name="ringBuffer">프레임을 읽어올 링버퍼</param>
        /// <param name="encoder">사용할 영상 인코더</param>
        /// <param name="config">인코딩 설정</param>
        public VideoSegmentWriter(
            string basePath,
            FrameRingBuffer ringBuffer,
            IVideoEncoder encoder,
            VideoEncoderConfig config)
        {
            _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
            _ringBuffer = ringBuffer ?? throw new ArgumentNullException(nameof(ringBuffer));
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _lastFlushTime = UnityEngine.Time.unscaledTime;
        }

        /// <summary>
        /// 주기적으로 호출되어야 합니다 (MonoBehaviour.Update 권장).
        /// SegmentDurationSeconds마다 자동으로 세그먼트를 플러시합니다.
        /// </summary>
        public void Tick()
        {
            if (_disposed)
                return;

            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastFlushTime >= SegmentDurationSeconds)
            {
                _lastFlushTime = now;
                _ = FlushSegmentAsync();
            }
        }

        /// <summary>
        /// 현재 링버퍼의 프레임을 새 세그먼트로 플러시합니다.
        /// 최대 MaxSegments개를 초과하면 가장 오래된 세그먼트를 삭제합니다.
        /// </summary>
        public async Task FlushSegmentAsync()
        {
            if (_disposed)
                return;

            // 이전 플러시가 완료될 때까지 대기
            Task prevFlush;
            lock (_flushLock)
            {
                prevFlush = _currentFlushTask;
            }

            await prevFlush;

            // TakeFrames(): 소유권 이전 — 내부 슬롯이 null로 비워지고 DoFlushAsync가 byte[] 반환 책임을 가집니다.
            var frames = _ringBuffer.TakeFrames();
            if (frames.Length == 0)
                return;

            string segDir = Path.Combine(_basePath, $"seg_{_segmentIndex:D6}");
            _segmentIndex++;

            Task flushTask = DoFlushAsync(frames, segDir);
            lock (_flushLock)
                _currentFlushTask = flushTask;

            await flushTask;
        }

        /// <summary>
        /// 모든 세그먼트를 즉시 삭제하고 초기화합니다.
        /// </summary>
        public void ClearSegments()
        {
            while (_segmentPaths.Count > 0)
            {
                string dir = _segmentPaths.Dequeue();
                TryDeleteDirectory(dir);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        private async Task DoFlushAsync(FrameData[] frames, string segDir)
        {
            try
            {
                await _encoder.EncodeAsync(frames, segDir, _config, cancellationToken: default);

                _segmentPaths.Enqueue(segDir);

                // 최대 세그먼트 수 초과 시 가장 오래된 것 삭제
                while (_segmentPaths.Count > MaxSegments)
                {
                    string oldest = _segmentPaths.Dequeue();
                    TryDeleteDirectory(oldest);
                }

                Debug.Log($"[Rekon] 세그먼트 플러시 완료: {segDir} ({frames.Length}프레임, 유지 중: {_segmentPaths.Count})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 세그먼트 플러시 실패: {ex.Message}");
            }
            finally
            {
                // TakeFrames()로 소유권을 이전받았으므로, 인코딩 완료(또는 실패) 후 여기서 반환합니다.
                var pool = ArrayPool<byte>.Shared;
                foreach (var f in frames)
                {
                    if (f.Data != null)
                        pool.Return(f.Data);
                }
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] 세그먼트 디렉토리 삭제 실패 ({path}): {ex.Message}");
            }
        }
    }
}
