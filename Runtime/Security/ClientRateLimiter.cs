using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RekonOps.BugBeacon
{
    /// <summary>
    /// HTTP 429(Too Many Requests) 응답을 감지하고 지수 백오프로 재시도하는 클라이언트 측 Rate Limiter.
    ///
    /// 동작 방식:
    ///   1. 요청 전 IsRateLimited 확인
    ///   2. 429 응답 수신 시 Retry-After 헤더 파싱 (없으면 지수 백오프)
    ///   3. 대기 후 재시도 (최대 MaxRetries회)
    ///   4. 재시도 초과 시 마지막 응답 반환
    ///
    /// 지수 백오프: 1초 → 2초 → 4초 → 8초 → 16초 (최대 60초)
    /// </summary>
    public class ClientRateLimiter
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        /// <summary>최대 재시도 횟수</summary>
        public const int MaxRetries = 5;

        /// <summary>지수 백오프 초기 대기 시간 (초)</summary>
        public const double InitialDelaySeconds = 1.0;

        /// <summary>지수 백오프 최대 대기 시간 (초)</summary>
        public const double MaxDelaySeconds = 60.0;

        /// <summary>HTTP Rate Limit 상태 코드</summary>
        private const long HttpStatusTooManyRequests = 429;

        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        /// <summary>Rate Limit 해제 예정 시각 (UTC)</summary>
        private DateTime _rateLimitUntil = DateTime.MinValue;

        private readonly object _stateLock = new object();

        // ──────────────────────────────────────────────────────────────
        // 공개 프로퍼티
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 현재 Rate Limit 상태 여부.
        /// true인 경우 요청 전 대기가 필요합니다.
        /// </summary>
        public bool IsRateLimited
        {
            get
            {
                lock (_stateLock)
                    return DateTime.UtcNow < _rateLimitUntil;
            }
        }

        /// <summary>
        /// Rate Limit 해제까지 남은 시간 (초). 제한 중이 아니면 0.
        /// </summary>
        public double SecondsUntilReset
        {
            get
            {
                lock (_stateLock)
                {
                    var remaining = (_rateLimitUntil - DateTime.UtcNow).TotalSeconds;
                    return remaining > 0 ? remaining : 0;
                }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 핵심 공개 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 지수 백오프를 적용하여 요청을 재시도하는 래퍼 메서드.
        /// 429 응답 시 Retry-After 또는 지수 백오프 후 재시도합니다.
        /// </summary>
        /// <param name="requestFactory">매 시도마다 새로운 UnityWebRequest를 생성하는 팩토리 함수</param>
        /// <returns>최종 응답의 UnityWebRequest (호출자가 Dispose 책임)</returns>
        public async Task<UnityWebRequest> WaitAndRetryAsync(Func<Task<UnityWebRequest>> requestFactory)
        {
            if (requestFactory == null)
                throw new ArgumentNullException(nameof(requestFactory));

            int attempt = 0;
            UnityWebRequest lastResponse = null;

            while (attempt <= MaxRetries)
            {
                // 현재 Rate Limit 상태라면 해제될 때까지 대기
                if (IsRateLimited)
                {
                    var waitSec = SecondsUntilReset;
                    Debug.Log($"[ClientRateLimiter] Rate Limit 상태. {waitSec:F1}초 대기 후 요청합니다.");
                    await Task.Delay(TimeSpan.FromSeconds(waitSec));
                }

                // 요청 실행
                lastResponse = await requestFactory();

                // 429가 아니면 성공으로 반환
                if (lastResponse.responseCode != HttpStatusTooManyRequests)
                    return lastResponse;

                // 429 감지: Rate Limit 설정
                var retryAfterSeconds = ParseRetryAfter(lastResponse);
                var backoffSeconds    = CalculateBackoff(attempt);
                var delaySeconds      = retryAfterSeconds > 0 ? retryAfterSeconds : backoffSeconds;

                SetRateLimitUntil(delaySeconds);

                Debug.LogWarning(
                    $"[ClientRateLimiter] 429 응답 수신. " +
                    $"시도 {attempt + 1}/{MaxRetries}, " +
                    $"{delaySeconds:F1}초 후 재시도합니다."
                );

                attempt++;

                if (attempt > MaxRetries)
                {
                    Debug.LogError($"[ClientRateLimiter] 최대 재시도 횟수({MaxRetries})를 초과했습니다.");
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            return lastResponse;
        }

        /// <summary>
        /// 외부에서 Rate Limit 상태를 수동으로 설정합니다.
        /// HTTP 응답 외부에서 Rate Limit를 감지한 경우 사용합니다.
        /// </summary>
        /// <param name="delaySeconds">제한 지속 시간 (초)</param>
        public void SetRateLimitUntil(double delaySeconds)
        {
            lock (_stateLock)
            {
                _rateLimitUntil = DateTime.UtcNow.AddSeconds(delaySeconds);
            }
        }

        /// <summary>
        /// Rate Limit 상태를 즉시 해제합니다.
        /// </summary>
        public void Reset()
        {
            lock (_stateLock)
            {
                _rateLimitUntil = DateTime.MinValue;
            }
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 헬퍼 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Retry-After 헤더를 파싱하여 대기 시간(초)을 반환합니다.
        /// 파싱 실패 시 0 반환.
        ///
        /// 지원 형식:
        ///   - 정수(초): "Retry-After: 30"
        ///   - HTTP 날짜: "Retry-After: Wed, 21 Oct 2026 07:28:00 GMT"
        /// </summary>
        /// <param name="request">429 응답이 담긴 UnityWebRequest</param>
        /// <returns>대기 시간(초), 파싱 실패 시 0</returns>
        public static double ParseRetryAfter(UnityWebRequest request)
        {
            if (request == null)
                return 0;

            var headerValue = request.GetResponseHeader("Retry-After");
            if (string.IsNullOrEmpty(headerValue))
                return 0;

            // 형식 1: 초 단위 정수
            if (double.TryParse(headerValue, out var seconds) && seconds >= 0)
                return seconds;

            // 형식 2: HTTP 날짜 형식 (RFC 1123)
            if (DateTime.TryParse(
                    headerValue,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var retryDate))
            {
                var remaining = (retryDate - DateTime.UtcNow).TotalSeconds;
                return remaining > 0 ? remaining : 0;
            }

            Debug.LogWarning($"[ClientRateLimiter] Retry-After 헤더를 파싱할 수 없습니다: '{headerValue}'");
            return 0;
        }

        /// <summary>
        /// 재시도 횟수 기반 지수 백오프 대기 시간을 계산합니다.
        /// 공식: min(InitialDelay * 2^attempt, MaxDelay)
        /// </summary>
        /// <param name="attempt">현재 재시도 횟수 (0-based)</param>
        /// <returns>대기 시간 (초)</returns>
        public static double CalculateBackoff(int attempt)
        {
            if (attempt < 0)
                throw new ArgumentOutOfRangeException(nameof(attempt), "재시도 횟수는 0 이상이어야 합니다.");

            var delay = InitialDelaySeconds * Math.Pow(2, attempt);
            return Math.Min(delay, MaxDelaySeconds);
        }
    }
}
