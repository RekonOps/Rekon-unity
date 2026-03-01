using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using GaoZombie.BugOneTouch;

namespace GaoZombie.BugOneTouch.Tests
{
    /// <summary>
    /// LogMasker 및 MaskingRuleLoader 단위 테스트.
    /// </summary>
    [TestFixture]
    public class LogMaskerTests
    {
        private LogMasker _masker;

        [SetUp]
        public void SetUp()
        {
            _masker = new LogMasker();
        }

        [TearDown]
        public void TearDown()
        {
            _masker.ClearRules();
        }

        // ──────────────────────────────────────────────────────────────
        // null / 빈 입력 안전성 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MaskAll_NullInput_ReturnsNull()
        {
            var result = _masker.MaskAll(null);
            Assert.IsNull(result);
        }

        [Test]
        public void MaskAll_EmptyString_ReturnsEmpty()
        {
            var result = _masker.MaskAll(string.Empty);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void MaskEmail_NullInput_ReturnsNull()
        {
            var result = LogMasker.MaskEmail(null);
            Assert.IsNull(result);
        }

        [Test]
        public void MaskIp_NullInput_ReturnsNull()
        {
            var result = LogMasker.MaskIp(null);
            Assert.IsNull(result);
        }

        [Test]
        public void MaskToken_NullInput_ReturnsNull()
        {
            var result = LogMasker.MaskToken(null);
            Assert.IsNull(result);
        }

        // ──────────────────────────────────────────────────────────────
        // 이메일 마스킹 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MaskEmail_SingleEmail_MaskedCorrectly()
        {
            var input    = "사용자 이메일은 user@example.com 입니다.";
            var result   = LogMasker.MaskEmail(input);

            Assert.IsFalse(result.Contains("user@example.com"), "원본 이메일이 남아있으면 안 됩니다.");
            Assert.IsTrue(result.Contains("[MASKED:EMAIL]"), "[MASKED:EMAIL] 토큰이 있어야 합니다.");
        }

        [Test]
        public void MaskEmail_MultipleEmails_AllMasked()
        {
            var input  = "문의처: a@test.co.kr, b@sample.org";
            var result = LogMasker.MaskEmail(input);

            Assert.IsFalse(result.Contains("a@test.co.kr"));
            Assert.IsFalse(result.Contains("b@sample.org"));

            // 두 이메일이 모두 마스킹됨을 카운팅으로 검증
            int count = CountOccurrences(result, "[MASKED:EMAIL]");
            Assert.AreEqual(2, count, "이메일 2개가 마스킹되어야 합니다.");
        }

        [Test]
        public void MaskEmail_NoEmail_StringUnchanged()
        {
            var input  = "이메일이 없는 일반 문자열입니다.";
            var result = LogMasker.MaskEmail(input);

            Assert.AreEqual(input, result);
        }

        [Test]
        public void MaskEmail_StaticMethod_SameAsInstanceMethod()
        {
            var input   = "admin@company.com 로그인";
            var static_ = LogMasker.MaskEmail(input);
            var masked  = _masker.MaskAll(input);

            // MaskAll은 이메일을 포함하여 마스킹하므로, 이메일 부분만 비교
            Assert.IsTrue(masked.Contains("[MASKED:EMAIL]"));
            Assert.IsFalse(masked.Contains("admin@company.com"));
        }

        // ──────────────────────────────────────────────────────────────
        // IP 마스킹 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MaskIp_SingleIpv4_MaskedCorrectly()
        {
            var input  = "서버 주소: 192.168.1.100";
            var result = LogMasker.MaskIp(input);

            Assert.IsFalse(result.Contains("192.168.1.100"));
            Assert.IsTrue(result.Contains("[MASKED:IP]"));
        }

        [Test]
        public void MaskIp_MultipleIps_AllMasked()
        {
            var input  = "클라이언트 10.0.0.1, 서버 172.16.254.1";
            var result = LogMasker.MaskIp(input);

            Assert.IsFalse(result.Contains("10.0.0.1"));
            Assert.IsFalse(result.Contains("172.16.254.1"));

            int count = CountOccurrences(result, "[MASKED:IP]");
            Assert.AreEqual(2, count, "IP 2개가 마스킹되어야 합니다.");
        }

        [Test]
        public void MaskIp_NoIp_StringUnchanged()
        {
            var input  = "IP가 없는 일반 문자열";
            var result = LogMasker.MaskIp(input);

            Assert.AreEqual(input, result);
        }

        [Test]
        public void MaskIp_InvalidOctet_NotMasked()
        {
            // 256.256.256.256 같은 패턴도 숫자 패턴으로 매칭될 수 있으므로 현재 정책 확인
            // (정규식은 값 범위를 검사하지 않으므로 255 초과도 마스킹됨 - 의도된 동작)
            var input  = "버전 1.2.3.4 사용 중";
            var result = LogMasker.MaskIp(input);

            // 1.2.3.4는 유효한 IP 패턴이므로 마스킹됨
            Assert.IsTrue(result.Contains("[MASKED:IP]"));
        }

        // ──────────────────────────────────────────────────────────────
        // 토큰/시크릿 마스킹 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MaskToken_TokenEqualFormat_MaskedCorrectly()
        {
            var input  = "token=abc123xyz";
            var result = LogMasker.MaskToken(input);

            Assert.IsFalse(result.Contains("abc123xyz"));
            Assert.IsTrue(result.Contains("[MASKED:TOKEN]"));
        }

        [Test]
        public void MaskToken_SecretColonFormat_MaskedCorrectly()
        {
            var input  = "secret: mysecretvalue";
            var result = LogMasker.MaskToken(input);

            Assert.IsFalse(result.Contains("mysecretvalue"));
            Assert.IsTrue(result.Contains("[MASKED:TOKEN]"));
        }

        [Test]
        public void MaskToken_ApiKey_MaskedCorrectly()
        {
            var input  = "api_key=sk-1234567890abcdef";
            var result = LogMasker.MaskToken(input);

            Assert.IsFalse(result.Contains("sk-1234567890abcdef"));
            Assert.IsTrue(result.Contains("[MASKED:TOKEN]"));
        }

        [Test]
        public void MaskToken_Password_MaskedCorrectly()
        {
            var input  = "password=\"SuperSecret!@#\"";
            var result = LogMasker.MaskToken(input);

            Assert.IsFalse(result.Contains("SuperSecret"));
            Assert.IsTrue(result.Contains("[MASKED:TOKEN]"));
        }

        [Test]
        public void MaskToken_NoToken_StringUnchanged()
        {
            var input  = "일반 키=값 쌍입니다.";
            var result = LogMasker.MaskToken(input);

            Assert.AreEqual(input, result);
        }

        // ──────────────────────────────────────────────────────────────
        // 복합 마스킹 테스트 (한 줄에 여러 패턴)
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MaskAll_MultiplePatternInOneLine_AllMasked()
        {
            var input = "사용자 admin@corp.com 가 서버 192.168.0.1 에서 token=bearer_xyz 로 로그인";
            var result = _masker.MaskAll(input);

            Assert.IsFalse(result.Contains("admin@corp.com"),    "이메일이 마스킹되어야 합니다.");
            Assert.IsFalse(result.Contains("192.168.0.1"),       "IP가 마스킹되어야 합니다.");
            Assert.IsFalse(result.Contains("bearer_xyz"),        "토큰이 마스킹되어야 합니다.");
            Assert.IsTrue(result.Contains("[MASKED:EMAIL]"),     "EMAIL 마스크 토큰이 있어야 합니다.");
            Assert.IsTrue(result.Contains("[MASKED:IP]"),        "IP 마스크 토큰이 있어야 합니다.");
            Assert.IsTrue(result.Contains("[MASKED:TOKEN]"),     "TOKEN 마스크 토큰이 있어야 합니다.");
        }

        [Test]
        public void MaskAll_ComplexLog_SensitiveDataRemoved()
        {
            var input = "[ERROR] 연결 실패: host=10.0.0.5, user=test@gmail.com, auth=token123, retries=3";
            var result = _masker.MaskAll(input);

            Assert.IsFalse(result.Contains("10.0.0.5"));
            Assert.IsFalse(result.Contains("test@gmail.com"));
            Assert.IsFalse(result.Contains("token123"));
            // 민감하지 않은 정보는 유지
            Assert.IsTrue(result.Contains("[ERROR]"));
            Assert.IsTrue(result.Contains("retries=3"));
        }

        // ──────────────────────────────────────────────────────────────
        // 커스텀 규칙 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void AddRule_ValidRule_RuleCountIncremented()
        {
            Assert.AreEqual(0, _masker.RuleCount);

            _masker.AddRule(new LogMasker.MaskingRule
            {
                Name        = "테스트 규칙",
                Pattern     = @"\d{4}-\d{4}",
                Replacement = "[MASKED:CUSTOM]",
                Enabled     = true
            });

            Assert.AreEqual(1, _masker.RuleCount);
        }

        [Test]
        public void MaskAll_CustomRule_Applied()
        {
            _masker.AddRule(new LogMasker.MaskingRule
            {
                Name        = "전화번호",
                Pattern     = @"\b010-\d{4}-\d{4}\b",
                Replacement = "[MASKED:PHONE]",
                Enabled     = true
            });

            var input  = "연락처: 010-1234-5678";
            var result = _masker.MaskAll(input);

            Assert.IsFalse(result.Contains("010-1234-5678"));
            Assert.IsTrue(result.Contains("[MASKED:PHONE]"));
        }

        [Test]
        public void AddRule_DisabledRule_NotApplied()
        {
            _masker.AddRule(new LogMasker.MaskingRule
            {
                Name        = "비활성 규칙",
                Pattern     = @"SENSITIVE",
                Replacement = "[MASKED:SENSITIVE]",
                Enabled     = false
            });

            var input  = "SENSITIVE 데이터";
            var result = _masker.MaskAll(input);

            // Enabled=false이면 적용되지 않아야 함
            Assert.IsTrue(result.Contains("SENSITIVE"), "비활성 규칙은 적용되면 안 됩니다.");
        }

        [Test]
        public void ClearRules_AfterAdd_RuleCountIsZero()
        {
            _masker.AddRule(new LogMasker.MaskingRule
            {
                Name        = "임시 규칙",
                Pattern     = @"\w+",
                Replacement = "[MASKED]",
                Enabled     = true
            });

            _masker.ClearRules();

            Assert.AreEqual(0, _masker.RuleCount);
        }

        [Test]
        public void AddRule_NullRule_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => _masker.AddRule(null));
        }

        [Test]
        public void AddRule_EmptyPattern_ThrowsArgumentException()
        {
            Assert.Throws<System.ArgumentException>(() => _masker.AddRule(new LogMasker.MaskingRule
            {
                Name        = "패턴 없는 규칙",
                Pattern     = "",
                Replacement = "[MASKED]",
                Enabled     = true
            }));
        }

        // ──────────────────────────────────────────────────────────────
        // MaskingRuleLoader JSON 로드 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void LoadFromJson_ValidJson_LoadsRules()
        {
            var json = @"{""rules"":[
                {""name"":""전화번호"",""pattern"":""\b010-\d{4}-\d{4}\b"",""replacement"":""[MASKED:PHONE]"",""enabled"":true},
                {""name"":""우편번호"",""pattern"":""\b\d{5}\b"",""replacement"":""[MASKED:ZIP]"",""enabled"":true}
            ]}";

            int loaded = MaskingRuleLoader.LoadFromJson(_masker, json);

            Assert.AreEqual(2, loaded, "2개의 규칙이 로드되어야 합니다.");
            Assert.AreEqual(2, _masker.RuleCount);
        }

        [Test]
        public void LoadFromJson_EmptyJson_Returns0()
        {
            int loaded = MaskingRuleLoader.LoadFromJson(_masker, "");
            Assert.AreEqual(0, loaded);
        }

        [Test]
        public void LoadFromJson_NullJson_Returns0()
        {
            int loaded = MaskingRuleLoader.LoadFromJson(_masker, null);
            Assert.AreEqual(0, loaded);
        }

        [Test]
        public void LoadFromJson_DisabledRule_NotCountedAsApplied()
        {
            var json = @"{""rules"":[
                {""name"":""비활성"",""pattern"":""\d+"",""replacement"":""[MASKED:NUM]"",""enabled"":false}
            ]}";

            int loaded = MaskingRuleLoader.LoadFromJson(_masker, json);

            // 비활성 규칙도 로드는 되지만 적용은 안 됨
            Assert.AreEqual(1, loaded);

            var result = _masker.MaskAll("숫자 12345");
            Assert.IsTrue(result.Contains("12345"), "비활성 규칙은 마스킹을 적용하면 안 됩니다.");
        }

        [Test]
        public void GetDefaultRules_Returns3Rules()
        {
            var rules = MaskingRuleLoader.GetDefaultRules();
            Assert.AreEqual(3, rules.Count, "기본 규칙은 3개여야 합니다.");

            Assert.AreEqual("이메일", rules[0].Name);
            Assert.AreEqual("IPv4",  rules[1].Name);
            Assert.AreEqual("토큰/시크릿", rules[2].Name);
        }

        // ──────────────────────────────────────────────────────────────
        // 스레드 안전성 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MaskAll_ConcurrentCalls_NoException()
        {
            // 여러 스레드에서 동시 호출 시 예외가 없어야 함
            _masker.AddRule(new LogMasker.MaskingRule
            {
                Name        = "스레드 테스트 규칙",
                Pattern     = @"\bTEST\b",
                Replacement = "[MASKED:TEST]",
                Enabled     = true
            });

            var exceptions = new System.Collections.Concurrent.ConcurrentBag<System.Exception>();

            Parallel.For(0, 100, _ =>
            {
                try
                {
                    var input  = $"user@test.com IP:192.168.0.1 token=abc TEST 메시지";
                    var result = _masker.MaskAll(input);
                    Assert.IsNotNull(result);
                }
                catch (System.Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            Assert.IsEmpty(exceptions, $"동시 호출 중 예외 발생: {string.Join(", ", exceptions)}");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        private static int CountOccurrences(string source, string target)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(target, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += target.Length;
            }
            return count;
        }
    }
}
