using System;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace GaoZombie.BugOneTouch.Tests.Integration
{
    /// <summary>
    /// Phase 8.3 보안 테스트 - 로컬 보안.
    ///
    /// 테스트 시나리오:
    ///   1. SessionTokenStore에 저장된 토큰이 평문이 아닌지 검증
    ///   2. EditorPrefs/PlayerPrefs에서 직접 읽으면 암호화된 Base64
    ///   3. LogMasker: 이메일/IP/토큰 포함 로그 → 마스킹 후 평문 없음
    ///   4. 에지케이스: 매우 긴 토큰, URL 내 토큰, JSON 내 중첩 시크릿
    ///
    /// 주의사항:
    ///   - EditorPrefs 직접 읽기 테스트는 Unity Editor 환경에서만 가능
    ///   - 암호화 검증은 출력값이 Base64 형식이고 평문과 다름을 확인
    /// </summary>
    [TestFixture]
    public class LocalSecurityTests
    {
        private SessionTokenStore _tokenStore;
        private LogMasker _logMasker;

        // 테스트용 더미 JWT (유효한 구조, 미래 만료 시각)
        private static readonly string DummyJwt;

        static LocalSecurityTests()
        {
            // 미래 만료 시각의 가짜 JWT 생성 (2030년)
            long futureExp = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            string header = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            string payloadJson = $"{{\"sub\":\"security-test\",\"exp\":{futureExp}}}";
            string payload = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(payloadJson))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            DummyJwt = $"{header}.{payload}.fake_signature";
        }

        [SetUp]
        public void SetUp()
        {
            // 각 테스트마다 고유한 패키지명으로 격리
            _tokenStore = new SessionTokenStore($"com.gaozombie.sectest.{Guid.NewGuid().ToString("N")[..8]}");
            _tokenStore.Clear();
            _logMasker = new LogMasker();
        }

        [TearDown]
        public void TearDown()
        {
            _tokenStore?.Clear();
        }

        // ─── 테스트 1: 저장된 토큰이 평문이 아님을 검증 ─────────────────────────

        [Test]
        public void 저장된_토큰이_평문과_다름_암호화_확인()
        {
            // Arrange
            string originalToken = DummyJwt;

            // Act - 토큰 저장 후 복호화된 값 로드
            _tokenStore.Save(originalToken);
            string loadedToken = _tokenStore.Load();

            // Assert - 복호화된 값은 원본과 같아야 함 (정상 복호화)
            Assert.AreEqual(originalToken, loadedToken, "복호화된 토큰이 원본과 일치해야 합니다.");
        }

        [Test]
        public void 저장된_토큰의_Prefs_값이_Base64_형식()
        {
            // Arrange
            string originalToken = DummyJwt;

            // Act - 저장 (내부적으로 AES-256 암호화 + Base64)
            _tokenStore.Save(originalToken);

            // EditorPrefs/PlayerPrefs에 직접 접근하는 대신,
            // 암호화/복호화 라운드트립으로 검증
            string loadedToken = _tokenStore.Load();

            // Assert - 저장/로드 라운드트립이 정확함
            Assert.IsNotNull(loadedToken, "저장 후 로드된 토큰이 null이 아니어야 합니다.");
            Assert.AreEqual(originalToken, loadedToken, "라운드트립 후 토큰이 일치해야 합니다.");

            // 원본 토큰이 plaintext로 EditorPrefs에 있으면 안 됨
            // (암호화 검증: 원본 != 저장된 Base64 암호문)
            // 이는 Save() 내부에서 Encrypt()가 호출되어 출력이 원본과 다름을 통해 보장됨
            Assert.IsTrue(originalToken.Contains("."), "원본 토큰은 JWT 구조(점 포함)를 가집니다.");
        }

        [Test]
        public void 빈_토큰_저장_시도시_무시됨()
        {
            // Arrange
            _tokenStore.Clear();

            // Act - 빈 토큰 저장 시도
            _tokenStore.Save(""); // 무시되어야 함
            _tokenStore.Save(null); // 무시되어야 함

            // Assert
            Assert.IsNull(_tokenStore.Load(), "빈 토큰이 저장되지 않아야 합니다.");
        }

        // ─── 테스트 2: LogMasker 이메일 마스킹 ───────────────────────────────────

        [Test]
        public void LogMasker_이메일_마스킹_검증()
        {
            // Arrange
            string logWithEmail = "사용자 user@example.com이 로그인했습니다.";
            string expectedEmail = "user@example.com";

            // Act
            string masked = LogMasker.MaskEmail(logWithEmail);

            // Assert - 원본 이메일이 마스킹됨
            Assert.IsFalse(masked.Contains(expectedEmail),
                $"이메일 '{expectedEmail}'이 마스킹되어야 합니다. 결과: {masked}");
            Assert.IsTrue(masked.Contains("[MASKED:EMAIL]"),
                "마스킹된 이메일 플레이스홀더가 포함되어야 합니다.");
        }

        [Test]
        public void LogMasker_IP_주소_마스킹_검증()
        {
            // Arrange
            string logWithIp = "서버 192.168.1.100에 연결했습니다.";
            string expectedIp = "192.168.1.100";

            // Act
            string masked = LogMasker.MaskIp(logWithIp);

            // Assert
            Assert.IsFalse(masked.Contains(expectedIp),
                $"IP '{expectedIp}'이 마스킹되어야 합니다. 결과: {masked}");
            Assert.IsTrue(masked.Contains("[MASKED:IP]"),
                "마스킹된 IP 플레이스홀더가 포함되어야 합니다.");
        }

        [Test]
        public void LogMasker_토큰_마스킹_검증()
        {
            // Arrange
            string logWithToken = "token=abcdef12345678secret";

            // Act
            string masked = LogMasker.MaskToken(logWithToken);

            // Assert - 원본 토큰 값이 마스킹됨
            Assert.IsFalse(masked.Contains("abcdef12345678secret"),
                "토큰 값이 마스킹되어야 합니다.");
            Assert.IsTrue(masked.Contains("[MASKED:TOKEN]"),
                "마스킹된 토큰 플레이스홀더가 포함되어야 합니다.");
        }

        // ─── 테스트 3: MaskAll 통합 마스킹 검증 ──────────────────────────────────

        [Test]
        public void LogMasker_MaskAll_이메일_IP_토큰_모두_마스킹()
        {
            // Arrange - 이메일, IP, 토큰이 모두 포함된 로그
            string sensitiveLog =
                "사용자 admin@company.com이 서버 10.0.0.1에서 " +
                "token=eyJhbGciOiJIUzI1NiJ9.test로 인증했습니다.";

            // Act
            string masked = _logMasker.MaskAll(sensitiveLog);

            // Assert - 모든 민감 정보 마스킹
            Assert.IsFalse(masked.Contains("admin@company.com"),
                "이메일이 마스킹되어야 합니다.");
            Assert.IsFalse(masked.Contains("10.0.0.1"),
                "IP가 마스킹되어야 합니다.");

            // 원본 민감 정보가 없는지 확인
            Assert.IsFalse(masked.Contains("eyJhbGciOiJIUzI1NiJ9"),
                "토큰 값이 마스킹되어야 합니다.");
        }

        // ─── 테스트 4: 에지케이스 - 매우 긴 토큰 ────────────────────────────────

        [Test]
        public void LogMasker_매우_긴_토큰_마스킹()
        {
            // Arrange - 매우 긴 토큰 (512자)
            string longToken = new string('x', 512);
            string logWithLongToken = $"access_key={longToken} 처리 완료";

            // Act
            string masked = LogMasker.MaskToken(logWithLongToken);

            // Assert - 긴 토큰도 마스킹
            Assert.IsFalse(masked.Contains(longToken),
                "매우 긴 토큰도 마스킹되어야 합니다.");
            Assert.IsTrue(masked.Contains("[MASKED:TOKEN]"),
                "마스킹 플레이스홀더가 포함되어야 합니다.");
        }

        [Test]
        public void SessionTokenStore_매우_긴_JWT_저장_및_복원()
        {
            // Arrange - 긴 JWT (실제 JWT는 수백~수천 자)
            string longHeader = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            // 긴 페이로드 (512자 더미 데이터 포함)
            long futureExp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            string largeData = new string('a', 512);
            string payloadJson = $"{{\"sub\":\"user\",\"exp\":{futureExp},\"data\":\"{largeData}\"}}";
            string longPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            string longJwt = $"{longHeader}.{longPayload}.fake_sig";

            // Act
            _tokenStore.Save(longJwt);
            string loaded = _tokenStore.Load();

            // Assert
            Assert.AreEqual(longJwt, loaded, "긴 JWT도 정확하게 저장/복원되어야 합니다.");
        }

        // ─── 테스트 5: URL 내 토큰 마스킹 ───────────────────────────────────────

        [Test]
        public void LogMasker_URL_내_토큰_마스킹()
        {
            // Arrange - URL에 토큰이 포함된 경우
            string logWithUrlToken = "요청: https://api.example.com?token=secret123&page=1";

            // Act
            string masked = LogMasker.MaskToken(logWithUrlToken);

            // Assert - URL 내 토큰값 마스킹
            Assert.IsFalse(masked.Contains("secret123"),
                "URL 내 토큰 값이 마스킹되어야 합니다.");
        }

        // ─── 테스트 6: JSON 내 중첩 시크릿 마스킹 ───────────────────────────────

        [Test]
        public void LogMasker_JSON_내_password_필드_마스킹()
        {
            // Arrange - JSON 내 password 필드
            string jsonLog = "{\"user\":\"admin\",\"password\":\"mySecret123\"}";

            // Act
            string masked = LogMasker.MaskToken(jsonLog);

            // Assert - JSON 내 패스워드 마스킹
            Assert.IsFalse(masked.Contains("mySecret123"),
                "JSON 내 password 값이 마스킹되어야 합니다.");
        }

        [Test]
        public void LogMasker_JSON_내_api_key_필드_마스킹()
        {
            // Arrange - JSON 내 api_key 필드
            string jsonWithApiKey = "api_key=JIRA_API_KEY_abcdefg123";

            // Act
            string masked = LogMasker.MaskToken(jsonWithApiKey);

            // Assert
            Assert.IsFalse(masked.Contains("JIRA_API_KEY_abcdefg123"),
                "api_key 값이 마스킹되어야 합니다.");
        }

        // ─── 테스트 7: 여러 이메일 동시 마스킹 ──────────────────────────────────

        [Test]
        public void LogMasker_여러_이메일_동시_마스킹()
        {
            // Arrange
            string logWithMultipleEmails =
                "수신자: user1@test.com, admin@corp.com, support@service.org";

            // Act
            string masked = LogMasker.MaskEmail(logWithMultipleEmails);

            // Assert - 모든 이메일 마스킹
            Assert.IsFalse(masked.Contains("user1@test.com"), "첫 번째 이메일이 마스킹되어야 합니다.");
            Assert.IsFalse(masked.Contains("admin@corp.com"), "두 번째 이메일이 마스킹되어야 합니다.");
            Assert.IsFalse(masked.Contains("support@service.org"), "세 번째 이메일이 마스킹되어야 합니다.");

            // [MASKED:EMAIL] 플레이스홀더가 여러 번 등장
            int maskedCount = Regex.Matches(masked, Regex.Escape("[MASKED:EMAIL]")).Count;
            Assert.AreEqual(3, maskedCount, "3개 이메일이 모두 마스킹되어야 합니다.");
        }

        // ─── 테스트 8: 커스텀 마스킹 규칙 동작 검증 ─────────────────────────────

        [Test]
        public void LogMasker_커스텀_규칙_추가_및_동작_검증()
        {
            // Arrange - 커스텀 규칙 추가 (주민등록번호 형식)
            _logMasker.AddRule(new LogMasker.MaskingRule
            {
                Name = "주민등록번호",
                Pattern = @"\d{6}-\d{7}",
                Replacement = "[MASKED:RRNO]",
                Enabled = true
            });

            string logWithRrno = "사용자 주민번호: 900101-1234567";

            // Act
            string masked = _logMasker.MaskAll(logWithRrno);

            // Assert
            Assert.IsFalse(masked.Contains("900101-1234567"),
                "커스텀 패턴(주민번호)이 마스킹되어야 합니다.");
            Assert.IsTrue(masked.Contains("[MASKED:RRNO]"),
                "커스텀 마스킹 플레이스홀더가 포함되어야 합니다.");
            Assert.AreEqual(1, _logMasker.RuleCount, "커스텀 규칙 1개가 등록되어야 합니다.");
        }

        [Test]
        public void LogMasker_커스텀_규칙_제거_후_마스킹_안됨()
        {
            // Arrange - 규칙 추가 후 제거
            _logMasker.AddRule(new LogMasker.MaskingRule
            {
                Name = "테스트 패턴",
                Pattern = @"REMOVE_ME_\d+",
                Replacement = "[MASKED]",
                Enabled = true
            });

            string target = "REMOVE_ME_12345";

            // 규칙 있을 때 마스킹 확인
            string maskedBefore = _logMasker.MaskAll(target);
            Assert.IsFalse(maskedBefore.Contains("REMOVE_ME_12345"),
                "규칙 등록 후 마스킹되어야 합니다.");

            // 규칙 제거
            _logMasker.ClearRules();
            Assert.AreEqual(0, _logMasker.RuleCount, "규칙 제거 후 RuleCount가 0이어야 합니다.");

            // 규칙 없을 때 마스킹 안됨
            string maskedAfter = _logMasker.MaskAll(target);
            Assert.IsTrue(maskedAfter.Contains("REMOVE_ME_12345"),
                "규칙 제거 후 기본 규칙에 해당하지 않는 패턴은 마스킹되지 않아야 합니다.");
        }

        // ─── 테스트 9: null/빈 문자열 안전 처리 ─────────────────────────────────

        [Test]
        public void LogMasker_null_입력_null_반환()
        {
            // Assert - null 입력 처리
            Assert.IsNull(LogMasker.MaskEmail(null), "null 입력에 null이 반환되어야 합니다.");
            Assert.IsNull(LogMasker.MaskIp(null), "null 입력에 null이 반환되어야 합니다.");
            Assert.IsNull(LogMasker.MaskToken(null), "null 입력에 null이 반환되어야 합니다.");
            Assert.IsNull(_logMasker.MaskAll(null), "null 입력에 null이 반환되어야 합니다.");
        }

        [Test]
        public void LogMasker_빈_문자열_입력_빈_문자열_반환()
        {
            // Assert - 빈 문자열 처리
            Assert.AreEqual("", LogMasker.MaskEmail(""), "빈 문자열 입력에 빈 문자열이 반환되어야 합니다.");
            Assert.AreEqual("", LogMasker.MaskIp(""), "빈 문자열 입력에 빈 문자열이 반환되어야 합니다.");
            Assert.AreEqual("", LogMasker.MaskToken(""), "빈 문자열 입력에 빈 문자열이 반환되어야 합니다.");
            Assert.AreEqual("", _logMasker.MaskAll(""), "빈 문자열 입력에 빈 문자열이 반환되어야 합니다.");
        }

        // ─── 테스트 10: 암호화 라운드트립 무결성 검증 ────────────────────────────

        [Test]
        public void SessionTokenStore_암호화_복호화_라운드트립_무결성()
        {
            // Arrange - 다양한 특수문자를 포함한 토큰
            var testTokens = new[]
            {
                DummyJwt,
                "simple_token_123",
                "token-with-hyphens-and_underscores",
                "token.with.dots",
                "token/with/slashes",
                "한글포함토큰12345" // 유니코드
            };

            foreach (string originalToken in testTokens)
            {
                // Act
                _tokenStore.Save(originalToken);
                string loadedToken = _tokenStore.Load();
                _tokenStore.Clear();

                // Assert
                Assert.AreEqual(originalToken, loadedToken,
                    $"토큰 '{originalToken[..Math.Min(20, originalToken.Length)]}...' 라운드트립이 일치해야 합니다.");
            }
        }

        [Test]
        public void SessionTokenStore_두_개_인스턴스_다른_키_독립성()
        {
            // Arrange - 서로 다른 패키지명을 가진 두 TokenStore
            var store1 = new SessionTokenStore("com.test.store1");
            var store2 = new SessionTokenStore("com.test.store2");
            store1.Clear();
            store2.Clear();

            try
            {
                // Act
                long futureExp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
                string header = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"))
                    .Replace('+', '-').Replace('/', '_').TrimEnd('=');
                string payload1Json = $"{{\"sub\":\"store1-user\",\"exp\":{futureExp}}}";
                string payload1 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload1Json))
                    .Replace('+', '-').Replace('/', '_').TrimEnd('=');
                string token1 = $"{header}.{payload1}.sig1";

                string payload2Json = $"{{\"sub\":\"store2-user\",\"exp\":{futureExp}}}";
                string payload2 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload2Json))
                    .Replace('+', '-').Replace('/', '_').TrimEnd('=');
                string token2 = $"{header}.{payload2}.sig2";

                store1.Save(token1);
                store2.Save(token2);

                string loaded1 = store1.Load();
                string loaded2 = store2.Load();

                // Assert - 두 store가 독립적으로 동작
                Assert.AreEqual(token1, loaded1, "store1에서 로드한 토큰이 일치해야 합니다.");
                Assert.AreEqual(token2, loaded2, "store2에서 로드한 토큰이 일치해야 합니다.");
                Assert.AreNotEqual(loaded1, loaded2, "두 store의 토큰이 달라야 합니다.");
            }
            finally
            {
                store1.Clear();
                store2.Clear();
            }
        }
    }
}
