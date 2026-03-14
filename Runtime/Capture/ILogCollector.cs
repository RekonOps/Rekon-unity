namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// 로그 수집기 인터페이스.
    /// 버퍼에 저장된 로그를 시간순으로 반환하는 계약을 정의합니다.
    /// </summary>
    public interface ILogCollector
    {
        /// <summary>
        /// 현재 버퍼에 저장된 모든 로그를 시간 오름차순으로 반환합니다.
        /// </summary>
        /// <returns>시간순 정렬된 LogEntry 배열 (복사본)</returns>
        LogEntry[] GetEntries();

        /// <summary>
        /// 현재 버퍼에 저장된 로그 항목 수를 반환합니다.
        /// </summary>
        int Count { get; }
    }
}
