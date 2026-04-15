using System;
using System.Collections.Generic;

namespace RekonOps.Rekon
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
        /// <remarks>
        /// ScreenshotEntries(메모리 큐)로 전환 중입니다.
        /// 신규 코드에서는 ScreenshotEntries를 사용하세요.
        /// </remarks>
        [Obsolete("ScreenshotEntries 프로퍼티를 사용하세요. 이 필드는 추후 제거됩니다.")]
        public string ScreenshotPath { get; set; }

        /// <summary>
        /// 스크린샷 핫키로 캡처된 PNG 항목 배열 (메모리 큐 방식).
        /// 캡처된 항목이 없을 경우 null 또는 빈 배열.
        /// </summary>
        public ScreenshotEntry[] ScreenshotEntries { get; set; }

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
        /// 영상 녹화 구간 동안 수집된 성능 타임라인 데이터.
        /// 영상 캡처가 비활성이거나 수집 실패 시 null.
        /// </summary>
        public PerformanceTimeline PerformanceTimeline { get; set; }

        /// <summary>
        /// R2에 업로드된 파일들의 공개 URL 목록.
        /// 키: 파일 유형(screenshot, log, video), 값: R2 공개 URL.
        /// R2 업로드가 수행되지 않은 경우 null 또는 빈 사전.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> R2Urls { get; set; }

        /// <summary>
        /// R2 업로드를 통해 생성된 리포트 ID.
        /// 웹 저장이 수행되지 않은 경우 null.
        /// </summary>
        public string WebReportId { get; set; }

        /// <summary>
        /// 캡처가 완료된 UTC 시각.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 적어도 하나의 아티팩트가 성공적으로 수집되었는지 여부.
        /// ScreenshotEntries(메모리 큐) 또는 레거시 ScreenshotPath 중 하나라도 존재하면 충족됩니다.
        /// </summary>
#pragma warning disable CS0618 // Obsolete 멤버 사용 (하위 호환 유지)
        public bool IsPartialSuccess =>
            (ScreenshotEntries != null && ScreenshotEntries.Length > 0) ||
            !string.IsNullOrEmpty(ScreenshotPath) ||
            !string.IsNullOrEmpty(LogsPath) ||
            !string.IsNullOrEmpty(StatePath);
#pragma warning restore CS0618

        /// <summary>
        /// 모든 기본 아티팩트가 성공적으로 수집되었는지 여부.
        /// ScreenshotEntries(메모리 큐) 또는 레거시 ScreenshotPath 중 하나라도 존재하면 스크린샷 조건을 충족합니다.
        /// </summary>
#pragma warning disable CS0618 // Obsolete 멤버 사용 (하위 호환 유지)
        public bool IsFullSuccess =>
            ((ScreenshotEntries != null && ScreenshotEntries.Length > 0) ||
             !string.IsNullOrEmpty(ScreenshotPath)) &&
            !string.IsNullOrEmpty(LogsPath) &&
            !string.IsNullOrEmpty(StatePath);
#pragma warning restore CS0618

        public override string ToString()
        {
            return $"CaptureResult(t={Timestamp:O}, screenshot={ScreenshotPath}, " +
                   $"logs={LogsPath}, state={StatePath}, video={VideoPath})";
        }
    }
}
