namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 영상 인코딩에 필요한 설정 값을 담는 클래스.
    /// BugBeaconSettings에서 생성하거나 직접 생성할 수 있습니다.
    /// </summary>
    public class VideoEncoderConfig
    {
        /// <summary>출력 영상 너비(픽셀)</summary>
        public int Width { get; set; }

        /// <summary>출력 영상 높이(픽셀)</summary>
        public int Height { get; set; }

        /// <summary>초당 프레임 수</summary>
        public int Fps { get; set; } = 15;

        /// <summary>목표 비트레이트(Mbps)</summary>
        public float BitrateMbps { get; set; }

        /// <summary>
        /// CRF(Constant Rate Factor) 품질 값. 0=무손실, 51=최저화질, 기본값 23.
        /// 값이 높을수록 파일 크기가 작아지고 화질이 낮아집니다.
        /// </summary>
        public int Crf { get; set; } = 23;

        /// <summary>
        /// 출력 파일 최대 크기(바이트). 0이면 제한 없음.
        /// 인코딩 후 이 값을 초과하면 CRF를 올려 재인코딩을 시도합니다.
        /// </summary>
        public long TargetMaxSizeBytes { get; set; } = 0;

        /// <summary>
        /// BugBeaconSettings에서 VideoEncoderConfig를 생성합니다.
        /// </summary>
        public static VideoEncoderConfig FromSettings(BugBeaconSettings settings)
        {
            if (settings == null)
                throw new System.ArgumentNullException(nameof(settings));

            return new VideoEncoderConfig
            {
                Width = settings.videoWidth,
                Height = settings.videoHeight,
                Fps = settings.videoFps,
                BitrateMbps = settings.videoBitrateMbps,
                Crf = 23,
                TargetMaxSizeBytes = settings.cachedAttachmentSizeLimitBytes > 0
                    ? settings.cachedAttachmentSizeLimitBytes
                    : 0,
            };
        }

        public override string ToString()
        {
            return $"VideoEncoderConfig(W={Width}, H={Height}, FPS={Fps}, Bitrate={BitrateMbps}Mbps, CRF={Crf}, MaxSize={TargetMaxSizeBytes})";
        }
    }
}
