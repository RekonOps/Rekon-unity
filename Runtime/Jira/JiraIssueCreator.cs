using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugBeacon
{
    /// <summary>
    /// Jira 이슈를 생성합니다.
    /// POST /rest/api/3/issue
    /// ADF(Atlassian Document Format)로 설명을 작성하고,
    /// AdditionalLabels로 지정된 레이블을 자동으로 추가합니다.
    ///
    /// ⚠️ JAM.dev 패턴 적용 (ADR-047):
    /// 이 클래스는 웹 대시보드(BugBeacon-web)의 push-to-jira API에서만 호출됩니다.
    /// Unity 플러그인(런타임)에서는 직접 호출하지 마세요.
    /// Unity → Web Backend → Jira 순서로만 동작해야 합니다.
    /// </summary>
    public class JiraIssueCreator
    {
        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private readonly JiraApiClient _apiClient;

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

            /// <summary>추가 레이블 목록</summary>
            public string[] AdditionalLabels { get; set; } = Array.Empty<string>();

            /// <summary>우선순위 이름 (예: "High", "Medium", "Low")</summary>
            public string Priority { get; set; } = "Medium";

            /// <summary>보고자 accountId</summary>
            public string ReporterAccountId { get; set; }

            /// <summary>담당자 accountId</summary>
            public string AssigneeAccountId { get; set; }

            /// <summary>스프린트 ID (숫자 문자열)</summary>
            public string SprintId { get; set; }

            /// <summary>상위 항목 이슈 키 (예: "PROJ-123")</summary>
            public string ParentKey { get; set; }

            /// <summary>
            /// R2에 업로드된 파일들의 공개 URL 목록.
            /// 키: 파일 유형 설명(예: "스크린샷", "로그", "영상"), 값: R2 공개 URL.
            /// null 또는 빈 사전이면 첨부파일 섹션을 추가하지 않습니다.
            /// </summary>
            public Dictionary<string, string> R2Urls { get; set; }
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
        public JiraIssueCreator(JiraApiClient apiClient)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
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

            // 레이블 병합: 추가 레이블만 사용 (기본 레이블은 웹 대시보드에서 관리됨, ADR-047)
            var labels = MergeLabels(request.AdditionalLabels);

            // R2 URL이 있으면 description에 첨부파일 섹션 추가
            string fullDescription = BuildDescriptionWithR2Links(request.Description, request.R2Urls);

            // ADF 본문 생성
            var adfDescription = AdfBuilder.CreateFromText(fullDescription);

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

        /// <summary>
        /// R2 URL이 있으면 description 끝에 첨부파일 섹션을 추가합니다.
        /// </summary>
        private static string BuildDescriptionWithR2Links(string description, Dictionary<string, string> r2Urls)
        {
            var desc = description ?? "";

            if (r2Urls == null || r2Urls.Count == 0)
                return desc;

            var sb = new StringBuilder(desc);
            if (sb.Length > 0)
                sb.Append("\n\n");

            sb.AppendLine("## 첨부파일");
            sb.AppendLine();

            foreach (var kvp in r2Urls)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    // URL 스킴 검증: http 또는 https만 허용
                    if (!Uri.TryCreate(kvp.Value, UriKind.Absolute, out var uri)
                        || (uri.Scheme != "https" && uri.Scheme != "http"))
                    {
                        Debug.LogWarning($"[JiraIssueCreator] 잘못된 R2 URL 스킴, 건너뜀: {kvp.Value}");
                        continue;
                    }

                    // 마크다운 링크 형식: [파일 유형](URL)
                    sb.AppendLine($"- [{kvp.Key}]({kvp.Value})");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static void ValidateRequest(CreateIssueRequest request)
        {
            if (string.IsNullOrEmpty(request.ProjectKey))
                throw new ArgumentException("ProjectKey는 필수입니다.", nameof(request));

            if (string.IsNullOrEmpty(request.Summary))
                throw new ArgumentException("Summary는 필수입니다.", nameof(request));

            if (request.Summary.Length > 255)
                throw new ArgumentException("Summary는 255자를 초과할 수 없습니다.", nameof(request));
        }

        /// <summary>
        /// 추가 레이블 배열을 중복 제거 후 반환합니다.
        /// 기본 레이블은 웹 대시보드(BugBeacon-web)에서 관리됩니다 (ADR-047).
        /// </summary>
        private static string[] MergeLabels(string[] additionalLabels)
        {
            var result = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);

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

            // reporter
            if (!string.IsNullOrEmpty(request.ReporterAccountId))
                sb.Append($",\"reporter\":{{\"accountId\":\"{EscapeJson(request.ReporterAccountId)}\"}}");

            // assignee
            if (!string.IsNullOrEmpty(request.AssigneeAccountId))
                sb.Append($",\"assignee\":{{\"accountId\":\"{EscapeJson(request.AssigneeAccountId)}\"}}");

            // sprint (customfield_10020이 일반적이나, 프로젝트마다 다를 수 있음)
            if (!string.IsNullOrEmpty(request.SprintId))
                sb.Append($",\"customfield_10020\":{request.SprintId}");

            // parent (에픽 등 상위 항목)
            if (!string.IsNullOrEmpty(request.ParentKey))
                sb.Append($",\"parent\":{{\"key\":\"{EscapeJson(request.ParentKey)}\"}}");

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
