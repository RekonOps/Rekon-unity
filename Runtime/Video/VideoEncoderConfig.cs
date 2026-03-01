namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 영상 인코딩에 필요한 설정 값을 담는 클래스.
    /// BugOneTouchSettings에서 생성하거나 직접 생성할 수 있습니다.
    /// </summary>
    public class VideoEncoderConfig
    {
        /// <summary>출력 영상 너비(픽셀)</summary>
        public int Width { get; set; }

        /// <summary>출력 영상 높이(픽셀)</summary>
        public int Height { get; set; }

        /// <summary>초당 프레임 수</summary>
        public int Fps { get; set; }

        /// <summary>목표 비트레이트(Mbps)</summary>
        public float BitrateMbps { get; set; }

        /// <summary>
        /// BugOneTouchSettings에서 VideoEncoderConfig를 생성합니다.
        /// </summary>
        public static VideoEncoderConfig FromSettings(BugOneTouchSettings settings)
        {
            if (settings == null)
                throw new System.ArgumentNullException(nameof(settings));

            return new VideoEncoderConfig
            {
                Width = settings.videoWidth,
                Height = settings.videoHeight,
                Fps = settings.videoFps,
                BitrateMbps = settings.videoBitrateMbps,
            };
        }

        public override string ToString()
        {
            return $"VideoEncoderConfig(W={Width}, H={Height}, FPS={Fps}, Bitrate={BitrateMbps}Mbps)";
        }
    }
}
