#if UNITY_STANDALONE || UNITY_EDITOR
using System.Threading;
using System.Threading.Tasks;

namespace RekonOps.Rekon
{
    /// <summary>
    /// FFmpeg 프로세스 실행을 추상화하는 seam 인터페이스.
    ///
    /// 목적:
    ///   - Mp4VideoEncoder에서 Process.Start() 직접 의존을 제거하여
    ///     단위 테스트 시 Mock으로 대체 가능하도록 함.
    ///   - 실제 구현: FfmpegProcessRunner (Process.Start 래핑)
    ///   - 테스트 구현: MockFfmpegProcessRunner
    /// </summary>
    public interface IFfmpegProcessRunner
    {
        /// <summary>
        /// FFmpeg 프로세스를 실행합니다.
        /// </summary>
        /// <param name="ffmpegPath">FFmpeg 실행 파일 경로 (예: "ffmpeg" 또는 "/opt/homebrew/bin/ffmpeg")</param>
        /// <param name="args">FFmpeg 명령줄 인수</param>
        /// <param name="stdinData">stdin 으로 전달할 raw 바이트 데이터 (null 이면 stdin 없음)</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>FFmpeg 프로세스 종료 코드 (0 = 성공)</returns>
        Task<int> RunAsync(
            string ffmpegPath,
            string args,
            byte[] stdinData,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// FFmpeg 프레임 스트리밍 전용 실행.
        /// 여러 프레임을 순차적으로 stdin 에 쓰는 방식.
        /// </summary>
        /// <param name="ffmpegPath">FFmpeg 실행 파일 경로</param>
        /// <param name="args">FFmpeg 명령줄 인수</param>
        /// <param name="frames">인코딩할 프레임 배열</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>FFmpeg 프로세스 종료 코드 (0 = 성공)</returns>
        Task<int> RunWithFramesAsync(
            string ffmpegPath,
            string args,
            FrameData[] frames,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 마지막으로 실행된 프로세스의 stderr 출력을 반환합니다.
        /// RunAsync / RunWithFramesAsync 호출 이후에 유효한 값을 가집니다.
        /// </summary>
        string GetLastStderr();
    }
}
#endif
