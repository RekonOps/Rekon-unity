using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// Unity ScreenCapture API 기반 스크린샷 캡처 구현체.
    ///
    /// 캡처 흐름:
    ///   1. ScreenCapture.CaptureScreenshotAsTexture(downscale) → Texture2D (메인 스레드)
    ///   2. texture.EncodeToPNG() → byte[] (메인 스레드에서 PNG 인코딩)
    ///   3. File.WriteAllBytes → ThreadPool에서 비동기 파일 저장
    ///
    /// 주의: CaptureAsync()는 반드시 Unity 메인 스레드에서 호출해야 합니다.
    /// </summary>
    public class ScreenshotCapturer : IScreenshotCapturer
    {
        private readonly BugBeaconSettings _settings;

        /// <summary>
        /// BugBeaconSettings를 주입하여 인스턴스를 생성합니다.
        /// </summary>
        /// <param name="settings">screenshotDownscale 등 설정이 포함된 에셋</param>
        public ScreenshotCapturer(BugBeaconSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// 현재 화면을 PNG 바이트 배열로 캡처합니다.
        /// BugBeaconSettings.screenshotDownscale 배율로 해상도를 축소합니다.
        ///
        /// 반드시 Unity 메인 스레드에서 호출해야 합니다.
        /// </summary>
        /// <returns>PNG 인코딩된 바이트 배열. 실패 시 null</returns>
        public Task<byte[]> CaptureAsync()
        {
            try
            {
                int downscale = Mathf.Max(1, _settings.screenshotDownscale);

                // 1단계: 메인 스레드에서 화면 캡처
                Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture(downscale);

                if (texture == null)
                {
                    Debug.LogWarning("[BugBeacon] CaptureScreenshotAsTexture가 null을 반환했습니다.");
                    return Task.FromResult<byte[]>(null);
                }

                // 2단계: 메인 스레드에서 PNG 인코딩 (Texture2D.EncodeToPNG는 메인 스레드 필요)
                byte[] pngBytes = texture.EncodeToPNG();

                // 텍스처 메모리 해제
                UnityEngine.Object.Destroy(texture);

                if (pngBytes == null || pngBytes.Length == 0)
                {
                    Debug.LogWarning("[BugBeacon] EncodeToPNG가 빈 바이트 배열을 반환했습니다.");
                    return Task.FromResult<byte[]>(null);
                }

                return Task.FromResult(pngBytes);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugBeacon] 스크린샷 캡처 중 오류 발생: {ex.Message}");
                return Task.FromResult<byte[]>(null);
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
                Debug.LogWarning("[BugBeacon] 저장할 PNG 데이터가 없습니다.");
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

                Debug.Log($"[BugBeacon] 스크린샷 저장 완료: {filePath} ({pngBytes.Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugBeacon] 스크린샷 저장 실패 (경로: {filePath}): {ex.Message}");
                throw;
            }
        }
    }
}
