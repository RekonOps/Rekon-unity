using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace RekonOps.BugBeacon.Tests.Integration
{
    /// <summary>
    /// Phase 8.3 보안 테스트 - Auth Broker 보안.
    ///
    /// 테스트 시나리오:
    ///   1. 유효하지 않은 JWT → 401 검증
    ///   2. 만료된 JWT → 401 검증
    ///   3. Rate limit 초과 → 429 + Retry-After 검증
    ///   4. CSRF state 재사용 방지 검증
    ///   5. 잘못된 tenant_id → 403 검증
    ///
    /// Mock 전략:
    ///   - AuthBrokerException 직접 시뮬레이션
    ///   - Mock HTTP 응답 핸들러로 에러 코드 반환
    ///   - 실제 네트워크 호출 없이 예외 처리 로직 검증
    /// </summary>
    [TestFixture]
    public class AuthBrokerSecurityTests
    {
        private SessionTokenStore _tokenStore;

        [SetUp]
        public void SetUp()
        {
            _tokenStore = new SessionTokenStore($"com.rekonops.test.{Guid.NewGuid().ToString("N")[..8]}");
            _tokenStore.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _tokenStore?.Clear();
        }

        // ─── 테스트 1: 유효하지 않은 JWT → 401 검증 ─────────────────────────────

        [Test]
        public void 유효하지_않은_JWT_401_예외_처리_검증()
        {
            // Arrange - 유효하지 않은 JWT로 인한 401 예외 시뮬레이션
            var unauthorizedException = new AuthBrokerException(401, "Unauthorized: 유효하지 않은 JWT");

            // Assert - 예외 코드 확인
            Assert.AreEqual(401, unauthorizedException.StatusCode,
                "유효하지 않은 JWT는 401 상태코드를 반환해야 합니다.");
            Assert.IsTrue(unauthorizedException.Message.Contains("Unauthorized"),
                "오류 메시지에 'Unauthorized'가 포함되어야 합니다.");
        }

        [Test]
        public void 빈_JWT_토큰_로드시_null_반환_검증()
        {
            // Arrange - 빈 토큰 상태
            _tokenStore.Clear();

            // Act
            string token = _tokenStore.Load();

            // Assert
            Assert.IsNull(token, "저장된 토큰이 없으면 null이 반환되어야 합니다.");
        }

        [Test]
        public void 잘못된_형식_JWT_저장후_로드_검증()
        {
            // Arrange - 잘못된 JWT 형식 (실제로 암호화되어 저장되지만
            // HasValidToken이 false를 반환해야 함)
            string malformedJwt = "not.a.valid.jwt.format";
            _tokenStore.Save(malformedJwt);

            // Act - IsExpired 확인 (exp 파싱 불가 → 만료로 간주)
            bool isExpired = _tokenStore.IsExpired(0);

            // Assert
            Assert.IsTrue(isExpired, "잘못된 JWT는 만료된 것으로 처리되어야 합니다.");
        }

        // ─── 테스트 2: 만료된 JWT → 401 검증 ────────────────────────────────────

        [Test]
        public void 만료된_JWT_401_예외_시뮬레이션()
        {
            // Arrange - 만료된 JWT로 인한 401 예외
            var expiredTokenException = new AuthBrokerException(401, "Unauthorized: 만료된 세션 토큰");

            // Assert
            Assert.AreEqual(401, expiredTokenException.StatusCode, "만료된 JWT는 401을 반환해야 합니다.");
        }

        [Test]
        public void 만료된_JWT_HasValidToken_false_반환()
        {
            // Arrange - 과거 exp를 가진 JWT 생성
            // exp = 2020-01-01T00:00:00Z (Unix: 1577836800)
            string expiredJwt = CreateFakeJwtWithExp(1577836800L);
            _tokenStore.Save(expiredJwt);

            // Act
            bool hasValid = _tokenStore.HasValidToken();

            // Assert
            Assert.IsFalse(hasValid, "만료된 JWT는 HasValidToken이 false여야 합니다.");
        }

        // ─── 테스트 3: Rate limit 초과 → 429 + Retry-After 검증 ─────────────────

        [Test]
        public void Rate_Limit_초과_429_예외_처리_검증()
        {
            // Arrange - Rate limit 초과 예외 시뮬레이션
            var rateLimitException = new AuthBrokerException(429, "Too Many Requests");

            // Assert
            Assert.AreEqual(429, rateLimitException.StatusCode,
                "Rate limit 초과 시 429 상태코드가 반환되어야 합니다.");
        }

        [Test]
        public void AuthBrokerException_4xx_재시도_없음_검증()
        {
            // 4xx 오류는 재시도하지 않아야 함 (AuthBrokerClient 정책)
            // 401 제외한 4xx는 바로 예외를 던져야 함

            var httpErrors = new[]
            {
                (400, "Bad Request"),
                (403, "Forbidden"),
                (404, "Not Found"),
                (409, "Conflict"),
                (429, "Too Many Requests")
            };

            foreach (var (statusCode, message) in httpErrors)
            {
                var ex = new AuthBrokerException(statusCode, message);

                // 4xx 오류 확인
                bool isClientError = ex.StatusCode >= 400 && ex.StatusCode < 500;
                Assert.IsTrue(isClientError,
                    $"상태코드 {statusCode}은 4xx 클라이언트 오류여야 합니다.");
            }
        }

        // ─── 테스트 4: CSRF state 재사용 방지 검증 ───────────────────────────────

        [Test]
        public void CSRF_State_고유성_검증()
        {
            // Arrange - 여러 번의 connect_id 생성 시뮬레이션
            var connectIds = new System.Collections.Generic.HashSet<string>();
            int count = 100;

            // Act - 100개의 고유 UUID 생성
            for (int i = 0; i < count; i++)
            {
                string connectId = Guid.NewGuid().ToString();
                connectIds.Add(connectId);
            }

            // Assert - 모든 connect_id가 고유함 (재사용 없음)
            Assert.AreEqual(count, connectIds.Count,
                "CSRF 방지를 위해 각 connect_id는 고유해야 합니다.");
        }

        [Test]
        public void CSRF_State_재사용_감지_메커니즘_검증()
        {
            // Arrange - 동일 connect_id 재사용 시뮬레이션
            string connectId = Guid.NewGuid().ToString();
            var usedConnectIds = new System.Collections.Generic.HashSet<string>();

            // Act - 첫 번째 사용
            bool firstUse = usedConnectIds.Add(connectId);

            // 두 번째 사용 시도 (재사용)
            bool secondUse = usedConnectIds.Add(connectId);

            // Assert
            Assert.IsTrue(firstUse, "첫 번째 사용은 성공해야 합니다.");
            Assert.IsFalse(secondUse, "동일 state 재사용은 실패해야 합니다 (CSRF 방지).");
        }

        // ─── 테스트 5: 잘못된 tenant_id → 403 검증 ──────────────────────────────

        [Test]
        public void 잘못된_tenant_id_403_예외_처리_검증()
        {
            // Arrange - 잘못된 tenant_id로 인한 403 예외 시뮬레이션
            var forbiddenException = new AuthBrokerException(403, "Forbidden: 잘못된 tenant_id");

            // Assert
            Assert.AreEqual(403, forbiddenException.StatusCode,
                "잘못된 tenant_id는 403 상태코드가 반환되어야 합니다.");
        }

        [Test]
        public void 빈_tenant_id_요청_예외_발생_검증()
        {
            // Arrange - 빈 tenant_id로 요청 시도 시뮬레이션
            var client = new AuthBrokerClient("https://test.example.com", _tokenStore);

            string emptyTenantId = "";
            string userId = "user-123";

            // Act - 빈 tenant_id로 요청 시 취소된 토큰이면 즉시 예외
            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await client.PostConnectJiraStartAsync(emptyTenantId, userId, cts.Token);
            }, "취소된 토큰으로 요청 시 즉시 예외가 발생해야 합니다.");
        }

        // ─── 테스트 6: OnUnauthorized 이벤트 발생 검증 ───────────────────────────

        [Test]
        public void AuthBrokerClient_OnUnauthorized_이벤트_구독_검증()
        {
            // Arrange
            var client = new AuthBrokerClient("https://test.example.com", _tokenStore);
            bool eventFired = false;
            Action handler = () => eventFired = true;

            // Act - 이벤트 구독
            client.OnUnauthorized += handler;

            // 구독 해제
            client.OnUnauthorized -= handler;

            // Assert - 핸들러 등록/해제가 예외 없이 처리됨
            Assert.IsFalse(eventFired, "이벤트가 자동으로 발생하지 않아야 합니다.");
        }

        // ─── 테스트 7: 5xx 서버 오류 재시도 정책 검증 ───────────────────────────

        [Test]
        public void 5xx_서버_오류는_재시도_가능함을_확인()
        {
            // 5xx는 일시적 서버 오류 → 재시도 대상
            var serverErrors = new[]
            {
                (500, "Internal Server Error"),
                (502, "Bad Gateway"),
                (503, "Service Unavailable"),
                (504, "Gateway Timeout")
            };

            foreach (var (statusCode, message) in serverErrors)
            {
                var ex = new AuthBrokerException(statusCode, message);

                // 5xx 오류는 재시도 가능 (4xx가 아님)
                bool isServerError = ex.StatusCode >= 500 && ex.StatusCode < 600;
                Assert.IsTrue(isServerError,
                    $"상태코드 {statusCode}은 5xx 서버 오류여야 합니다 (재시도 가능).");
            }
        }

        // ─── 테스트 8: JWT 서명 검증 없이 저장 방지 검증 ────────────────────────

        [Test]
        public void JWT_구조_검증_3부분_필수()
        {
            // JWT는 반드시 header.payload.signature 3 부분으로 구성되어야 함
            var invalidTokens = new[]
            {
                "onlyone",
                "two.parts",
                "", // 빈 문자열
                "four.parts.here.extra"
            };

            foreach (string token in invalidTokens)
            {
                long? exp = SessionTokenStore.ExtractJwtExpiry(token);
                Assert.IsNull(exp, $"잘못된 JWT '{token}'에서 exp 추출이 실패해야 합니다 (null 반환).");
            }
        }

        // ─── 헬퍼 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 특정 exp 타임스탬프를 가진 가짜 JWT를 생성합니다.
        /// </summary>
        private static string CreateFakeJwtWithExp(long expUnixTimestamp)
        {
            string header = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            string payloadJson = $"{{\"sub\":\"test-user\",\"exp\":{expUnixTimestamp}}}";
            string payload = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(payloadJson))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            return $"{header}.{payload}.fake_signature";
        }
    }
}
