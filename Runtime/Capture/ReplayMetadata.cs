using System;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// team_pro 플랜 전용 리플레이 메타데이터.
    /// 영상·로그·성능 타임라인 3축 동기 재생에 필요한 시간 보정 정보를 담습니다.
    ///
    /// 시간축:
    ///   - 로그(LogEntry.Timestamp) = Time.realtimeSinceStartup 기준
    ///   - 영상 프레임(FrameData.Timestamp) = Time.unscaledTime 기준
    ///   - clock_offset = realtime − unscaled → 두 시계 보정값
    ///
    /// 모든 시간 필드는 double (float 다운캐스팅 금지 — 로그가 double이므로 정밀도 보존).
    ///
    /// 필드명 규칙:
    ///   PerformanceSample 패턴과 동일하게 public 필드명을 snake_case로 선언합니다.
    ///   JsonUtility.ToJson(this) 결과가 그대로 snake_case JSON이 되어
    ///   Backend(reports.replay_metadata JSONB) / Web 변환공식과 정합합니다.
    /// </summary>
    [Serializable]
    public class ReplayMetadata
    {
        // ── 영상 구간 ────────────────────────────────────────────────────────

        /// <summary>
        /// 영상 ring buffer 첫 프레임 timestamp 실측값 (Time.unscaledTime 축).
        /// FrameRingBuffer.GetFrames()[0].Timestamp 값.
        /// </summary>
        public double video_start_t_abs;

        /// <summary>
        /// 실제 영상 길이 (초). frames[last].Timestamp − frames[0].Timestamp.
        /// 즉시 캡처 edge case 시 0.0 가능.
        /// </summary>
        public double video_duration_s;

        // ── 캡처 시점 ────────────────────────────────────────────────────────

        /// <summary>
        /// 캡처 트리거 시점의 Time.realtimeSinceStartup 값 (realtime 축).
        /// </summary>
        public double capture_trigger_t_abs;

        /// <summary>
        /// Play Mode 시작 시각 (보통 0.0).
        /// </summary>
        public double play_mode_start_t_abs;

        // ── 시간축 보정값 ─────────────────────────────────────────────────────

        /// <summary>
        /// 캡처 시점 1회 측정한 시계 오프셋.
        /// clock_offset = Time.realtimeSinceStartupAsDouble − Time.unscaledTimeAsDouble
        ///
        /// 로그(realtime 축) → 영상(unscaled 축) 환산:
        ///   logToVideoTime = (logTAbs − clock_offset) − video_start_t_abs
        ///   videoTimeToLogAbs = video_start_t_abs + videoCurrentTime + clock_offset
        /// </summary>
        public double clock_offset;

        // ── 로그 통계 ─────────────────────────────────────────────────────────

        /// <summary>수집된 전체 로그 수</summary>
        public int log_count_total;

        /// <summary>영상 구간 내 로그 수 (video_start_t_abs ~ video_start_t_abs + video_duration_s)</summary>
        public int log_count_in_video;

        /// <summary>영상 시작 이전 로그 수</summary>
        public int log_count_before_video;

        // ── 스키마 버전 ───────────────────────────────────────────────────────

        /// <summary>
        /// 향후 호환성 버전 번호.
        /// 현재: 2 (clock_offset 필드 추가된 버전).
        /// </summary>
        public int schema_version;

        /// <summary>
        /// JSON 문자열로 직렬화합니다 (JsonUtility 호환, snake_case 출력).
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        public override string ToString()
        {
            return $"ReplayMetadata(video_start={video_start_t_abs:F3}, duration={video_duration_s:F1}s, " +
                   $"log_total={log_count_total}, in_video={log_count_in_video}, schema={schema_version})";
        }
    }
}
