namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 단일 비디오 프레임의 픽셀 데이터와 메타 정보를 담는 구조체.
    /// FrameRingBuffer에 저장되고 VideoEncoder로 전달됩니다.
    /// </summary>
    public readonly struct FrameData
    {
        /// <summary>RGBA32 형식의 픽셀 데이터 (행 단위, 위에서 아래 순서)</summary>
        public readonly byte[] Data;

        /// <summary>프레임 너비(픽셀)</summary>
        public readonly int Width;

        /// <summary>프레임 높이(픽셀)</summary>
        public readonly int Height;

        /// <summary>프레임 캡처 시각 (Time.unscaledTimeAsDouble)</summary>
        public readonly double Timestamp;

        /// <summary>
        /// FrameData를 초기화합니다.
        /// </summary>
        /// <param name="data">RGBA32 픽셀 바이트 배열</param>
        /// <param name="width">프레임 너비</param>
        /// <param name="height">프레임 높이</param>
        /// <param name="timestamp">캡처 시각 (Time.unscaledTimeAsDouble)</param>
        public FrameData(byte[] data, int width, int height, double timestamp)
        {
            Data = data;
            Width = width;
            Height = height;
            Timestamp = timestamp;
        }

        /// <summary>
        /// 유효한 프레임인지 확인합니다 (데이터 있고 크기 양수)
        /// </summary>
        public bool IsValid => Data != null && Data.Length > 0 && Width > 0 && Height > 0;

        public override string ToString()
        {
            return $"FrameData(W={Width}, H={Height}, T={Timestamp:F3}s, {(Data?.Length ?? 0):N0}bytes)";
        }
    }
}
