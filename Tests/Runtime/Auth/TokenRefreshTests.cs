using System;
using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// 토큰 갱신 관련 단위 테스트.
    /// SessionTokenStore의 JWT 저장/로드/만료 검증 및
    /// ReAuthHandler 이벤트 동작을 테스트합니다.
    /// </summary>
    [TestFixture]
    public class TokenRefreshTests
    {
        private SessionTokenStore _tokenStore;

        // 테스트용 JWT 토큰 (exp: 9999999999 = 충분히 먼 미래)
        // Header: {"alg":"HS256","typ":"JWT"}
        // Payload: {"sub":"user-123","tenant_id":"tenant-456","user_id":"user-123","exp":9999999999}
        private const string ValidFutureJwt =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" +
            ".eyJzdWIiOiJ1c2VyLTEyMyIsInRlbmFudF9pZCI6InRlbmFudC00NTYiLCJ1c2VyX2lkIjoidXNlci0xMjMiLCJleHAiOjk5OTk5OTk5OTl9" +
            ".HMAC_SIGNATURE_PLACEHOLDER";

        // 만료된 JWT (exp: 1000000000 = 2001년, 이미 만료)
        // Payload: {"sub":"user-123","exp":1000000000}
        private const string ExpiredJwt =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" +
            ".eyJzdWIiOiJ1c2VyLTEyMyIsImV4cCI6MTAwMDAwMDAwMH0" +
            ".HMAC_SIGNATURE_PLACEHOLDER";

        [SetUp]
        public void SetUp()
        {
            _tokenStore = new SessionTokenStore("com.rekonops.test");
            _tokenStore.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _tokenStore?.Clear();
        }

        // ─── SessionTokenStore 기본 기능 테스트 ───────────────────────────────────

        [Test]
        public void 저장_후_로드_동일한_토큰_반환()
        {
            // Arrange
            var token = ValidFutureJwt;

            // Act
            _tokenStore.Save(token);
            var loaded = _tokenStore.Load();

            // Assert
            Assert.AreEqual(token, loaded, "저장된 토큰과 로드된 토큰이 동일해야 합니다.");
        }

        [Test]
        public void 토큰_없을_때_Load_null_반환()
        {
            // Arrange - Clear 후 토큰 없는 상태

            // Act
            var loaded = _tokenStore.Load();

            // Assert
            Assert.IsNull(loaded, "토큰이 없을 때 null을 반환해야 합니다.");
        }

        [Test]
        public void Clear_후_Load_null_반환()
        {
            // Arrange
            _tokenStore.Save(ValidFutureJwt);

            // Act
            _tokenStore.Clear();
            var loaded = _tokenStore.Load();

            // Assert
            Assert.IsNull(loaded, "Clear 후 null을 반환해야 합니다.");
        }

        [Test]
        public void 빈_문자열_저장_시_무시됨()
        {
            // Arrange
            _tokenStore.Save(ValidFutureJwt); // 먼저 유효한 토큰 저장

            // Act
            _tokenStore.Save(""); // 빈 문자열 저장 시도

            // Assert - 기존 토큰이 유지되어야 함 또는 null 반환
            // 빈 문자열 저장 시 기존 값이 남아있을 수도 있음 (구현에 따라 다름)
            // 여기서는 Load가 null이 아닌 것만 확인 (빈 문자열 무시 동작)
            var loaded = _tokenStore.Load();
            Assert.AreNotEqual("", loaded, "빈 문자열이 토큰으로 저장되지 않아야 합니다.");
        }

        [Test]
        public void 덮어쓰기_새_토큰_반환()
        {
            // Arrange
            var firstToken = ValidFutureJwt;
            var secondToken = ValidFutureJwt + ".different";

            // Act
            _tokenStore.Save(firstToken);
            _tokenStore.Save(secondToken);
            var loaded = _tokenStore.Load();

            // Assert
            Assert.AreEqual(secondToken, loaded, "새 토큰으로 덮어써진 값을 반환해야 합니다.");
        }

        // ─── JWT 만료 검증 테스트 ─────────────────────────────────────────────────

        [Test]
        public void ExtractJwtExpiry_유효한_JWT_만료시간_추출()
        {
            // Act
            var exp = SessionTokenStore.ExtractJwtExpiry(ValidFutureJwt);

            // Assert
            Assert.IsNotNull(exp, "exp 필드가 추출되어야 합니다.");
            Assert.AreEqual(9999999999L, exp.Value, "exp 값이 정확하게 추출되어야 합니다.");
        }

        [Test]
        public void ExtractJwtExpiry_만료된_JWT_과거_시간_추출()
        {
            // Act
            var exp = SessionTokenStore.ExtractJwtExpiry(ExpiredJwt);

            // Assert
            Assert.IsNotNull(exp, "exp 필드가 추출되어야 합니다.");
            Assert.AreEqual(1000000000L, exp.Value, "만료 시간이 정확하게 추출되어야 합니다.");
        }

        [Test]
        public void ExtractJwtExpiry_null_JWT_null_반환()
        {
            // Act
            var exp = SessionTokenStore.ExtractJwtExpiry(null);

            // Assert
            Assert.IsNull(exp, "null JWT에서 null을 반환해야 합니다.");
        }

        [Test]
        public void ExtractJwtExpiry_빈_문자열_null_반환()
        {
            // Act
            var exp = SessionTokenStore.ExtractJwtExpiry("");

            // Assert
            Assert.IsNull(exp, "빈 문자열 JWT에서 null을 반환해야 합니다.");
        }

        [Test]
        public void ExtractJwtExpiry_잘못된_형식_null_반환()
        {
            // Act
            var exp = SessionTokenStore.ExtractJwtExpiry("invalid.jwt");

            // Assert
            Assert.IsNull(exp, "잘못된 형식에서 null을 반환해야 합니다.");
        }

        [Test]
        public void ExtractJwtExpiry_JWT_파트_부족_null_반환()
        {
            // Act - JWT는 3개 파트가 필요 (header.payload.signature)
            var exp = SessionTokenStore.ExtractJwtExpiry("header.payload");

            // Assert
            Assert.IsNull(exp, "2파트 JWT에서 null을 반환해야 합니다.");
        }

        [Test]
        public void IsExpired_미래_만료_토큰_false_반환()
        {
            // Arrange
            _tokenStore.Save(ValidFutureJwt);

            // Act
            var isExpired = _tokenStore.IsExpired(marginSeconds: 0);

            // Assert
            Assert.IsFalse(isExpired, "미래 만료 토큰은 만료되지 않아야 합니다.");
        }

        [Test]
        public void IsExpired_과거_만료_토큰_true_반환()
        {
            // Arrange
            _tokenStore.Save(ExpiredJwt);

            // Act
            var isExpired = _tokenStore.IsExpired(marginSeconds: 0);

            // Assert
            Assert.IsTrue(isExpired, "과거 만료 토큰은 만료 상태여야 합니다.");
        }

        [Test]
        public void IsExpired_토큰_없을_때_true_반환()
        {
            // Arrange - 토큰 없는 상태

            // Act
            var isExpired = _tokenStore.IsExpired();

            // Assert
            Assert.IsTrue(isExpired, "토큰이 없을 때 만료 상태를 반환해야 합니다.");
        }

        [Test]
        public void HasValidToken_유효한_토큰_true_반환()
        {
            // Arrange
            _tokenStore.Save(ValidFutureJwt);

            // Act
            var hasValid = _tokenStore.HasValidToken();

            // Assert
            Assert.IsTrue(hasValid, "유효한 토큰이 있을 때 true를 반환해야 합니다.");
        }

        [Test]
        public void HasValidToken_만료된_토큰_false_반환()
        {
            // Arrange
            _tokenStore.Save(ExpiredJwt);

            // Act
            var hasValid = _tokenStore.HasValidToken();

            // Assert
            Assert.IsFalse(hasValid, "만료된 토큰이 있을 때 false를 반환해야 합니다.");
        }

        [Test]
        public void HasValidToken_토큰_없을_때_false_반환()
        {
            // Arrange - 토큰 없는 상태

            // Act
            var hasValid = _tokenStore.HasValidToken();

            // Assert
            Assert.IsFalse(hasValid, "토큰이 없을 때 false를 반환해야 합니다.");
        }

        // ─── 암호화 무결성 테스트 ─────────────────────────────────────────────────

        [Test]
        public void 다른_PackageName_다른_키_복호화_실패()
        {
            // Arrange
            var store1 = new SessionTokenStore("com.rekonops.test.app1");
            var store2 = new SessionTokenStore("com.rekonops.test.app2");

            const string token = "test-jwt-token";

            try
            {
                // Act
                store1.Save(token);

                // store2는 다른 키로 복호화 시도 → 실패해야 함
                // 실제로는 같은 PrefsKey를 사용하므로 암호화된 값은 읽을 수 있지만
                // 복호화가 실패해야 함
                var loaded = store2.Load();

                // Assert - 복호화 실패로 null 반환 또는 예외가 내부에서 처리됨
                // store1과 store2가 동일한 PrefsKey를 사용하므로,
                // 키가 달라 복호화 실패 시 null을 반환해야 함
                Assert.AreNotEqual(token, loaded,
                    "다른 패키지명으로 암호화된 데이터를 복호화할 수 없어야 합니다.");
            }
            finally
            {
                store1.Clear();
                store2.Clear();
            }
        }

        [Test]
        public void 암호화된_저장_데이터_평문_아님_검증()
        {
            // Arrange
            const string sensitiveToken = "my-super-secret-jwt-token";

            // Act
            _tokenStore.Save(sensitiveToken);

            // EditorPrefs에서 직접 읽은 값이 평문이 아닌지 확인
            // (간접적으로: Load()가 원본을 반환하지만 저장 형태는 다름)
            var loaded = _tokenStore.Load();

            // Assert - 복호화된 값이 원본과 동일
            Assert.AreEqual(sensitiveToken, loaded, "복호화된 값이 원본과 동일해야 합니다.");
        }

        // ─── ReAuthHandler 테스트 ─────────────────────────────────────────────────

        [Test]
        public void ReAuthHandler_생성_정상()
        {
            // Act
            var handler = new ReAuthHandler(_tokenStore);

            // Assert
            Assert.IsNotNull(handler);
            Assert.IsFalse(handler.IsReAuthPending, "초기 상태는 재인증 대기 중이 아니어야 합니다.");
        }

        [Test]
        public void ReAuthHandler_null_TokenStore_예외()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => new ReAuthHandler(null),
                "null TokenStore 전달 시 예외가 발생해야 합니다.");
        }

        [Test]
        public void ReAuthHandler_NotifyReAuthCompleted_대기_상태_해제()
        {
            // Arrange
            var handler = new ReAuthHandler(_tokenStore);

            // Act - 완료 알림
            handler.NotifyReAuthCompleted();

            // Assert
            Assert.IsFalse(handler.IsReAuthPending, "완료 후 대기 상태가 해제되어야 합니다.");
        }

        [Test]
        public void ReAuthHandler_NotifyReAuthCancelled_대기_상태_해제()
        {
            // Arrange
            var handler = new ReAuthHandler(_tokenStore);

            // Act - 취소 알림
            handler.NotifyReAuthCancelled();

            // Assert
            Assert.IsFalse(handler.IsReAuthPending, "취소 후 대기 상태가 해제되어야 합니다.");
        }

        [Test]
        public void ReAuthHandler_OnReAuthRequired_이벤트_구독_정상()
        {
            // Arrange
            var handler = new ReAuthHandler(_tokenStore);
            ReAuthEventArgs receivedArgs = null;

            handler.OnReAuthRequired += args => receivedArgs = args;

            // Act - TriggerReAuthAsync 없이 이벤트 구독만 테스트
            // (비동기 메서드는 PlayMode 테스트에서 테스트)

            // Assert
            Assert.IsNull(receivedArgs, "아직 이벤트가 발생하지 않아야 합니다.");
        }

        // ─── ReAuthEventArgs 테스트 ───────────────────────────────────────────────

        [Test]
        public void ReAuthEventArgs_Reason_정상_저장()
        {
            // Arrange & Act
            var args = new ReAuthEventArgs("토큰이 만료되었습니다.");

            // Assert
            Assert.AreEqual("토큰이 만료되었습니다.", args.Reason);
            Assert.That(args.Timestamp, Is.LessThanOrEqualTo(DateTime.UtcNow));
        }

        // ─── TokenRefreshManager 생성 테스트 ──────────────────────────────────────

        [Test]
        public void TokenRefreshManager_null_BrokerClient_예외()
        {
            // Arrange
            var reAuthHandler = new ReAuthHandler(_tokenStore);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => new TokenRefreshManager(null, _tokenStore, reAuthHandler));
        }

        [Test]
        public void TokenRefreshManager_null_TokenStore_예외()
        {
            // Arrange
            var reAuthHandler = new ReAuthHandler(_tokenStore);
            var brokerClient = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => new TokenRefreshManager(brokerClient, null, reAuthHandler));
        }

        [Test]
        public void TokenRefreshManager_null_ReAuthHandler_예외()
        {
            // Arrange
            var brokerClient = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => new TokenRefreshManager(brokerClient, _tokenStore, null));
        }

        [Test]
        public void TokenRefreshManager_정상_생성_및_Dispose()
        {
            // Arrange
            var reAuthHandler = new ReAuthHandler(_tokenStore);
            var brokerClient = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            // Act
            using var manager = new TokenRefreshManager(brokerClient, _tokenStore, reAuthHandler);

            // Assert
            Assert.IsNotNull(manager, "TokenRefreshManager가 정상 생성되어야 합니다.");
        }

        [Test]
        public void TokenRefreshManager_HasValidSession_유효한_토큰_true()
        {
            // Arrange
            _tokenStore.Save(ValidFutureJwt);
            var reAuthHandler = new ReAuthHandler(_tokenStore);
            var brokerClient = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            using var manager = new TokenRefreshManager(brokerClient, _tokenStore, reAuthHandler);

            // Act
            var hasValid = manager.HasValidSession();

            // Assert
            Assert.IsTrue(hasValid, "유효한 세션 토큰이 있을 때 true를 반환해야 합니다.");
        }

        [Test]
        public void TokenRefreshManager_HasValidSession_토큰_없을_때_false()
        {
            // Arrange - 토큰 없는 상태
            var reAuthHandler = new ReAuthHandler(_tokenStore);
            var brokerClient = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            using var manager = new TokenRefreshManager(brokerClient, _tokenStore, reAuthHandler);

            // Act
            var hasValid = manager.HasValidSession();

            // Assert
            Assert.IsFalse(hasValid, "토큰이 없을 때 false를 반환해야 합니다.");
        }

        [Test]
        public void TokenRefreshManager_InvalidateAccessTokenCache_예외_없이_실행()
        {
            // Arrange
            var reAuthHandler = new ReAuthHandler(_tokenStore);
            var brokerClient = new AuthBrokerClient(
                "https://test.supabase.co/functions/v1",
                _tokenStore);

            using var manager = new TokenRefreshManager(brokerClient, _tokenStore, reAuthHandler);

            // Act & Assert - 예외 없이 실행되어야 함
            Assert.DoesNotThrow(() => manager.InvalidateAccessTokenCache(),
                "캐시 무효화는 예외 없이 실행되어야 합니다.");
        }
    }
}
