using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace RekonOps.Rekon
{
    /// <summary>
    /// AsyncGPUReadback 기반 스크린샷 캡처 구현체.
    ///
    /// 캡처 흐름 (스터터링 완전 제거):
    ///   1. WaitForEndOfFrame 대기
    ///   2. CaptureScreenshotIntoRenderTexture(rt) → GPU 버퍼 복사 (~0.1ms, 논블로킹)
    ///   3. AsyncGPUReadback.Request(rt, callback) → 비동기 GPU→CPU 전송 요청
    ///   4. 2~3프레임 후 콜백 수신 → NativeArray 데이터 확보
    ///   5. Task.Run(EncodeNativeArrayToPNG) → 백그라운드 PNG 인코딩
    ///
    /// GPU Stall 제거 원리:
    ///   기존 CaptureScreenshotAsTexture는 GPU 파이프라인을 강제로 비우는 동기 readback을
    ///   수행하여 8~30ms의 메인 스레드 블로킹을 유발함.
    ///   AsyncGPUReadback은 요청만 하고 즉시 반환하므로 메인 스레드를 블로킹하지 않음.
    /// </summary>
    public class ScreenshotCapturer : IScreenshotCapturer
    {
        private readonly RekonSettings _settings;
        private readonly MonoBehaviour _coroutineRunner;

        // RT 재사용으로 GPU 메모리 할당 비용 제거
        private RenderTexture _cachedRenderTexture;
        private int _cachedWidth;
        private int _cachedHeight;

        /// <summary>
        /// RekonSettings를 주입하여 인스턴스를 생성합니다.
        /// </summary>
        /// <param name="settings">screenshotDownscale 등 설정이 포함된 에셋</param>
        /// <param name="coroutineRunner">WaitForEndOfFrame 코루틴 실행용 MonoBehaviour (null이면 즉시 캡처 시도)</param>
        public ScreenshotCapturer(RekonSettings settings, MonoBehaviour coroutineRunner = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _coroutineRunner = coroutineRunner;
        }

        /// <summary>
        /// 현재 화면을 PNG 바이트 배열로 캡처합니다.
        /// AsyncGPUReadback을 사용하여 메인 스레드 블로킹 없이 캡처합니다.
        /// </summary>
        /// <returns>PNG 인코딩된 바이트 배열. 실패 시 null</returns>
        public Task<byte[]> CaptureAsync()
        {
            if (_coroutineRunner != null)
            {
                var tcs = new TaskCompletionSource<byte[]>();
                _coroutineRunner.StartCoroutine(CaptureCoroutine(tcs));
                return tcs.Task;
            }

            // coroutineRunner 없을 때 폴백 (테스트 환경)
            return Task.FromResult(CaptureImmediate());
        }

        private IEnumerator CaptureCoroutine(TaskCompletionSource<byte[]> tcs)
        {
            yield return new WaitForEndOfFrame();

            try
            {
                // downscale 계산
                int downscale = Mathf.Max(1, _settings.screenshotDownscale);
                if (Screen.height > 1080 && downscale == 1)
                {
                    downscale = Mathf.CeilToInt((float)Screen.height / 1080f);
                    Debug.Log($"[Rekon] 스크린샷 자동 다운스케일: {downscale}x (화면 높이 {Screen.height}px > 1080px)");
                }

                int targetW = Screen.width / downscale;
                int targetH = Screen.height / downscale;

                // RT 재사용 (크기 변경 시에만 재생성)
                EnsureRenderTexture(targetW, targetH);

                // 1단계: 화면을 RT에 복사 (빠름, ~0.1ms, 메인 스레드 비블로킹)
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_cachedRenderTexture);

                // 2단계: 비동기 GPU→CPU 읽기 요청 (즉시 반환, 2~3프레임 후 콜백)
                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    var capturedWidth = targetW;
                    var capturedHeight = targetH;

                    AsyncGPUReadback.Request(_cachedRenderTexture, 0, TextureFormat.RGBA32,
                        request =>
                        {
                            if (request.hasError)
                            {
                                Debug.LogWarning("[Rekon] AsyncGPUReadback 오류.");
                                tcs.TrySetResult(null);
                                return;
                            }

                            // NativeArray 데이터 확보 (콜백은 메인 스레드에서 호출됨)
                            var data = request.GetData<byte>();
                            byte[] rawBytes = data.ToArray(); // 복사 (콜백 수명 이후에도 유효해야 함)

                            // 3단계: 백그라운드 PNG 인코딩
                            Task.Run(() =>
                            {
                                NativeArray<byte> nativeArray = default;
                                try
                                {
                                    nativeArray = new NativeArray<byte>(rawBytes, Allocator.Persistent);
                                    byte[] pngBytes = ImageConversion.EncodeNativeArrayToPNG(
                                        nativeArray,
                                        GraphicsFormat.R8G8B8A8_SRGB,
                                        (uint)capturedWidth,
                                        (uint)capturedHeight
                                    ).ToArray();
                                    tcs.TrySetResult(pngBytes?.Length > 0 ? pngBytes : null);
                                }
                                catch (Exception ex)
                                {
                                    tcs.TrySetException(ex);
                                }
                                finally
                                {
                                    if (nativeArray.IsCreated) nativeArray.Dispose();
                                }
                            });
                        });
                }
                else
                {
                    // AsyncGPUReadback 미지원 환경 폴백 (구형 모바일 등)
                    Debug.LogWarning("[Rekon] AsyncGPUReadback 미지원, 동기 폴백 사용");
                    yield return null; // CaptureScreenshotIntoRenderTexture 반영 대기 1프레임

                    var prev = RenderTexture.active;
                    RenderTexture.active = _cachedRenderTexture;
                    var fallback = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
                    fallback.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0, false);
                    fallback.Apply();
                    RenderTexture.active = prev;

                    var format = fallback.graphicsFormat;
                    byte[] rawBytes = fallback.GetRawTextureData<byte>().ToArray();
                    UnityEngine.Object.Destroy(fallback);

                    Task.Run(() =>
                    {
                        NativeArray<byte> nativeArray = default;
                        try
                        {
                            nativeArray = new NativeArray<byte>(rawBytes, Allocator.Persistent);
                            byte[] pngBytes = ImageConversion.EncodeNativeArrayToPNG(
                                nativeArray, format, (uint)targetW, (uint)targetH).ToArray();
                            tcs.TrySetResult(pngBytes?.Length > 0 ? pngBytes : null);
                        }
                        catch (Exception ex) { tcs.TrySetException(ex); }
                        finally { if (nativeArray.IsCreated) nativeArray.Dispose(); }
                    });
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private void EnsureRenderTexture(int width, int height)
        {
            if (_cachedRenderTexture != null &&
                _cachedWidth == width &&
                _cachedHeight == height)
                return;

            if (_cachedRenderTexture != null)
            {
                _cachedRenderTexture.Release();
                UnityEngine.Object.Destroy(_cachedRenderTexture);
            }

            _cachedRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _cachedRenderTexture.Create();
            _cachedWidth = width;
            _cachedHeight = height;
            Debug.Log($"[Rekon] 스크린샷 RT 생성: {width}x{height}");
        }

        private byte[] CaptureImmediate()
        {
            // 테스트/폴백용 동기 캡처 (기존 로직 유지)
            try
            {
                int downscale = Mathf.Max(1, _settings.screenshotDownscale);
                if (Screen.height > 1080 && downscale == 1)
                {
                    downscale = Mathf.CeilToInt((float)Screen.height / 1080f);
                }

                var texture = ScreenCapture.CaptureScreenshotAsTexture(downscale);
                if (texture == null) return null;
                var pngBytes = texture.EncodeToPNG();
                UnityEngine.Object.Destroy(texture);
                return pngBytes;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 스크린샷 캡처 중 오류 발생: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// PNG 바이트 배열을 지정 경로에 비동기로 저장합니다.
        /// </summary>
        public async Task SaveAsync(byte[] pngBytes, string filePath)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                Debug.LogWarning("[Rekon] 저장할 PNG 데이터가 없습니다.");
                return;
            }

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                await Task.Run(() => File.WriteAllBytes(filePath, pngBytes));
                Debug.Log($"[Rekon] 스크린샷 저장 완료: {filePath} ({pngBytes.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 스크린샷 저장 실패 (경로: {filePath}): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// RenderTexture 리소스 해제. RekonBootstrap Dispose에서 호출.
        /// </summary>
        public void Dispose()
        {
            if (_cachedRenderTexture != null)
            {
                _cachedRenderTexture.Release();
                UnityEngine.Object.Destroy(_cachedRenderTexture);
                _cachedRenderTexture = null;
            }
        }
    }
}
