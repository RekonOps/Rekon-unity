using System.Threading.Tasks;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 영상 인코더 인터페이스.
    /// MVP 구현(PNG 시퀀스)과 향후 FFmpeg/MediaFoundation 구현을 동일 계약으로 교체 가능합니다.
    /// </summary>
    public interface IVideoEncoder
    {
        /// <summary>
        /// 프레임 배열을 지정 경로에 영상 파일로 인코딩합니다.
        /// </summary>
        /// <param name="frames">인코딩할 프레임 배열 (시간순 정렬됨)</param>
        /// <param name="outputPath">출력 파일 또는 디렉토리 경로</param>
        /// <param name="config">인코딩 설정</param>
        Task EncodeAsync(FrameData[] frames, string outputPath, VideoEncoderConfig config);
    }
}
