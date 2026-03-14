namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 프레임 캡처 인터페이스.
    /// GPU 읽기 전략 (AsyncGPUReadback / ReadPixels)을 추상화합니다.
    /// </summary>
    public interface IFrameCapturer
    {
        /// <summary>
        /// 프레임 캡처를 시작합니다.
        /// Update마다 설정된 FPS에 맞춰 프레임을 FrameRingBuffer에 추가합니다.
        /// </summary>
        void StartCapturing();

        /// <summary>
        /// 프레임 캡처를 중지합니다.
        /// </summary>
        void StopCapturing();

        /// <summary>
        /// 현재 캡처가 진행 중인지 여부
        /// </summary>
        bool IsCapturing { get; }
    }
}
