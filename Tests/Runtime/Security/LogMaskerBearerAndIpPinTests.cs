using NUnit.Framework;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// LogMasker 회귀 안전망 (characterization) 테스트.
    ///
    /// 목적: §7 "기능 무영향 보안 강화" 작업 전, 현재 마스킹 규칙을 핀(pin)으로 고정합니다.
    ///       기존 LogMaskerTests.cs 에 누락된 두 영역을 보강합니다.
    ///         1) MaskBearer — Bearer 토큰 마스킹 (기존 테스트 전무)
    ///         2) MaskIp 의 사설/로컬 IP 예외 (IsPrivateOrLocalIp) — 미검증
    ///
    /// 주의: 새 기능/이상적 동작이 아니라 "현재 코드 동작"을 고정하는 것이 목적입니다.
    ///        보안 작업 후 이 테스트가 red 가 되면 = 마스킹 규칙 회귀.
    /// </summary>
    [TestFixture]
    public class LogMaskerBearerAndIpPinTests
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
        // MaskBearer — Bearer 토큰 마스킹 핀
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void MaskBearer_AuthorizationHeader_ReplacedWithMaskedToken()
        {
            // BearerRegex = @"Bearer\s+[A-Za-z0-9\-._~+/]+=*"
            // 허용 문자(A-Za-z0-9-._~+/) + 끝의 '=' 패딩만 매칭됨에 유의.
            var input  = "Authorization: Bearer abc123XYZ-._~+/=";
            var result = LogMasker.MaskBearer(input);

            Assert.IsTrue(result.Contains("Bearer [MASKED:TOKEN]"),
                "'Bearer [MASKED:TOKEN]' 로 치환되어야 합니다.");
            Assert.IsFalse(result.Contains("abc123XYZ"),
                "원본 토큰 문자열이 남아있으면 안 됩니다.");
        }

        [Test]
        public void MaskBearer_NoBearer_StringUnchanged()
        {
            // Bearer 키워드가 없으면 현재 동작상 불변
            var input  = "이것은 토큰이 없는 일반 로그 라인입니다.";
            var result = LogMasker.MaskBearer(input);

            Assert.AreEqual(input, result, "Bearer 가 없으면 문자열이 변하지 않아야 합니다.");
        }

        [Test]
        public void MaskBearer_NullInput_ReturnsNull()
        {
            var result = LogMasker.MaskBearer(null);
            Assert.IsNull(result, "null 입력은 그대로 null 반환되어야 합니다.");
        }

        [Test]
        public void MaskBearer_EmptyString_ReturnsEmpty()
        {
            var result = LogMasker.MaskBearer(string.Empty);
            Assert.AreEqual(string.Empty, result, "빈 문자열은 그대로 반환되어야 합니다.");
        }

        [Test]
        public void MaskAll_IncludesBearerMasking()
        {
            // MaskAll 은 기본 규칙 4종(Email/Ip/Token/Bearer) 을 적용함.
            // Bearer 토큰이 포함된 로그에서 Bearer 마스킹이 적용되는지 핀.
            var input  = "요청 헤더 Authorization: Bearer eyJhbGci0iJIUzI1NiJ9 전송 완료";
            var result = _masker.MaskAll(input);

            Assert.IsTrue(result.Contains("Bearer [MASKED:TOKEN]"),
                "MaskAll 은 Bearer 토큰을 마스킹해야 합니다.");
            Assert.IsFalse(result.Contains("eyJhbGci0iJIUzI1NiJ9"),
                "원본 Bearer 토큰이 남아있으면 안 됩니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // MaskIp — 사설/로컬 IP 예외 (IsPrivateOrLocalIp) 핀
        // ──────────────────────────────────────────────────────────────

        [Test]
        [TestCase("10.0.0.5")]      // 10.x.x.x 사설망
        [TestCase("172.16.0.1")]    // 172.16~31.x.x 사설망 (하한)
        [TestCase("172.31.255.1")]  // 172.16~31.x.x 사설망 (상한)
        [TestCase("192.168.0.1")]   // 192.168.x.x 사설망
        [TestCase("127.0.0.1")]     // 루프백
        [TestCase("0.0.0.0")]       // 와일드카드
        public void MaskIp_PrivateOrLocalIp_NotMasked(string privateIp)
        {
            // 현재 IsPrivateOrLocalIp 동작: 사설/로컬 IP 는 마스킹하지 않고 원본 유지
            var input  = $"내부 서버 주소: {privateIp}";
            var result = LogMasker.MaskIp(input);

            Assert.IsTrue(result.Contains(privateIp),
                $"사설/로컬 IP {privateIp} 는 마스킹되지 않고 원본이 유지되어야 합니다.");
            Assert.IsFalse(result.Contains("[MASKED:IP]"),
                $"사설/로컬 IP {privateIp} 는 [MASKED:IP] 로 치환되면 안 됩니다.");
        }

        [Test]
        [TestCase("8.8.8.8")]
        [TestCase("1.2.3.4")]
        public void MaskIp_PublicIp_Masked(string publicIp)
        {
            // 공인 IP 는 마스킹됨
            var input  = $"외부 접속 IP: {publicIp}";
            var result = LogMasker.MaskIp(input);

            Assert.IsFalse(result.Contains(publicIp),
                $"공인 IP {publicIp} 는 마스킹되어 원본이 남으면 안 됩니다.");
            Assert.IsTrue(result.Contains("[MASKED:IP]"),
                $"공인 IP {publicIp} 는 [MASKED:IP] 로 치환되어야 합니다.");
        }

        [Test]
        public void MaskIp_MixedPrivateAndPublic_SelectiveReplace()
        {
            // 한 줄에 사설+공인 혼합 시: 사설은 원본 유지, 공인만 [MASKED:IP]
            var input  = "클라이언트 192.168.0.1 에서 외부 8.8.8.8 로 연결";
            var result = LogMasker.MaskIp(input);

            Assert.IsTrue(result.Contains("192.168.0.1"),
                "사설 IP 192.168.0.1 은 원본이 유지되어야 합니다.");
            Assert.IsFalse(result.Contains("8.8.8.8"),
                "공인 IP 8.8.8.8 은 마스킹되어야 합니다.");
            Assert.IsTrue(result.Contains("[MASKED:IP]"),
                "공인 IP 부분에 [MASKED:IP] 가 있어야 합니다.");
        }

        [Test]
        [TestCase("172.15.0.1")]  // 172.16 미만 → 사설 범위 밖
        [TestCase("172.32.0.1")]  // 172.31 초과 → 사설 범위 밖
        public void MaskIp_172BoundaryOutsidePrivateRange_Masked(string boundaryIp)
        {
            // 172.16~31 만 사설. 경계 밖(172.15 / 172.32)은 공인으로 취급 → 마스킹.
            var input  = $"경계값 IP: {boundaryIp}";
            var result = LogMasker.MaskIp(input);

            Assert.IsFalse(result.Contains(boundaryIp),
                $"{boundaryIp} 는 사설 범위 밖이므로 마스킹되어야 합니다.");
            Assert.IsTrue(result.Contains("[MASKED:IP]"),
                $"{boundaryIp} 는 [MASKED:IP] 로 치환되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 방법3: property/불변식 보강 — 무작위 삽입 문자열 마스킹 불변식
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 무작위 이메일 패턴 삽입 문자열에 대해 마스킹 후 원본 이메일이 남지 않는지 검증.
        ///
        /// EmailRegex = @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}"
        /// 허용 문자 범위 내 무작위 이메일로 검증합니다.
        /// </summary>
        [Test]
        public void Property_RandomEmail_AfterMaskEmail_OriginalPatternAbsent()
        {
            // 재현 가능한 시드
            var rng = new System.Random(seed: 101);
            string[] domains   = { "example.com", "test.io", "sample.org", "mail.net", "abc.co.kr" };
            string[] localPre  = { "user", "admin", "contact", "info", "support", "dev123", "qa.tester" };

            for (int trial = 0; trial < 15; trial++)
            {
                var local  = localPre[rng.Next(localPre.Length)];
                var domain = domains[rng.Next(domains.Length)];
                var email  = $"{local}{rng.Next(1, 999)}@{domain}";

                var prefix = trial % 2 == 0 ? "요청자 이메일: " : $"AUTH user={email} status=ok / ";
                var suffix = trial % 3 == 0 ? " (확인 바람)" : "";
                var input  = $"{prefix}{email}{suffix}";

                var result = LogMasker.MaskEmail(input);

                Assert.IsFalse(result.Contains(email),
                    $"trial={trial}: 마스킹 후 원본 이메일 '{email}' 이 남아있습니다.");
                Assert.IsTrue(result.Contains("[MASKED:EMAIL]"),
                    $"trial={trial}: [MASKED:EMAIL] 토큰이 없습니다.");
            }
        }

        /// <summary>
        /// 무작위 Bearer 토큰 삽입 문자열에 대해 MaskBearer 후 원본 토큰이 남지 않는지 검증.
        ///
        /// BearerRegex 허용 문자(A-Za-z0-9-._~+/) 범위 내 무작위 토큰으로 검증합니다.
        /// </summary>
        [Test]
        public void Property_RandomBearerToken_AfterMaskBearer_OriginalTokenAbsent()
        {
            var rng = new System.Random(seed: 202);
            // BearerRegex 가 허용하는 문자 집합 (끝 '=' 패딩 제외한 본체 문자)
            const string tokenChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~+/";

            for (int trial = 0; trial < 15; trial++)
            {
                int len   = rng.Next(10, 80);
                var buf   = new char[len];
                for (int j = 0; j < len; j++)
                    buf[j] = tokenChars[rng.Next(tokenChars.Length)];
                var token = new string(buf);

                var input  = $"Authorization: Bearer {token}";
                var result = LogMasker.MaskBearer(input);

                Assert.IsFalse(result.Contains(token),
                    $"trial={trial}: 마스킹 후 원본 토큰이 남아있습니다. (len={len})");
                Assert.IsTrue(result.Contains("Bearer [MASKED:TOKEN]"),
                    $"trial={trial}: 'Bearer [MASKED:TOKEN]' 토큰이 없습니다.");
            }
        }

        /// <summary>
        /// 무작위 공인 IP 삽입 문자열에 대해 MaskIp 후 원본 IP 가 남지 않는지 검증.
        ///
        /// 사설 범위(10.x, 172.16~31.x, 192.168.x, 127.x, 0.x)를 의도적으로 피해
        /// 공인 IP 대역만 생성하여 마스킹됨을 보장합니다.
        /// </summary>
        [Test]
        public void Property_RandomPublicIp_AfterMaskIp_OriginalIpAbsent()
        {
            var rng = new System.Random(seed: 303);

            for (int trial = 0; trial < 15; trial++)
            {
                // 공인 IP: 첫 옥텟 범위 제한 (사설/로컬 제외)
                // 2~9, 11~172-boundary, 173~191, 193~255 중 간단히 범위 선정
                // 편의상 첫 옥텟을 2~9 또는 20~100 에서 선택
                int first  = rng.Next(0, 2) == 0 ? rng.Next(2, 10) : rng.Next(20, 101);
                int second = rng.Next(1, 256);
                int third  = rng.Next(0, 256);
                int fourth = rng.Next(1, 256);

                // 사설 범위 우발 충돌 방지 (방어적 skip)
                bool isPrivate =
                    (first == 10) ||
                    (first == 172 && second >= 16 && second <= 31) ||
                    (first == 192 && second == 168) ||
                    (first == 127) ||
                    (first == 0);

                if (isPrivate)
                {
                    first = 8; // 안전한 공인 대역
                }

                var publicIp = $"{first}.{second}.{third}.{fourth}";
                var prefix   = trial % 2 == 0 ? "클라이언트 접속 IP: " : $"요청 출처 [{publicIp}] 처리 중 ";
                var input    = $"{prefix}{publicIp}";

                var result = LogMasker.MaskIp(input);

                Assert.IsFalse(result.Contains(publicIp),
                    $"trial={trial}: 마스킹 후 공인 IP '{publicIp}' 이 남아있습니다.");
                Assert.IsTrue(result.Contains("[MASKED:IP]"),
                    $"trial={trial}: [MASKED:IP] 토큰이 없습니다.");
            }
        }

        /// <summary>
        /// 무작위 사설 IP 삽입 문자열에 대해 MaskIp 후 원본 IP 가 **남아 있어야** 하는지 검증.
        ///
        /// 현재 IsPrivateOrLocalIp 동작: 사설/로컬 IP 는 마스킹 예외.
        /// 이 동작이 변경되면 이 테스트가 red 가 됩니다 (회귀 신호).
        /// </summary>
        [Test]
        public void Property_RandomPrivateIp_AfterMaskIp_OriginalIpPreserved()
        {
            // 사설 IP 생성기: 각 대역에서 순환 선택
            var privateIps = new System.Collections.Generic.List<string>();
            var rng = new System.Random(seed: 404);

            for (int i = 0; i < 5; i++)
                privateIps.Add($"10.{rng.Next(0, 256)}.{rng.Next(0, 256)}.{rng.Next(1, 256)}");
            for (int i = 0; i < 5; i++)
                privateIps.Add($"172.{rng.Next(16, 32)}.{rng.Next(0, 256)}.{rng.Next(1, 256)}");
            for (int i = 0; i < 5; i++)
                privateIps.Add($"192.168.{rng.Next(0, 256)}.{rng.Next(1, 256)}");

            int trial = 0;
            foreach (var privateIp in privateIps)
            {
                var input  = $"내부 서비스 호출 from {privateIp}";
                var result = LogMasker.MaskIp(input);

                Assert.IsTrue(result.Contains(privateIp),
                    $"trial={trial}: 사설 IP '{privateIp}' 가 마스킹되어 원본이 사라졌습니다 (예외 동작 회귀).");
                Assert.IsFalse(result.Contains("[MASKED:IP]"),
                    $"trial={trial}: 사설 IP '{privateIp}' 에 [MASKED:IP] 가 삽입되었습니다 (예외 동작 회귀).");
                trial++;
            }
        }
    }
}
