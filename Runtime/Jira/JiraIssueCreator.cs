using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// Jira 이슈를 생성합니다.
    /// POST /rest/api/3/issue
    /// ADF(Atlassian Document Format)로 설명을 작성하고,
    /// BugOneTouchSettings의 defaultLabels를 자동으로 추가합니다.
    /// </summary>
    public class JiraIssueCreator
    {
        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly JiraApiClient _apiClient;
        private readonly BugOneTouchSettings _settings;

        // ─── 요청/응답 모델 ────────────────────────────────────────────────────────

        /// <summary>이슈 생성 요청 데이터</summary>
        public class CreateIssueRequest
        {
            /// <summary>Jira 프로젝트 키 (예: "PROJ")</summary>
            public string ProjectKey { get; set; }

            /// <summary>이슈 유형 이름 (기본값: "Bug")</summary>
            public string IssueType { get; set; } = "Bug";

            /// <summary>이슈 제목</summary>
            public string Summary { get; set; }

            /// <summary>이슈 설명 (일반 텍스트, 내부에서 ADF로 변환)</summary>
            public string Description { get; set; }

            /// <summary>추가 레이블 목록 (BugOneTouchSettings.defaultLabels가 자동 추가됨)</summary>
            public string[] AdditionalLabels { get; set; } = Array.Empty<string>();

            /// <summary>우선순위 이름 (예: "High", "Medium", "Low")</summary>
            public string Priority { get; set; } = "Medium";
        }

        /// <summary>이슈 생성 결과</summary>
        public class CreateIssueResult
        {
            /// <summary>생성된 이슈 키 (예: "PROJ-123")</summary>
            public string IssueKey { get; set; }

            /// <summary>이슈 self URL</summary>
            public string IssueUrl { get; set; }
        }

        // Jira API 응답 역직렬화용
        [Serializable]
        private class JiraCreateIssueResponse
        {
            public string id;
            public string key;
            public string self;
        }

        // ─── 생성자 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// JiraIssueCreator를 초기화합니다.
        /// </summary>
        /// <param name="apiClient">Jira API 클라이언트</param>
        /// <param name="settings">Bug OneTouch 설정 (defaultLabels 참조)</param>
        public JiraIssueCreator(JiraApiClient apiClient, BugOneTouchSettings settings)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // ─── 공개 메서드 ───────────────────────────────────────────────────────────

        /// <summary>
        /// Jira 이슈를 생성합니다.
        /// </summary>
        /// <param name="request">이슈 생성 요청 데이터</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>생성된 이슈 정보</returns>
        public async Task<CreateIssueResult> CreateAsync(
            CreateIssueRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateRequest(request);

            // 레이블 병합: 기본 레이블 + 추가 레이블
            var labels = MergeLabels(_settings.defaultLabels, request.AdditionalLabels);

            // ADF 본문 생성
            var adfDescription = AdfBuilder.CreateFromText(request.Description ?? "");

            // Jira 이슈 생성 JSON 빌드
            var requestJson = BuildCreateIssueJson(request, labels, adfDescription);

            Debug.Log($"[JiraIssueCreator] 이슈 생성 요청: {request.ProjectKey} / {request.Summary}");

            // API 호출
            var responseJson = await _apiClient.PostAsync("/issue", requestJson, cancellationToken);

            // 응답 파싱
            var response = JsonUtility.FromJson<JiraCreateIssueResponse>(responseJson);

            if (string.IsNullOrEmpty(response?.key))
                throw new InvalidOperationException($"Jira 이슈 생성 응답에 key가 없습니다. 응답: {responseJson}");

            Debug.Log($"[JiraIssueCreator] 이슈 생성 완료: {response.key} ({response.self})");

            return new CreateIssueResult
            {
                IssueKey = response.key,
                IssueUrl = response.self
            };
        }

        // ─── 내부 메서드 ───────────────────────────────────────────────────────────

        private static void ValidateRequest(CreateIssueRequest request)
        {
            if (string.IsNullOrEmpty(request.ProjectKey))
                throw new ArgumentException("ProjectKey는 필수입니다.", nameof(request));

            if (string.IsNullOrEmpty(request.Summary))
                throw new ArgumentException("Summary는 필수입니다.", nameof(request));

            if (request.Summary.Length > 255)
                throw new ArgumentException("Summary는 255자를 초과할 수 없습니다.", nameof(request));
        }

        private static string[] MergeLabels(string[] defaultLabels, string[] additionalLabels)
        {
            var result = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);

            if (defaultLabels != null)
                foreach (var label in defaultLabels)
                    if (!string.IsNullOrWhiteSpace(label))
                        result.Add(SanitizeLabel(label));

            if (additionalLabels != null)
                foreach (var label in additionalLabels)
                    if (!string.IsNullOrWhiteSpace(label))
                        result.Add(SanitizeLabel(label));

            var arr = new string[result.Count];
            result.CopyTo(arr);
            return arr;
        }

        /// <summary>Jira 레이블 형식에 맞게 정제합니다 (공백 → 하이픈, 소문자화).</summary>
        private static string SanitizeLabel(string label)
        {
            return label.Trim().Replace(' ', '-').ToLowerInvariant();
        }

        /// <summary>
        /// Jira 이슈 생성 API 요청 JSON을 빌드합니다.
        /// </summary>
        private static string BuildCreateIssueJson(
            CreateIssueRequest request,
            string[] labels,
            string adfDescriptionJson)
        {
            // 레이블 배열 JSON
            var labelsJson = BuildJsonArray(labels, l => $"\"{EscapeJson(l)}\"");

            var sb = new StringBuilder();
            sb.Append("{\"fields\":{");
            sb.Append($"\"project\":{{\"key\":\"{EscapeJson(request.ProjectKey)}\"}},");
            sb.Append($"\"issuetype\":{{\"name\":\"{EscapeJson(request.IssueType ?? "Bug")}\"}},");
            sb.Append($"\"summary\":\"{EscapeJson(request.Summary)}\",");
            sb.Append($"\"description\":{adfDescriptionJson},");
            sb.Append($"\"labels\":{labelsJson},");
            sb.Append($"\"priority\":{{\"name\":\"{EscapeJson(request.Priority ?? "Medium")}\"}}");
            sb.Append("}}");

            return sb.ToString();
        }

        private static string BuildJsonArray<T>(T[] items, Func<T, string> serializer)
        {
            if (items == null || items.Length == 0)
                return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(serializer(items[i]));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    // ─── ADF 빌더 유틸리티 ────────────────────────────────────────────────────────

    /// <summary>
    /// Atlassian Document Format(ADF) 빌더 유틸리티.
    /// Jira API v3에서 요구하는 구조화된 문서 형식을 생성합니다.
    /// </summary>
    public static class AdfBuilder
    {
        /// <summary>
        /// 일반 텍스트를 ADF JSON 문자열로 변환합니다.
        /// 빈 줄로 단락을 구분합니다.
        /// </summary>
        /// <param name="text">변환할 텍스트</param>
        /// <returns>ADF JSON 문자열</returns>
        public static string CreateFromText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return CreateEmpty();

            // 빈 줄로 단락 분리
            var paragraphs = text.Split(
                new[] { "\n\n", "\r\n\r\n" },
                StringSplitOptions.RemoveEmptyEntries);

            var sb = new StringBuilder();
            sb.Append("{\"version\":1,\"type\":\"doc\",\"content\":[");

            bool first = true;
            foreach (var paragraph in paragraphs)
            {
                if (!first) sb.Append(",");
                first = false;

                var trimmed = paragraph.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    AppendParagraph(sb, trimmed);
            }

            // 내용이 없으면 빈 단락 추가
            if (first)
                AppendEmptyParagraph(sb);

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// 제목 + 섹션 구조로 ADF JSON 문자열을 생성합니다.
        /// </summary>
        /// <param name="title">문서 제목</param>
        /// <param name="sections">섹션 이름 → 내용 쌍</param>
        /// <returns>ADF JSON 문자열</returns>
        public static string CreateWithSections(
            string title,
            System.Collections.Generic.Dictionary<string, string> sections)
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":1,\"type\":\"doc\",\"content\":[");

            bool first = true;

            // 제목 추가
            if (!string.IsNullOrEmpty(title))
            {
                AppendHeading(sb, title, 2);
                first = false;
            }

            // 섹션 추가
            if (sections != null)
            {
                foreach (var section in sections)
                {
                    if (!first) sb.Append(",");
                    first = false;

                    AppendHeading(sb, section.Key, 3);
                    sb.Append(",");
                    AppendParagraph(sb, section.Value);
                }
            }

            if (first)
                AppendEmptyParagraph(sb);

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// 빈 ADF 문서를 생성합니다.
        /// </summary>
        public static string CreateEmpty()
        {
            var sb = new StringBuilder();
            sb.Append("{\"version\":1,\"type\":\"doc\",\"content\":[");
            AppendEmptyParagraph(sb);
            sb.Append("]}");
            return sb.ToString();
        }

        // ─── 내부 헬퍼 메서드 ─────────────────────────────────────────────────────

        private static void AppendParagraph(StringBuilder sb, string text)
        {
            sb.Append("{\"type\":\"paragraph\",\"content\":[");
            sb.Append("{\"type\":\"text\",\"text\":\"");
            sb.Append(EscapeJsonString(text));
            sb.Append("\"}");
            sb.Append("]}");
        }

        private static void AppendEmptyParagraph(StringBuilder sb)
        {
            sb.Append("{\"type\":\"paragraph\",\"content\":[]}");
        }

        private static void AppendHeading(StringBuilder sb, string text, int level)
        {
            sb.Append($"{{\"type\":\"heading\",\"attrs\":{{\"level\":{level}}},\"content\":[");
            sb.Append("{\"type\":\"text\",\"text\":\"");
            sb.Append(EscapeJsonString(text));
            sb.Append("\"}");
            sb.Append("]}");
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            // 줄바꿈은 ADF 단락 내에서 하드브레이크로 처리
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n")
                .Replace("\t", "    ");
        }
    }
}
