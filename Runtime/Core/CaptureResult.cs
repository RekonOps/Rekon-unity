using System;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 캡처 파이프라인 실행 결과를 담는 데이터 클래스.
    /// CaptureOrchestrator.StartAsync()가 반환합니다.
    /// </summary>
    public class CaptureResult
    {
        /// <summary>
        /// 저장된 스크린샷 파일 경로 (PNG).
        /// 캡처 실패 시 null 또는 빈 문자열.
        /// </summary>
        public string ScreenshotPath { get; set; }

        /// <summary>
        /// 저장된 로그 ZIP 파일 경로.
        /// 캡처 실패 시 null 또는 빈 문자열.
        /// </summary>
        public string LogsPath { get; set; }

        /// <summary>
        /// 저장된 상태 스냅샷 JSON 파일 경로.
        /// 캡처 실패 시 null 또는 빈 문자열.
        /// </summary>
        public string StatePath { get; set; }

        /// <summary>
        /// 저장된 영상 세그먼트 디렉토리 경로 (Raw 프레임 시퀀스).
        /// 영상 캡처 비활성 또는 실패 시 null 또는 빈 문자열.
        /// </summary>
        public string VideoPath { get; set; }

        /// <summary>
        /// 캡처 시점에 수집된 상태 스냅샷.
        /// ManifestGenerator에서 환경 정보(platform, device, os, scene 등)를 추출하는 데 사용됩니다.
        /// </summary>
        public StateSnapshot StateSnapshot { get; set; }

        /// <summary>
        /// 캡처가 완료된 UTC 시각.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 적어도 하나의 아티팩트가 성공적으로 수집되었는지 여부.
        /// </summary>
        public bool IsPartialSuccess =>
            !string.IsNullOrEmpty(ScreenshotPath) ||
            !string.IsNullOrEmpty(LogsPath) ||
            !string.IsNullOrEmpty(StatePath);

        /// <summary>
        /// 모든 기본 아티팩트가 성공적으로 수집되었는지 여부.
        /// </summary>
        public bool IsFullSuccess =>
            !string.IsNullOrEmpty(ScreenshotPath) &&
            !string.IsNullOrEmpty(LogsPath) &&
            !string.IsNullOrEmpty(StatePath);

        public override string ToString()
        {
            return $"CaptureResult(t={Timestamp:O}, screenshot={ScreenshotPath}, " +
                   $"logs={LogsPath}, state={StatePath}, video={VideoPath})";
        }
    }
}
