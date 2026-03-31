namespace RekonOps.Rekon
{
    /// <summary>
    /// 단일 비디오 프레임의 픽셀 데이터와 메타 정보를 담는 구조체.
    /// FrameRingBuffer에 저장되고 VideoEncoder로 전달됩니다.
    ///
    /// 사전 할당 주의:
    ///   Data는 FrameRingBuffer가 사전 할당한 슬롯 배열을 가리킵니다.
    ///   슬롯 배열은 링버퍼가 소유하며 재사용되므로 외부에서 반환하거나 수정하지 마세요.
    ///   실제 유효 픽셀 바이트 수는 Data.Length가 아닌 DataLength를 사용해야 합니다.
    ///   Mp4VideoEncoder는 frame.Data를 stdin에 쓸 때 반드시 DataLength만큼만 write해야 합니다.
    /// </summary>
    public readonly struct FrameData
    {
        /// <summary>RGBA32 형식의 픽셀 데이터 (행 단위, 위에서 아래 순서).
        /// 링버퍼의 사전 할당 슬롯이므로 Data.Length 대신 DataLength를 사용하세요.
        /// 외부에서 반환하거나 수정하지 마세요.</summary>
        public readonly byte[] Data;

        /// <summary>Data 배열에서 실제 유효한 픽셀 데이터의 바이트 수.
        /// 슬롯 크기(Data.Length)와 다를 수 있으므로 항상 이 값을 사용해야 합니다.</summary>
        public readonly int DataLength;

        /// <summary>프레임 너비(픽셀)</summary>
        public readonly int Width;

        /// <summary>프레임 높이(픽셀)</summary>
        public readonly int Height;

        /// <summary>프레임 캡처 시각 (Time.unscaledTimeAsDouble)</summary>
        public readonly double Timestamp;

        /// <summary>
        /// FrameData를 초기화합니다 (DataLength = data.Length).
        /// 일반 byte[] 할당 시 사용합니다.
        /// </summary>
        /// <param name="data">RGBA32 픽셀 바이트 배열</param>
        /// <param name="width">프레임 너비</param>
        /// <param name="height">프레임 높이</param>
        /// <param name="timestamp">캡처 시각 (Time.unscaledTimeAsDouble)</param>
        public FrameData(byte[] data, int width, int height, double timestamp)
        {
            Data = data;
            DataLength = data?.Length ?? 0;
            Width = width;
            Height = height;
            Timestamp = timestamp;
        }

        /// <summary>
        /// FrameData를 초기화합니다 (실제 유효 길이 별도 지정).
        /// 사전 할당 슬롯은 frameSize 이상일 수 있으므로
        /// 실제 유효 길이를 dataLength로 별도 지정합니다.
        /// </summary>
        /// <param name="data">RGBA32 픽셀 바이트 배열 (링버퍼 사전 할당 슬롯)</param>
        /// <param name="dataLength">실제 유효 픽셀 데이터 바이트 수</param>
        /// <param name="width">프레임 너비</param>
        /// <param name="height">프레임 높이</param>
        /// <param name="timestamp">캡처 시각 (Time.unscaledTimeAsDouble)</param>
        public FrameData(byte[] data, int dataLength, int width, int height, double timestamp)
        {
            Data = data;
            DataLength = dataLength;
            Width = width;
            Height = height;
            Timestamp = timestamp;
        }

        /// <summary>
        /// 유효한 프레임인지 확인합니다 (데이터 있고 크기 양수)
        /// </summary>
        public bool IsValid => Data != null && DataLength > 0 && Width > 0 && Height > 0;

        public override string ToString()
        {
            return $"FrameData(W={Width}, H={Height}, T={Timestamp:F3}s, {DataLength:N0}bytes)";
        }
    }
}
