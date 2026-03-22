using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 로그 문자열 내 민감 정보(이메일, IP, 토큰 등)를 마스킹하는 유틸리티 클래스.
    ///
    /// 스레드 안전성:
    ///   Regex.Replace는 인스턴스 메서드가 아닌 컴파일된 패턴을 사용하므로 안전합니다.
    ///   규칙 목록 변경 시 ReaderWriterLockSlim으로 보호합니다.
    /// </summary>
    public class LogMasker
    {
        // ──────────────────────────────────────────────────────────────
        // 기본 마스킹 정규식 패턴
        // ──────────────────────────────────────────────────────────────

        /// <summary>이메일 주소 탐지 패턴</summary>
        private static readonly Regex EmailRegex = new Regex(
            @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>IPv4 주소 탐지 패턴</summary>
        private static readonly Regex IpRegex = new Regex(
            @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b",
            RegexOptions.Compiled
        );

        /// <summary>토큰/시크릿 키-값 쌍 탐지 패턴</summary>
        private static readonly Regex TokenRegex = new Regex(
            @"(token|secret|password|api_key|apikey|access_key|auth)[=:]\s*[""']?[^\s""',;]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>Bearer 토큰 탐지 패턴</summary>
        private static readonly Regex BearerRegex = new Regex(
            @"Bearer\s+[A-Za-z0-9\-._~+/]+=*",
            RegexOptions.Compiled);

        // ──────────────────────────────────────────────────────────────
        // 마스킹 형식 상수
        // ──────────────────────────────────────────────────────────────

        private const string EmailMask   = "[MASKED:EMAIL]";
        private const string IpMask      = "[MASKED:IP]";
        private const string TokenPrefix = "$1=[MASKED:TOKEN]";

        // ──────────────────────────────────────────────────────────────
        // 커스텀 규칙 관리
        // ──────────────────────────────────────────────────────────────

        /// <summary>커스텀 마스킹 규칙 항목</summary>
        public class MaskingRule
        {
            /// <summary>규칙 이름 (디버그 용도)</summary>
            public string Name        { get; set; }

            /// <summary>탐지할 정규식 패턴</summary>
            public string Pattern     { get; set; }

            /// <summary>대체 문자열 (Regex.Replace replacement)</summary>
            public string Replacement { get; set; }

            /// <summary>규칙 활성화 여부</summary>
            public bool   Enabled     { get; set; }

            /// <summary>컴파일된 Regex (내부 캐시)</summary>
            internal Regex CompiledRegex { get; set; }
        }

        private readonly List<MaskingRule>       _customRules = new List<MaskingRule>();
        private readonly ReaderWriterLockSlim    _rulesLock   = new ReaderWriterLockSlim();

        // ──────────────────────────────────────────────────────────────
        // 기본 마스킹 메서드 (개별)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 입력 문자열에서 이메일 주소를 마스킹합니다.
        /// </summary>
        /// <param name="input">원본 문자열</param>
        /// <returns>이메일이 마스킹된 문자열</returns>
        public static string MaskEmail(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return EmailRegex.Replace(input, EmailMask);
        }

        /// <summary>
        /// 입력 문자열에서 IPv4 주소를 마스킹합니다.
        /// 로컬 및 사설망 IP(127.0.0.1, 10.x.x.x, 192.168.x.x, 172.16~31.x.x 등)는 마스킹하지 않습니다.
        /// </summary>
        /// <param name="input">원본 문자열</param>
        /// <returns>공인 IP가 마스킹된 문자열</returns>
        public static string MaskIp(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return IpRegex.Replace(input, match =>
            {
                string ip = match.Value;
                if (IsPrivateOrLocalIp(ip)) return ip;
                return IpMask;
            });
        }

        /// <summary>
        /// IP 주소가 로컬 또는 사설망 주소인지 판별합니다.
        /// </summary>
        /// <param name="ip">판별할 IPv4 주소 문자열</param>
        /// <returns>로컬/사설망이면 true, 공인 IP이면 false</returns>
        private static bool IsPrivateOrLocalIp(string ip)
        {
            if (ip == "127.0.0.1" || ip == "0.0.0.0") return true;
            var parts = ip.Split('.');
            if (parts.Length != 4) return false;
            if (!int.TryParse(parts[0], out int first)) return false;
            if (!int.TryParse(parts[1], out int second)) return false;
            if (first == 10) return true;
            if (first == 172 && second >= 16 && second <= 31) return true;
            if (first == 192 && second == 168) return true;
            return false;
        }

        /// <summary>
        /// 입력 문자열에서 토큰/시크릿 키-값 쌍을 마스킹합니다.
        /// </summary>
        /// <param name="input">원본 문자열</param>
        /// <returns>토큰이 마스킹된 문자열</returns>
        public static string MaskToken(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return TokenRegex.Replace(input, TokenPrefix);
        }

        /// <summary>
        /// 입력 문자열에서 Bearer 토큰을 마스킹합니다.
        /// </summary>
        /// <param name="input">원본 문자열</param>
        /// <returns>Bearer 토큰이 마스킹된 문자열</returns>
        public static string MaskBearer(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return BearerRegex.Replace(input, "Bearer [MASKED:TOKEN]");
        }

        // ──────────────────────────────────────────────────────────────
        // 통합 마스킹 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 입력 문자열에 모든 기본 마스킹 규칙과 커스텀 규칙을 순서대로 적용합니다.
        /// </summary>
        /// <param name="input">원본 문자열</param>
        /// <returns>모든 민감 정보가 마스킹된 문자열</returns>
        public string MaskAll(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // 기본 규칙 4종 적용
            var result = MaskEmail(input);
            result = MaskIp(result);
            result = MaskToken(result);
            result = MaskBearer(result);

            // 커스텀 규칙 적용
            _rulesLock.EnterReadLock();
            try
            {
                foreach (var rule in _customRules)
                {
                    if (!rule.Enabled || rule.CompiledRegex == null)
                        continue;

                    result = rule.CompiledRegex.Replace(result, rule.Replacement);
                }
            }
            finally
            {
                _rulesLock.ExitReadLock();
            }

            return result;
        }

        // ──────────────────────────────────────────────────────────────
        // 커스텀 규칙 관리 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 커스텀 마스킹 규칙을 추가합니다.
        /// </summary>
        /// <param name="rule">추가할 마스킹 규칙</param>
        public void AddRule(MaskingRule rule)
        {
            if (rule == null)
                throw new ArgumentNullException(nameof(rule));

            if (string.IsNullOrEmpty(rule.Pattern))
                throw new ArgumentException("규칙 패턴이 비어있습니다.", nameof(rule));

            // Regex 컴파일 (쓰기 락 밖에서 미리 컴파일하여 락 점유 시간 최소화)
            var compiled = new Regex(rule.Pattern, RegexOptions.Compiled);

            _rulesLock.EnterWriteLock();
            try
            {
                rule.CompiledRegex = compiled;
                _customRules.Add(rule);
            }
            finally
            {
                _rulesLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 등록된 모든 커스텀 마스킹 규칙을 제거합니다.
        /// </summary>
        public void ClearRules()
        {
            _rulesLock.EnterWriteLock();
            try
            {
                _customRules.Clear();
            }
            finally
            {
                _rulesLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// 현재 등록된 커스텀 마스킹 규칙 수를 반환합니다.
        /// </summary>
        public int RuleCount
        {
            get
            {
                _rulesLock.EnterReadLock();
                try
                {
                    return _customRules.Count;
                }
                finally
                {
                    _rulesLock.ExitReadLock();
                }
            }
        }
    }
}
