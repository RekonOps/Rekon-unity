using System;
using System.Threading.Tasks;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 캡처 파이프라인 오케스트레이터 인터페이스.
    /// 스크린샷, 로그, 상태, 영상을 병렬로 수집하고 CaptureResult를 반환합니다.
    /// </summary>
    public interface ICaptureOrchestrator
    {
        /// <summary>
        /// 캡처 진행 상황을 알리는 이벤트.
        /// 각 서브시스템 완료 시 발행됩니다.
        /// </summary>
        event Action<CaptureProgressEvent> OnProgress;

        /// <summary>
        /// 캡처가 완전히 완료되었을 때 발행되는 이벤트.
        /// CaptureResult를 인자로 전달합니다.
        /// </summary>
        event Action<CaptureResult> OnCaptureCompleted;

        /// <summary>
        /// 모든 서브시스템에서 병렬로 데이터를 수집하고 결과를 반환합니다.
        /// 내부적으로 5초 타임아웃이 적용됩니다.
        /// </summary>
        /// <returns>수집된 아티팩트 경로를 담은 CaptureResult</returns>
        Task<CaptureResult> StartAsync();
    }
}
