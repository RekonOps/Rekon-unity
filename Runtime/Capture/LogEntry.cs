using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 단일 로그 항목을 나타내는 불변 구조체.
    /// Application.logMessageReceived 콜백에서 생성됩니다.
    /// </summary>
    public readonly struct LogEntry
    {
        /// <summary>로그가 기록된 시각 (Application.logMessageReceived 호출 시점의 Time.realtimeSinceStartup)</summary>
        public readonly double Timestamp;

        /// <summary>로그 유형 (Log, Warning, Error, Exception, Assert)</summary>
        public readonly LogType LogType;

        /// <summary>로그 메시지 본문</summary>
        public readonly string Message;

        /// <summary>스택 트레이스 (Error/Exception 시 포함, 일반 로그는 빈 문자열)</summary>
        public readonly string StackTrace;

        /// <summary>
        /// LogEntry 구조체를 초기화합니다.
        /// </summary>
        /// <param name="timestamp">Time.realtimeSinceStartup 기준 타임스탬프</param>
        /// <param name="logType">Unity 로그 유형</param>
        /// <param name="message">로그 메시지</param>
        /// <param name="stackTrace">스택 트레이스 문자열</param>
        public LogEntry(double timestamp, LogType logType, string message, string stackTrace)
        {
            Timestamp = timestamp;
            LogType = logType;
            Message = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
        }

        /// <summary>
        /// 사람이 읽기 좋은 형태로 변환합니다.
        /// </summary>
        public override string ToString()
        {
            return $"[{Timestamp:F3}] [{LogType}] {Message}";
        }
    }
}
