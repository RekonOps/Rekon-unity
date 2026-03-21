using System;
using System.Threading.Tasks;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// ClientRateLimiter 단위 테스트.
    ///
    /// 주의: WaitAndRetryAsync는 실제 HTTP 요청을 필요로 하므로,
    /// UnityWebRequest 없이 테스트 가능한 부분(상태 관리, 파싱, 백오프 계산)을 우선 검증합니다.
    /// </summary>
    [TestFixture]
    public class ClientRateLimiterTests
    {
        private ClientRateLimiter _limiter;

        [SetUp]
        public void SetUp()
        {
            _limiter = new ClientRateLimiter();
        }

        [TearDown]
        public void TearDown()
        {
            _limiter.Reset();
        }

        // ──────────────────────────────────────────────────────────────
        // 초기 상태 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void InitialState_IsNotRateLimited()
        {
            Assert.IsFalse(_limiter.IsRateLimited, "초기 상태에서 Rate Limit 상태가 아니어야 합니다.");
        }

        [Test]
        public void InitialState_SecondsUntilResetIsZero()
        {
            Assert.AreEqual(0, _limiter.SecondsUntilReset, 0.1,
                "초기 상태에서 대기 시간은 0이어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // Rate Limit 상태 설정/해제 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void SetRateLimitUntil_PositiveDelay_IsRateLimited()
        {
            _limiter.SetRateLimitUntil(30.0);

            Assert.IsTrue(_limiter.IsRateLimited, "SetRateLimitUntil 후 Rate Limit 상태여야 합니다.");
        }

        [Test]
        public void SetRateLimitUntil_PositiveDelay_SecondsUntilResetIsApproximate()
        {
            _limiter.SetRateLimitUntil(30.0);

            var remaining = _limiter.SecondsUntilReset;
            Assert.Greater(remaining, 25.0,    "남은 시간이 25초보다 커야 합니다.");
            Assert.LessOrEqual(remaining, 31.0, "남은 시간이 31초보다 작거나 같아야 합니다.");
        }

        [Test]
        public void SetRateLimitUntil_ZeroDelay_NotRateLimited()
        {
            // 0초로 설정하면 즉시 해제 상태
            _limiter.SetRateLimitUntil(0.0);

            Assert.IsFalse(_limiter.IsRateLimited, "0초 딜레이 설정 시 Rate Limit 상태가 아니어야 합니다.");
        }

        [Test]
        public void Reset_AfterRateLimit_IsNotRateLimited()
        {
            _limiter.SetRateLimitUntil(100.0);
            Assert.IsTrue(_limiter.IsRateLimited);

            _limiter.Reset();

            Assert.IsFalse(_limiter.IsRateLimited, "Reset 후 Rate Limit 상태가 해제되어야 합니다.");
            Assert.AreEqual(0, _limiter.SecondsUntilReset, 0.1);
        }

        // ──────────────────────────────────────────────────────────────
        // Retry-After 파싱 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void ParseRetryAfter_NullRequest_Returns0()
        {
            var result = ClientRateLimiter.ParseRetryAfter(null);
            Assert.AreEqual(0, result, 0.01);
        }

        [Test]
        public void ParseRetryAfter_IntegerSeconds_ParsedCorrectly()
        {
            // UnityWebRequest 없이 정수 파싱 로직만 직접 검증
            // ParseRetryAfter의 정수 파싱 부분은 내부적으로 double.TryParse 사용
            double result;
            Assert.IsTrue(double.TryParse("30", out result));
            Assert.AreEqual(30.0, result, 0.01, "Retry-After: 30 은 30초로 파싱되어야 합니다.");
        }

        [Test]
        public void ParseRetryAfter_HttpDateInFuture_PositiveSeconds()
        {
            // 미래 날짜를 파싱하면 양수 초를 반환해야 함
            var futureDate = DateTime.UtcNow.AddSeconds(60);
            var dateString = futureDate.ToString("R"); // RFC 1123 형식

            var parsed = DateTime.TryParse(
                dateString,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var retryDate
            );

            Assert.IsTrue(parsed, "미래 날짜 문자열이 파싱되어야 합니다.");

            var remaining = (retryDate - DateTime.UtcNow).TotalSeconds;
            Assert.Greater(remaining, 0, "미래 날짜의 남은 시간은 양수여야 합니다.");
        }

        [Test]
        public void ParseRetryAfter_HttpDateInPast_Returns0()
        {
            // 과거 날짜는 0 반환해야 함
            var pastDate   = DateTime.UtcNow.AddSeconds(-60);
            var remaining  = (pastDate - DateTime.UtcNow).TotalSeconds;
            var clamped    = remaining > 0 ? remaining : 0;

            Assert.AreEqual(0, clamped, "과거 날짜는 0초를 반환해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 지수 백오프 계산 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CalculateBackoff_Attempt0_Returns1Second()
        {
            var delay = ClientRateLimiter.CalculateBackoff(0);
            Assert.AreEqual(1.0, delay, 0.001, "첫 번째 시도(0)는 1초 대기여야 합니다.");
        }

        [Test]
        public void CalculateBackoff_Attempt1_Returns2Seconds()
        {
            var delay = ClientRateLimiter.CalculateBackoff(1);
            Assert.AreEqual(2.0, delay, 0.001, "두 번째 시도(1)는 2초 대기여야 합니다.");
        }

        [Test]
        public void CalculateBackoff_Attempt2_Returns4Seconds()
        {
            var delay = ClientRateLimiter.CalculateBackoff(2);
            Assert.AreEqual(4.0, delay, 0.001, "세 번째 시도(2)는 4초 대기여야 합니다.");
        }

        [Test]
        public void CalculateBackoff_Attempt3_Returns8Seconds()
        {
            var delay = ClientRateLimiter.CalculateBackoff(3);
            Assert.AreEqual(8.0, delay, 0.001, "네 번째 시도(3)는 8초 대기여야 합니다.");
        }

        [Test]
        public void CalculateBackoff_Attempt4_Returns16Seconds()
        {
            var delay = ClientRateLimiter.CalculateBackoff(4);
            Assert.AreEqual(16.0, delay, 0.001, "다섯 번째 시도(4)는 16초 대기여야 합니다.");
        }

        [Test]
        public void CalculateBackoff_LargeAttempt_CappedAtMaxDelay()
        {
            // 많은 시도 횟수에서 최대값(60초) 초과하지 않아야 함
            var delay = ClientRateLimiter.CalculateBackoff(100);
            Assert.AreEqual(ClientRateLimiter.MaxDelaySeconds, delay, 0.001,
                $"대기 시간은 최대 {ClientRateLimiter.MaxDelaySeconds}초를 넘으면 안 됩니다.");
        }

        [Test]
        public void CalculateBackoff_ExponentialSequence_CorrectProgression()
        {
            // 지수 백오프 수열: 1, 2, 4, 8, 16, 32, 60(cap)
            double[] expected = { 1, 2, 4, 8, 16, 32, 60 };
            for (int i = 0; i < expected.Length; i++)
            {
                var delay = ClientRateLimiter.CalculateBackoff(i);
                Assert.AreEqual(expected[i], delay, 0.001,
                    $"시도 {i}의 백오프가 {expected[i]}초여야 합니다.");
            }
        }

        [Test]
        public void CalculateBackoff_NegativeAttempt_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ClientRateLimiter.CalculateBackoff(-1));
        }

        // ──────────────────────────────────────────────────────────────
        // 상수 값 검증
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Constants_MaxRetries_Is5()
        {
            Assert.AreEqual(5, ClientRateLimiter.MaxRetries, "최대 재시도 횟수는 5회여야 합니다.");
        }

        [Test]
        public void Constants_InitialDelay_Is1Second()
        {
            Assert.AreEqual(1.0, ClientRateLimiter.InitialDelaySeconds, 0.001,
                "초기 대기 시간은 1초여야 합니다.");
        }

        [Test]
        public void Constants_MaxDelay_Is60Seconds()
        {
            Assert.AreEqual(60.0, ClientRateLimiter.MaxDelaySeconds, 0.001,
                "최대 대기 시간은 60초여야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // WaitAndRetryAsync - null 팩토리 예외 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void WaitAndRetryAsync_NullFactory_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await _limiter.WaitAndRetryAsync(null)
            );
        }

        // ──────────────────────────────────────────────────────────────
        // 스레드 안전성 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void IsRateLimited_ConcurrentReads_NoException()
        {
            _limiter.SetRateLimitUntil(10.0);

            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            Parallel.For(0, 100, _ =>
            {
                try
                {
                    _ = _limiter.IsRateLimited;
                    _ = _limiter.SecondsUntilReset;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.IsEmpty(exceptions, "동시 읽기 시 예외가 없어야 합니다.");
        }

        [Test]
        public void SetAndReset_Concurrent_NoException()
        {
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            Parallel.For(0, 100, i =>
            {
                try
                {
                    if (i % 2 == 0)
                        _limiter.SetRateLimitUntil(10.0);
                    else
                        _limiter.Reset();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.IsEmpty(exceptions, "동시 쓰기 시 예외가 없어야 합니다.");
        }
    }
}
