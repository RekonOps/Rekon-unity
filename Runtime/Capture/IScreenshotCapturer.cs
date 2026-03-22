using System.Threading.Tasks;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 스크린샷 캡처 전략 인터페이스.
    /// 플랫폼별 구현체 또는 테스트용 목(Mock)으로 교체 가능합니다.
    /// </summary>
    public interface IScreenshotCapturer
    {
        /// <summary>
        /// 현재 프레임을 PNG 바이트 배열로 캡처하여 반환합니다.
        /// </summary>
        /// <returns>PNG 인코딩된 바이트 배열. 실패 시 null</returns>
        Task<byte[]> CaptureAsync();

        /// <summary>
        /// PNG 바이트 배열을 지정 경로에 비동기로 저장합니다.
        /// </summary>
        /// <param name="pngBytes">PNG 바이트 배열</param>
        /// <param name="filePath">저장할 파일 경로</param>
        Task SaveAsync(byte[] pngBytes, string filePath);
    }
}
