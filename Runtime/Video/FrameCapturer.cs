using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace RekonOps.Rekon
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
    /// GC 최적화:
    ///   FrameRingBuffer.AddFromNativeArray() / AddFromManagedArray()를 사용하여
    ///   사전 할당된 슬롯에 직접 복사합니다.
    ///   AsyncGPUReadback 콜백 이후 new byte[] 할당이 전혀 없습니다.
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
        private int _currentWidth;
        private int _currentHeight;
        private readonly WaitForEndOfFrame _waitForEndOfFrame = new WaitForEndOfFrame();

        public bool IsCapturing => _isCapturing;

        public void Initialize(FrameRingBuffer ringBuffer, VideoEncoderConfig config)
        {
            _ringBuffer = ringBuffer ?? throw new ArgumentNullException(nameof(ringBuffer));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _captureInterval = 1f / Mathf.Max(1, _config.Fps);
            _asyncGpuReadbackSupported = SystemInfo.supportsAsyncGPUReadback;

            // RT는 현재 화면 크기로 생성 (ScreenCapture가 화면 전체를 채우도록)
            // 화면 크기가 변경되면 CaptureLoopCoroutine에서 자동 재생성
            _currentWidth = Screen.width;
            _currentHeight = Screen.height;
            CreateRenderResources(_currentWidth, _currentHeight);

            Debug.Log($"[Rekon] FrameCapturer 초기화: {_currentWidth}x{_currentHeight}@{_config.Fps}fps, " +
                      $"AsyncGPUReadback={_asyncGpuReadbackSupported}");
        }

        public void StartCapturing()
        {
            if (_ringBuffer == null)
            {
                Debug.LogError("[Rekon] FrameCapturer가 초기화되지 않았습니다. Initialize()를 먼저 호출하세요.");
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

        private void CreateRenderResources(int width, int height)
        {
            ReleaseRenderResources();
            _renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _renderTexture.Create();
            _fallbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        }

        private void ReleaseRenderResources()
        {
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

        private void OnDestroy()
        {
            _isCapturing = false;
            if (_captureCoroutine != null)
            {
                StopCoroutine(_captureCoroutine);
                _captureCoroutine = null;
            }
            ReleaseRenderResources();
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

                // 화면 크기가 변경되면 RT/Texture 재생성 (에디터에서 Game Window 리사이즈 대응)
                int sw = Screen.width;
                int sh = Screen.height;
                if (sw != _currentWidth || sh != _currentHeight)
                {
                    _currentWidth = sw;
                    _currentHeight = sh;
                    CreateRenderResources(sw, sh);
                    Debug.Log($"[Rekon] 화면 크기 변경 감지: {sw}x{sh}, RT 재생성");
                }

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
            // width/height를 콜백 시점에서도 유효하도록 지역 변수로 캡처
            int captureWidth = _currentWidth;
            int captureHeight = _currentHeight;

            AsyncGPUReadback.Request(_renderTexture, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError) return;
                if (_config == null || _ringBuffer == null) return;

                // GC 할당 제거: NativeArray를 사전 할당 슬롯에 직접 복사
                var data = request.GetData<byte>();
                _ringBuffer.AddFromNativeArray(data, captureWidth, captureHeight, timestamp);
            });
        }

        private void CaptureWithReadPixelsFallback(double timestamp)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            try
            {
                // _fallbackTexture를 재사용하여 매 프레임 new/Destroy로 인한 GC 부담 방지
                _fallbackTexture.ReadPixels(new Rect(0, 0, _currentWidth, _currentHeight), 0, 0, false);
                _fallbackTexture.Apply();
                // GetRawTextureData()는 내부 버퍼 참조를 반환하므로 직접 슬롯에 복사
                byte[] raw = _fallbackTexture.GetRawTextureData();
                _ringBuffer?.AddFromManagedArray(raw, raw.Length, _currentWidth, _currentHeight, timestamp);
            }
            finally
            {
                RenderTexture.active = prev;
            }
        }
    }
}
