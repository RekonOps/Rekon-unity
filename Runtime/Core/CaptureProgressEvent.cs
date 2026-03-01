namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 캡처 파이프라인 진행 상황을 나타내는 이벤트 데이터.
    /// CaptureOrchestrator가 각 단계 완료 시 발행합니다.
    /// </summary>
    public class CaptureProgressEvent
    {
        /// <summary>
        /// 현재 진행 중인 단계 이름.
        /// 값 목록: "screenshot", "logs", "state", "video", "complete"
        /// </summary>
        public string Stage { get; set; }

        /// <summary>
        /// 전체 진행률 (0.0 ~ 1.0).
        ///   0.0 = 시작
        ///   0.25 = 스크린샷 완료
        ///   0.50 = 로그 완료
        ///   0.75 = 상태 완료
        ///   1.0  = 전체 완료
        /// </summary>
        public float Progress { get; set; }

        /// <summary>
        /// 단계별 오류 메시지 (성공 시 null)
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 해당 단계가 성공적으로 완료되었는지 여부
        /// </summary>
        public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

        public CaptureProgressEvent(string stage, float progress, string errorMessage = null)
        {
            Stage = stage;
            Progress = progress;
            ErrorMessage = errorMessage;
        }

        public override string ToString()
        {
            string status = IsSuccess ? "OK" : $"Error: {ErrorMessage}";
            return $"CaptureProgress(stage={Stage}, {Progress:P0}, {status})";
        }
    }
}
