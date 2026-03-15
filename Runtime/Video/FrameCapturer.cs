using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// ScreenCapture.CaptureScreenshotIntoRenderTexture()를 사용하여
    /// 게임 화면을 캡처하고 FrameRingBuffer에 저장합니다.
    ///
    /// 캡처 전략:
    ///   WaitForEndOfFrame 이후 ScreenCapture API로 현재 화면을 RenderTexture에 복사.
    ///   camera.Render() 추가 렌더링이 없어 스터터링이 발생하지 않습니다.
    ///
    /// GPU 읽기:
    ///   1. AsyncGPUReadback.Request() 지원 시 비동기 GPU 읽기 (성능 우선)
    ///   2. 미지원 시 ReadPixels 폴백 (동기, 메인 스레드 블로킹)
    ///
    /// UI 포함:
    ///   ScreenCapture는 UI를 포함한 최종 화면을 캡처합니다.
    ///   WaitForEndOfFrame 시점에서 이미 렌더링이 완료된 백버퍼를 복사하므로
    ///   별도의 Canvas 조작 없이 게임 화면 + UI가 그대로 녹화됩니다.
    /// </summary>
    public class FrameCapturer : MonoBehaviour, IFrameCapturer
    {
        private FrameRingBuffer _ringBuffer;
        private VideoEncoderConfig _config;
        private RenderTexture _renderTexture;
        private Texture2D _fallbackTexture; // ReadPixels 폴백 시 재사용할 텍스처 (매 프레임 new/Destroy 방지)
        private bool _isCapturing;
        private float _lastCaptureTime;
        private float _captureInterval;
        private bool _asyncGpuReadbackSupported;
        private Coroutine _captureCoroutine;
        private readonly WaitForEndOfFrame _waitForEndOfFrame = new WaitForEndOfFrame();

        public bool IsCapturing => _isCapturing;

        public void Initialize(FrameRingBuffer ringBuffer, VideoEncoderConfig config)
        {
            _ringBuffer = ringBuffer ?? throw new ArgumentNullException(nameof(ringBuffer));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _captureInterval = 1f / Mathf.Max(1, _config.Fps);
            _asyncGpuReadbackSupported = SystemInfo.supportsAsyncGPUReadback;

            _renderTexture = new RenderTexture(_config.Width, _config.Height, 0, RenderTextureFormat.ARGB32);
            _renderTexture.Create();

            // AsyncGPUReadback 미지원 플랫폼을 위한 폴백 텍스처를 1회 생성하여 재사용
            _fallbackTexture = new Texture2D(_config.Width, _config.Height, TextureFormat.RGBA32, false);

            Debug.Log($"[BugBeacon] FrameCapturer 초기화: {_config.Width}x{_config.Height}@{_config.Fps}fps, " +
                      $"AsyncGPUReadback={_asyncGpuReadbackSupported}");
        }

        public void StartCapturing()
        {
            if (_ringBuffer == null)
            {
                Debug.LogError("[BugBeacon] FrameCapturer가 초기화되지 않았습니다. Initialize()를 먼저 호출하세요.");
                return;
            }
            if (_isCapturing) return;

            _isCapturing = true;
            _lastCaptureTime = Time.unscaledTime;
            _captureCoroutine = StartCoroutine(CaptureLoopCoroutine());
        }

        public void StopCapturing()
        {
            _isCapturing = false;
            if (_captureCoroutine != null)
            {
                StopCoroutine(_captureCoroutine);
                _captureCoroutine = null;
            }
        }

        private void OnDestroy()
        {
            _isCapturing = false;
            if (_captureCoroutine != null)
            {
                StopCoroutine(_captureCoroutine);
                _captureCoroutine = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_fallbackTexture != null)
            {
                Destroy(_fallbackTexture);
                _fallbackTexture = null;
            }
        }

        private IEnumerator CaptureLoopCoroutine()
        {
            while (_isCapturing)
            {
                yield return _waitForEndOfFrame;

                // FPS 스로틀링: unscaledTime 기반
                float now = Time.unscaledTime;
                if (now - _lastCaptureTime < _captureInterval)
                    continue;

                _lastCaptureTime = now;

                // 현재 화면을 RenderTexture에 캡처 (렌더링 완료 후, 추가 렌더링 없음)
                // WaitForEndOfFrame 이후이므로 UI를 포함한 최종 화면이 그대로 캡처됨
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_renderTexture);

                if (_asyncGpuReadbackSupported)
                {
                    EnqueueAsyncReadback((double)now);
                }
                else
                {
                    CaptureWithReadPixelsFallback((double)now);
                }
            }
        }

        private void EnqueueAsyncReadback(double timestamp)
        {
            AsyncGPUReadback.Request(_renderTexture, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError) return;
                if (_config == null || _ringBuffer == null) return;

                var data = request.GetData<byte>();
                byte[] bytes = new byte[data.Length];
                data.CopyTo(bytes);

                _ringBuffer.Add(new FrameData(bytes, _config.Width, _config.Height, timestamp));
            });
        }

        private void CaptureWithReadPixelsFallback(double timestamp)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            try
            {
                // _fallbackTexture를 재사용하여 매 프레임 new/Destroy로 인한 GC 부담 방지
                _fallbackTexture.ReadPixels(new Rect(0, 0, _config.Width, _config.Height), 0, 0, false);
                _fallbackTexture.Apply();
                // GetRawTextureData()는 내부 버퍼 참조를 반환하므로 반드시 복사해야 함
                // _fallbackTexture 재사용 시 이전 프레임 데이터가 덮어씌워지는 것을 방지
                byte[] raw = _fallbackTexture.GetRawTextureData();
                byte[] bytes = new byte[raw.Length];
                Buffer.BlockCopy(raw, 0, bytes, 0, raw.Length);
                _ringBuffer?.Add(new FrameData(bytes, _config.Width, _config.Height, timestamp));
            }
            finally
            {
                RenderTexture.active = prev;
            }
        }
    }
}
