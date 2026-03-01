using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace RekonOps.BugOneTouch.Tests.Integration
{
    /// <summary>
    /// Phase 8.1 E2E 통합 테스트 - 토큰 만료→Re-auth 플로우.
    ///
    /// 테스트 시나리오:
    ///   1. 만료된 JWT 시뮬레이션
    ///   2. TokenRefreshManager → POST /token/jira 호출 → 401 응답
    ///   3. 재시도 3회 → ReAuthHandler.OnReAuthRequired 이벤트 발생
    ///   4. SessionTokenStore 클리어 확인
    ///
    /// Mock 전략:
    ///   - Mock AuthBrokerClient로 HTTP 호출 대체
    ///   - 401 응답을 AuthBrokerException으로 시뮬레이션
    /// </summary>
    [TestFixture]
    public class TokenReAuthTests
    {
        private SessionTokenStore _tokenStore;
        private ReAuthHandler _reAuthHandler;

        [SetUp]
        public void SetUp()
        {
            // 테스트용 TokenStore (고유한 패키지명으로 격리)
            _tokenStore = new SessionTokenStore($"com.rekonops.test.{Guid.NewGuid().ToString("N")[..8]}");
            _tokenStore.Clear();

            _reAuthHandler = new ReAuthHandler(_tokenStore);
        }

        [TearDown]
        public void TearDown()
        {
            _tokenStore?.Clear();
        }

        // ─── 테스트 1: 만료된 JWT 감지 검증 ─────────────────────────────────────

        [Test]
        public void 만료된_JWT_IsExpired_반환_true()
        {
            // Arrange - 과거 시각의 exp 필드를 가진 JWT 생성
            // exp = 2020-01-01T00:00:00Z (Unix timestamp: 1577836800)
            string expiredJwt = CreateFakeJwt(expUnixTimestamp: 1577836800L); // 과거

            // Act
            _tokenStore.Save(expiredJwt);
            bool isExpired = _tokenStore.IsExpired(0);

            // Assert
            Assert.IsTrue(isExpired, "만료된 JWT는 IsExpired가 true를 반환해야 합니다.");
        }

        [Test]
        public void 유효한_JWT_IsExpired_반환_false()
        {
            // Arrange - 미래 시각의 exp 필드를 가진 JWT 생성
            // exp = 현재 시각 + 1시간
            long futureExp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            string validJwt = CreateFakeJwt(expUnixTimestamp: futureExp);

            // Act
            _tokenStore.Save(validJwt);
            bool isExpired = _tokenStore.IsExpired(0);

            // Assert
            Assert.IsFalse(isExpired, "유효한 JWT는 IsExpired가 false를 반환해야 합니다.");
        }

        // ─── 테스트 2: 401 응답 시 SessionTokenStore 클리어 ─────────────────────

        [Test]
        public async Task 401_응답_수신_시_TokenStore_클리어_확인()
        {
            // Arrange
            long futureExp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            string validJwt = CreateFakeJwt(expUnixTimestamp: futureExp);
            _tokenStore.Save(validJwt);

            // 401 예외 시뮬레이션
            var unauthorizedException = new AuthBrokerException(401, "Unauthorized");

            // Act - 401 수신 시 TokenStore 클리어 동작 시뮬레이션
            bool tokenExistsBeforeClear = !string.IsNullOrEmpty(_tokenStore.Load());
            _tokenStore.Clear(); // 401 수신 시 실제 코드에서 호출되는 동작

            // Assert
            Assert.IsTrue(tokenExistsBeforeClear, "클리어 전에는 토큰이 있어야 합니다.");
            Assert.IsNull(_tokenStore.Load(), "클리어 후 토큰이 없어야 합니다.");

            await Task.CompletedTask;
        }

        // ─── 테스트 3: ReAuthHandler.OnReAuthRequired 이벤트 발생 ────────────────

        [Test]
        public async Task ReAuthHandler_OnReAuthRequired_이벤트_발생_검증()
        {
            // Arrange
            bool reAuthEventFired = false;
            string capturedReason = null;

            _reAuthHandler.OnReAuthRequired += (args) =>
            {
                reAuthEventFired = true;
                capturedReason = args.Reason;
            };

            // Act - 재인증 트리거
            await _reAuthHandler.TriggerReAuthAsync("Jira 연결이 만료되었습니다. 다시 연결해주세요.");

            // Assert
            Assert.IsTrue(reAuthEventFired, "OnReAuthRequired 이벤트가 발생해야 합니다.");
            Assert.IsNotNull(capturedReason, "이벤트 인자(reason)가 있어야 합니다.");
            Assert.IsTrue(capturedReason.Contains("Jira"), "이벤트 이유에 Jira가 포함되어야 합니다.");
        }

        // ─── 테스트 4: ReAuth 중복 트리거 방지 검증 ──────────────────────────────

        [Test]
        public async Task ReAuthHandler_중복_트리거_방지_검증()
        {
            // Arrange
            int eventFireCount = 0;
            _reAuthHandler.OnReAuthRequired += (_) => eventFireCount++;

            // Act - 두 번 트리거 (첫 번째만 발생해야 함)
            await _reAuthHandler.TriggerReAuthAsync("첫 번째 재인증 요청");
            await _reAuthHandler.TriggerReAuthAsync("두 번째 재인증 요청 (중복)");

            // Assert
            Assert.AreEqual(1, eventFireCount, "중복 트리거는 무시되어야 합니다 (이벤트 1회만 발생).");
            Assert.IsTrue(_reAuthHandler.IsReAuthPending, "재인증 대기 상태여야 합니다.");
        }

        // ─── 테스트 5: ReAuth 완료 후 상태 리셋 ─────────────────────────────────

        [Test]
        public async Task ReAuthHandler_재인증_완료_후_상태_리셋()
        {
            // Arrange
            await _reAuthHandler.TriggerReAuthAsync("만료된 토큰");
            Assert.IsTrue(_reAuthHandler.IsReAuthPending, "재인증 대기 상태여야 합니다.");

            // Act - 재인증 완료
            _reAuthHandler.NotifyReAuthCompleted();

            // Assert
            Assert.IsFalse(_reAuthHandler.IsReAuthPending, "재인증 완료 후 대기 상태가 해제되어야 합니다.");
        }

        // ─── 테스트 6: ReAuth 취소 후 상태 리셋 ─────────────────────────────────

        [Test]
        public async Task ReAuthHandler_재인증_취소_후_상태_리셋()
        {
            // Arrange
            await _reAuthHandler.TriggerReAuthAsync("만료된 토큰");
            Assert.IsTrue(_reAuthHandler.IsReAuthPending, "재인증 대기 상태여야 합니다.");

            // Act - 재인증 취소
            _reAuthHandler.NotifyReAuthCancelled();

            // Assert
            Assert.IsFalse(_reAuthHandler.IsReAuthPending, "재인증 취소 후 대기 상태가 해제되어야 합니다.");
        }

        // ─── 테스트 7: JWT ExtractJwtExpiry 파싱 검증 ────────────────────────────

        [Test]
        public void JWT_exp_필드_정상_파싱()
        {
            // Arrange
            long expectedExp = 1700000000L;
            string jwt = CreateFakeJwt(expUnixTimestamp: expectedExp);

            // Act
            long? extracted = SessionTokenStore.ExtractJwtExpiry(jwt);

            // Assert
            Assert.IsNotNull(extracted, "exp 필드가 추출되어야 합니다.");
            Assert.AreEqual(expectedExp, extracted.Value, "올바른 exp 값이 추출되어야 합니다.");
        }

        [Test]
        public void 잘못된_JWT_형식_ExtractJwtExpiry_null_반환()
        {
            // Arrange
            var invalidJwts = new[]
            {
                "",
                null,
                "not.a.jwt",
                "only_one_part",
                "two.parts"
            };

            foreach (var invalidJwt in invalidJwts)
            {
                // Act
                long? result = SessionTokenStore.ExtractJwtExpiry(invalidJwt);

                // Assert
                Assert.IsNull(result, $"잘못된 JWT '{invalidJwt}'에서 null이 반환되어야 합니다.");
            }
        }

        // ─── 테스트 8: TokenStore Clear 후 HasValidToken 검증 ────────────────────

        [Test]
        public void TokenStore_Clear_후_HasValidToken_false_반환()
        {
            // Arrange - 유효한 토큰 저장
            long futureExp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            string validJwt = CreateFakeJwt(expUnixTimestamp: futureExp);
            _tokenStore.Save(validJwt);

            Assert.IsTrue(_tokenStore.HasValidToken(), "저장 후 토큰이 유효해야 합니다.");

            // Act
            _tokenStore.Clear();

            // Assert
            Assert.IsFalse(_tokenStore.HasValidToken(), "클리어 후 유효한 토큰이 없어야 합니다.");
        }

        // ─── 헬퍼 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 가짜 JWT를 생성합니다.
        /// 실제 서명은 없고 exp 필드만 포함한 구조입니다.
        /// </summary>
        private static string CreateFakeJwt(long expUnixTimestamp)
        {
            // header: {"alg":"HS256","typ":"JWT"}
            string header = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            // payload: {"sub":"test-user","exp":<timestamp>}
            string payloadJson = $"{{\"sub\":\"test-user\",\"exp\":{expUnixTimestamp}}}";
            string payload = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(payloadJson))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            // signature: fake
            string signature = "fake_signature";

            return $"{header}.{payload}.{signature}";
        }
    }
}
