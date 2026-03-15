using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// Camera.main을 이용해 설정된 FPS에 맞춰 프레임을 캡처하여 FrameRingBuffer에 저장합니다.
    ///
    /// 캡처 전략:
    ///   1. AsyncGPUReadback.Request() 지원 시 비동기 GPU 읽기 사용 (성능 우선)
    ///   2. 미지원 시 ReadPixels 폴백 (동기, 메인 스레드 블로킹)
    ///
    /// camera.Render()를 사용하여 설정된 해상도(기본 1280x720)로 직접 렌더링합니다.
    /// 이 방식은 게임 윈도우 크기와 무관하게 일정한 해상도로 캡처됩니다.
    ///
    /// FPS 스로틀링:
    ///   Time.unscaledTime 기반으로 프레임 간격을 계산하여 목표 FPS를 유지합니다.
    /// </summary>
    public class FrameCapturer : MonoBehaviour, IFrameCapturer
    {
        private FrameRingBuffer _ringBuffer;
        private VideoEncoderConfig _config;
        private RenderTexture _renderTexture;
        private bool _isCapturing;
        private float _lastCaptureTime;
        private float _captureInterval;
        private bool _asyncGpuReadbackSupported;

        /// <summary>현재 캡처가 진행 중인지 여부</summary>
        public bool IsCapturing => _isCapturing;

        /// <summary>
        /// FrameCapturer를 초기화합니다.
        /// </summary>
        /// <param name="ringBuffer">프레임을 저장할 링버퍼</param>
        /// <param name="config">캡처 설정 (해상도, FPS 등)</param>
        public void Initialize(FrameRingBuffer ringBuffer, VideoEncoderConfig config)
        {
            _ringBuffer = ringBuffer ?? throw new ArgumentNullException(nameof(ringBuffer));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _captureInterval = 1f / Mathf.Max(1, _config.Fps);
            _asyncGpuReadbackSupported = SystemInfo.supportsAsyncGPUReadback;

            // RenderTexture 생성
            _renderTexture = new RenderTexture(_config.Width, _config.Height, 24, RenderTextureFormat.ARGB32);
            _renderTexture.Create();

            Debug.Log($"[BugBeacon] FrameCapturer 초기화: {_config.Width}x{_config.Height}@{_config.Fps}fps, " +
                      $"AsyncGPUReadback={_asyncGpuReadbackSupported}");
        }

        /// <summary>
        /// 프레임 캡처를 시작합니다.
        /// </summary>
        public void StartCapturing()
        {
            if (_ringBuffer == null)
            {
                Debug.LogError("[BugBeacon] FrameCapturer가 초기화되지 않았습니다. Initialize()를 먼저 호출하세요.");
                return;
            }
            _isCapturing = true;
            _lastCaptureTime = Time.unscaledTime;
        }

        /// <summary>
        /// 프레임 캡처를 중지합니다.
        /// </summary>
        public void StopCapturing()
        {
            _isCapturing = false;
        }

        private void OnDestroy()
        {
            _isCapturing = false;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }

        private void Update()
        {
            if (!_isCapturing)
                return;

            float now = Time.unscaledTime;
            if (now - _lastCaptureTime < _captureInterval)
                return;

            _lastCaptureTime = now;
            CaptureFrame(now);
        }

        // ──────────────────────────────────────────────────────────────
        // 캡처 로직
        // ──────────────────────────────────────────────────────────────

        private void CaptureFrame(float timestamp)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[BugBeacon] Camera.main이 없습니다. 프레임 캡처 건너뜀.");
                return;
            }

            if (_asyncGpuReadbackSupported)
            {
                CaptureWithAsyncReadback(camera, timestamp);
            }
            else
            {
                CaptureWithReadPixels(camera, timestamp);
            }
        }

        /// <summary>
        /// AsyncGPUReadback을 사용한 비동기 캡처.
        /// GPU → CPU 전송이 비동기로 처리되어 메인 스레드 블로킹이 없습니다.
        /// </summary>
        private void CaptureWithAsyncReadback(Camera camera, float timestamp)
        {
            var prevTarget = camera.targetTexture;
            camera.targetTexture = _renderTexture;
            camera.Render();
            camera.targetTexture = prevTarget;

            double captureTimestamp = (double)timestamp;

            AsyncGPUReadback.Request(_renderTexture, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    Debug.LogWarning("[BugBeacon] AsyncGPUReadback 실패. ReadPixels로 폴백 없음.");
                    return;
                }

                // 에디터 도메인 리로드 등으로 _config/_ringBuffer가 해제된 경우 방어
                if (_config == null || _ringBuffer == null) return;

                var data = request.GetData<byte>();
                byte[] bytes = new byte[data.Length];
                data.CopyTo(bytes);

                var frame = new FrameData(bytes, _config.Width, _config.Height, captureTimestamp);
                _ringBuffer.Add(frame);
            });
        }

        /// <summary>
        /// ReadPixels를 사용한 동기 폴백 캡처.
        /// 메인 스레드를 잠시 블로킹합니다.
        /// </summary>
        private void CaptureWithReadPixels(Camera camera, float timestamp)
        {
            var prevTarget = camera.targetTexture;
            camera.targetTexture = _renderTexture;
            camera.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = _renderTexture;

            try
            {
                var texture = new Texture2D(_config.Width, _config.Height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, _config.Width, _config.Height), 0, 0, false);
                texture.Apply();

                byte[] bytes = texture.GetRawTextureData();

                var frame = new FrameData(bytes, _config.Width, _config.Height, timestamp);
                _ringBuffer?.Add(frame);

                Destroy(texture);
            }
            finally
            {
                RenderTexture.active = prev;
                camera.targetTexture = prevTarget;
            }
        }
    }
}
