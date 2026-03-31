using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace RekonOps.Rekon
{
    /// <summary>
    /// Unity ScreenCapture API 기반 스크린샷 캡처 구현체.
    ///
    /// 캡처 흐름 (스터터링 최소화):
    ///   1. ScreenCapture.CaptureScreenshotAsTexture(downscale) → Texture2D (메인 스레드, 빠름)
    ///   2. texture.GetRawTextureData() → NativeArray 복사 (메인 스레드, 빠름)
    ///   3. Texture2D.Destroy (메인 스레드, 즉시 해제)
    ///   4. ImageConversion.EncodeNativeArrayToPNG → byte[] (백그라운드 스레드, CPU 집약적)
    ///
    /// 주의: CaptureAsync()는 반드시 Unity 메인 스레드에서 호출해야 합니다.
    /// </summary>
    public class ScreenshotCapturer : IScreenshotCapturer
    {
        private readonly RekonSettings _settings;
        private readonly MonoBehaviour _coroutineRunner;

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
        /// RekonSettings.screenshotDownscale 배율로 해상도를 축소합니다.
        ///
        /// 반드시 Unity 메인 스레드에서 호출해야 합니다.
        /// </summary>
        /// <returns>PNG 인코딩된 바이트 배열. 실패 시 null</returns>
        public Task<byte[]> CaptureAsync()
        {
            // WaitForEndOfFrame 코루틴 브릿지 — 렌더링 완료 후 캡처
            if (_coroutineRunner != null)
            {
                var tcs = new TaskCompletionSource<byte[]>();
                _coroutineRunner.StartCoroutine(CaptureAtEndOfFrameCoroutine(tcs));
                return tcs.Task;
            }

            // 폴백: 즉시 캡처 (테스트 환경 등 coroutineRunner 없을 때)
            return Task.FromResult(CaptureImmediate());
        }

        private IEnumerator CaptureAtEndOfFrameCoroutine(TaskCompletionSource<byte[]> tcs)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                int downscale = Mathf.Max(1, _settings.screenshotDownscale);

                // 1080p 최대 해상도 캡
                if (Screen.height > 1080 && downscale == 1)
                {
                    downscale = Mathf.CeilToInt((float)Screen.height / 1080f);
                    Debug.Log($"[Rekon] 스크린샷 자동 다운스케일: {downscale}x (화면 높이 {Screen.height}px > 1080px)");
                }

                // 1단계: 메인 스레드 — 화면 캡처 + raw 바이트 복사 (빠름, ~1ms)
                Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture(downscale);

                if (texture == null)
                {
                    Debug.LogWarning("[Rekon] CaptureScreenshotAsTexture가 null을 반환했습니다.");
                    tcs.SetResult(null);
                    yield break;
                }

                int width = texture.width;
                int height = texture.height;
                var format = texture.graphicsFormat;
                // raw 바이트를 관리 배열로 복사 (NativeArray → byte[])
                byte[] rawBytes = texture.GetRawTextureData<byte>().ToArray();
                UnityEngine.Object.Destroy(texture); // 텍스처 즉시 해제 — 메인 스레드 부담 최소화

                // 2단계: 백그라운드 스레드 — PNG 인코딩 (CPU 집약적, ~10~50ms)
                Task.Run(() =>
                {
                    NativeArray<byte> nativeArray = default;
                    try
                    {
                        nativeArray = new NativeArray<byte>(rawBytes, Allocator.Persistent);
                        byte[] pngBytes = ImageConversion.EncodeNativeArrayToPNG(
                            nativeArray, format, (uint)width, (uint)height).ToArray();

                        if (pngBytes == null || pngBytes.Length == 0)
                        {
                            tcs.SetResult(null);
                            return;
                        }
                        tcs.SetResult(pngBytes);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                    finally
                    {
                        if (nativeArray.IsCreated) nativeArray.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }

        private byte[] CaptureImmediate()
        {
            try
            {
                int downscale = Mathf.Max(1, _settings.screenshotDownscale);

                // 1080p 최대 해상도 캡
                if (Screen.height > 1080 && downscale == 1)
                {
                    downscale = Mathf.CeilToInt((float)Screen.height / 1080f);
                    Debug.Log($"[Rekon] 스크린샷 자동 다운스케일: {downscale}x (화면 높이 {Screen.height}px > 1080px)");
                }

                Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture(downscale);

                if (texture == null)
                {
                    Debug.LogWarning("[Rekon] CaptureScreenshotAsTexture가 null을 반환했습니다.");
                    return null;
                }

                byte[] pngBytes = texture.EncodeToPNG();
                UnityEngine.Object.Destroy(texture);

                if (pngBytes == null || pngBytes.Length == 0)
                {
                    Debug.LogWarning("[Rekon] EncodeToPNG가 빈 바이트 배열을 반환했습니다.");
                    return null;
                }

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
        /// ThreadPool(Task.Run)을 사용하여 파일 I/O를 메인 스레드에서 분리합니다.
        /// </summary>
        /// <param name="pngBytes">저장할 PNG 바이트 배열</param>
        /// <param name="filePath">저장할 파일 경로 (절대 경로 권장)</param>
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

                // ThreadPool에서 파일 쓰기 (메인 스레드 블로킹 방지)
                await Task.Run(() => File.WriteAllBytes(filePath, pngBytes));

                Debug.Log($"[Rekon] 스크린샷 저장 완료: {filePath} ({pngBytes.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 스크린샷 저장 실패 (경로: {filePath}): {ex.Message}");
                throw;
            }
        }
    }
}
