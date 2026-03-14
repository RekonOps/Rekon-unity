using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// JSON 설정 파일에서 커스텀 마스킹 규칙을 로드하여 LogMasker에 주입하는 클래스.
    ///
    /// 설정 파일 경로: {패키지경로}/Settings/masking-rules.json
    ///
    /// JSON 스키마:
    /// <code>
    /// {
    ///   "rules": [
    ///     {
    ///       "name": "규칙 이름",
    ///       "pattern": "정규식 패턴",
    ///       "replacement": "대체 문자열",
    ///       "enabled": true
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    public static class MaskingRuleLoader
    {
        // ──────────────────────────────────────────────────────────────
        // JSON 역직렬화용 내부 DTO 클래스
        // ──────────────────────────────────────────────────────────────

        [Serializable]
        private class RuleDto
        {
            /// <summary>규칙 이름</summary>
            public string name        = "";

            /// <summary>탐지 정규식 패턴</summary>
            public string pattern     = "";

            /// <summary>대체 문자열</summary>
            public string replacement = "[MASKED]";

            /// <summary>활성화 여부</summary>
            public bool   enabled     = true;
        }

        [Serializable]
        private class RulesFileDto
        {
            /// <summary>규칙 목록</summary>
            public List<RuleDto> rules = new List<RuleDto>();
        }

        // ──────────────────────────────────────────────────────────────
        // 기본 마스킹 규칙 정의
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 내장 기본 마스킹 규칙 3종을 반환합니다.
        /// </summary>
        public static IReadOnlyList<LogMasker.MaskingRule> GetDefaultRules()
        {
            return new List<LogMasker.MaskingRule>
            {
                new LogMasker.MaskingRule
                {
                    Name        = "이메일",
                    Pattern     = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
                    Replacement = "[MASKED:EMAIL]",
                    Enabled     = true
                },
                new LogMasker.MaskingRule
                {
                    Name        = "IPv4",
                    Pattern     = @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b",
                    Replacement = "[MASKED:IP]",
                    Enabled     = true
                },
                new LogMasker.MaskingRule
                {
                    Name        = "토큰/시크릿",
                    Pattern     = @"(token|secret|password|api_key|apikey|access_key|auth)[=:]\s*[""']?[^\s""',;]+",
                    Replacement = "$1=[MASKED:TOKEN]",
                    Enabled     = true
                }
            };
        }

        // ──────────────────────────────────────────────────────────────
        // 로드 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 지정 경로의 JSON 파일에서 커스텀 마스킹 규칙을 로드하여 masker에 추가합니다.
        /// 파일이 없거나 파싱 실패 시 경고 로그만 출력하고 종료합니다.
        /// </summary>
        /// <param name="masker">규칙을 주입할 LogMasker 인스턴스</param>
        /// <param name="filePath">JSON 파일 절대 경로</param>
        /// <returns>로드에 성공한 규칙 수</returns>
        public static int LoadFromFile(LogMasker masker, string filePath)
        {
            if (masker == null)
                throw new ArgumentNullException(nameof(masker));

            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogWarning("[MaskingRuleLoader] 파일 경로가 비어있습니다. 커스텀 규칙 로드를 건너뜁니다.");
                return 0;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[MaskingRuleLoader] 마스킹 규칙 파일을 찾을 수 없습니다: {filePath}");
                return 0;
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MaskingRuleLoader] 파일 읽기 실패 ({filePath}): {ex.Message}");
                return 0;
            }

            return LoadFromJson(masker, json);
        }

        /// <summary>
        /// JSON 문자열에서 커스텀 마스킹 규칙을 파싱하여 masker에 추가합니다.
        /// </summary>
        /// <param name="masker">규칙을 주입할 LogMasker 인스턴스</param>
        /// <param name="json">규칙 JSON 문자열</param>
        /// <returns>로드에 성공한 규칙 수</returns>
        public static int LoadFromJson(LogMasker masker, string json)
        {
            if (masker == null)
                throw new ArgumentNullException(nameof(masker));

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[MaskingRuleLoader] JSON 내용이 비어있습니다.");
                return 0;
            }

            RulesFileDto dto;
            try
            {
                dto = JsonUtility.FromJson<RulesFileDto>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MaskingRuleLoader] JSON 파싱 실패: {ex.Message}");
                return 0;
            }

            if (dto == null || dto.rules == null || dto.rules.Count == 0)
            {
                Debug.LogWarning("[MaskingRuleLoader] 유효한 규칙이 없습니다.");
                return 0;
            }

            int loaded = 0;
            foreach (var ruleDto in dto.rules)
            {
                if (string.IsNullOrEmpty(ruleDto.pattern))
                {
                    Debug.LogWarning($"[MaskingRuleLoader] 규칙 '{ruleDto.name}'의 패턴이 비어있어 건너뜁니다.");
                    continue;
                }

                try
                {
                    var rule = new LogMasker.MaskingRule
                    {
                        Name        = ruleDto.name,
                        Pattern     = ruleDto.pattern,
                        Replacement = ruleDto.replacement,
                        Enabled     = ruleDto.enabled
                    };
                    masker.AddRule(rule);
                    loaded++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MaskingRuleLoader] 규칙 '{ruleDto.name}' 등록 실패: {ex.Message}");
                }
            }

            Debug.Log($"[MaskingRuleLoader] {loaded}개의 커스텀 마스킹 규칙을 로드했습니다.");
            return loaded;
        }

        // ──────────────────────────────────────────────────────────────
        // 패키지 기본 경로 해석
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 패키지 기본 마스킹 규칙 JSON 파일의 경로를 반환합니다.
        /// Packages 캐시 또는 Assets 내 Settings 폴더를 순서대로 탐색합니다.
        /// </summary>
        public static string GetDefaultRulesFilePath()
        {
            // 패키지 캐시 경로 (패키지로 설치된 경우)
            var packageCachePath = Path.Combine(
                Application.dataPath,
                "..",
                "Library",
                "PackageCache",
                "com.gaozombie.bugbeacon",
                "Settings",
                "masking-rules.json"
            );
            packageCachePath = Path.GetFullPath(packageCachePath);

            if (File.Exists(packageCachePath))
                return packageCachePath;

            // 개발 환경(Packages/com.gaozombie.bugbeacon) 경로
            var devPackagePath = Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.gaozombie.bugbeacon",
                "Settings",
                "masking-rules.json"
            );
            devPackagePath = Path.GetFullPath(devPackagePath);

            if (File.Exists(devPackagePath))
                return devPackagePath;

            // 로컬 개발 환경: Assets 형제 폴더 (유니티 패키지 직접 편집 시)
            var localPath = Path.Combine(
                Application.dataPath,
                "..",
                "Settings",
                "masking-rules.json"
            );
            localPath = Path.GetFullPath(localPath);

            return localPath;
        }
    }
}
