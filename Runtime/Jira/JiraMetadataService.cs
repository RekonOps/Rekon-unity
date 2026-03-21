using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace RekonOps.Rekon
{
    // ─── 공개 데이터 모델 ─────────────────────────────────────────────────────────

    /// <summary>Jira 프로젝트 정보</summary>
    [Serializable]
    public class JiraProject
    {
        public string id;
        public string key;
        public string name;
    }

    /// <summary>Jira 이슈 타입 정보</summary>
    [Serializable]
    public class JiraIssueTypeInfo
    {
        public string id;
        public string name;
        public string description;
    }

    /// <summary>Jira 필드의 허용 값 (allowedValues 배열 항목)</summary>
    [Serializable]
    public class JiraFieldAllowedValue
    {
        public string id;
        public string name;
        public string value;  // 일부 필드는 "value" 키 사용
    }

    /// <summary>Jira 이슈 생성 필드 정보</summary>
    [Serializable]
    public class JiraFieldInfo
    {
        public string fieldId;
        public string name;
        public bool required;
        public string schemaType;  // "string", "array", "option", "priority" 등
        public JiraFieldAllowedValue[] allowedValues;
    }

    /// <summary>Jira 사용자 정보</summary>
    [Serializable]
    public class JiraUser
    {
        public string accountId;
        public string displayName;
        public string emailAddress;
    }

    /// <summary>Jira 보드 정보</summary>
    [Serializable]
    public class JiraBoard
    {
        public int id;
        public string name;
    }

    /// <summary>Jira 스프린트 정보</summary>
    [Serializable]
    public class JiraSprint
    {
        public int id;
        public string name;
        public string state; // "active", "future", "closed"
    }

    /// <summary>Jira 이슈 요약 정보</summary>
    [Serializable]
    public class JiraIssueSummary
    {
        public string id;
        public string key;
        public JiraIssueSummaryFields fields;
    }

    /// <summary>Jira 이슈 요약 필드</summary>
    [Serializable]
    public class JiraIssueSummaryFields
    {
        public string summary;
    }

    // ─── 서비스 클래스 ────────────────────────────────────────────────────────────

    /// <summary>
    /// JiraApiClient를 사용하여 Jira 프로젝트/이슈타입/필드 메타데이터를 동적으로 조회하는 서비스.
    /// </summary>
    public class JiraMetadataService
    {
        private readonly JiraApiClient _apiClient;

        /// <summary>
        /// JiraMetadataService를 초기화합니다.
        /// </summary>
        /// <param name="apiClient">Jira REST API 클라이언트</param>
        public JiraMetadataService(JiraApiClient apiClient)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        // ─── 공개 메서드 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 접근 가능한 Jira 프로젝트 목록을 조회합니다.
        /// </summary>
        /// <param name="ct">취소 토큰</param>
        /// <returns>프로젝트 배열. 조회 실패 시 빈 배열.</returns>
        public async Task<JiraProject[]> GetProjectsAsync(CancellationToken ct = default)
        {
            try
            {
                string json = await _apiClient.GetAsync("/project/search?maxResults=50", ct);
                Debug.Log($"[Rekon] 프로젝트 API 원문 응답 (처음 500자): {(json.Length > 500 ? json.Substring(0, 500) : json)}");
                var response = JsonUtility.FromJson<ProjectSearchResponse>(json);
                return response?.GetItems() ?? Array.Empty<JiraProject>();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 프로젝트 목록 조회 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 특정 프로젝트의 이슈 타입 목록을 조회합니다.
        /// </summary>
        /// <param name="projectKey">Jira 프로젝트 키 (예: "PROJ")</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>이슈 타입 배열. 조회 실패 시 빈 배열.</returns>
        public async Task<JiraIssueTypeInfo[]> GetIssueTypesAsync(string projectKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(projectKey))
                throw new ArgumentException("projectKey는 필수입니다.", nameof(projectKey));

            try
            {
                string json = await _apiClient.GetAsync($"/issue/createmeta/{projectKey}/issuetypes", ct);
                Debug.Log($"[Rekon] 이슈 타입 API 원문 응답 (처음 500자): {(json.Length > 500 ? json.Substring(0, 500) : json)}");
                var response = JsonUtility.FromJson<IssueTypesResponse>(json);
                return response?.GetItems() ?? Array.Empty<JiraIssueTypeInfo>();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 이슈 타입 목록 조회 실패 (프로젝트: {projectKey}): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 특정 프로젝트/이슈타입의 필드 목록을 조회합니다.
        /// </summary>
        /// <param name="projectKey">Jira 프로젝트 키 (예: "PROJ")</param>
        /// <param name="issueTypeId">이슈 타입 ID</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>필드 정보 배열. 조회 실패 시 빈 배열.</returns>
        public async Task<JiraFieldInfo[]> GetFieldsAsync(
            string projectKey,
            string issueTypeId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(projectKey))
                throw new ArgumentException("projectKey는 필수입니다.", nameof(projectKey));
            if (string.IsNullOrEmpty(issueTypeId))
                throw new ArgumentException("issueTypeId는 필수입니다.", nameof(issueTypeId));

            try
            {
                string endpoint = $"/issue/createmeta/{projectKey}/issuetypes/{issueTypeId}";
                Debug.Log($"[Rekon] 필드 API 호출: GET {endpoint}");
                string json = await _apiClient.GetAsync(endpoint, ct);
                Debug.Log($"[Rekon] 필드 API 응답 (첫 500자): {(json?.Length > 500 ? json.Substring(0, 500) + "..." : json)}");

                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogError("[Rekon] 필드 API 응답이 비어 있습니다.");
                    throw new InvalidOperationException("API 응답이 비어 있습니다.");
                }

                FieldsResponse response;
                try
                {
                    response = JsonUtility.FromJson<FieldsResponse>(json);
                }
                catch (Exception parseEx)
                {
                    Debug.LogError($"[Rekon] 필드 JSON 파싱 실패: {parseEx.Message}\n응답 내용: {json}");
                    throw new InvalidOperationException($"필드 JSON 파싱 실패: {parseEx.Message}", parseEx);
                }

                var items = response?.GetItems();
                if (items == null || items.Length == 0)
                {
                    Debug.LogWarning($"[Rekon] 필드 응답의 'fields'/'values' 필드가 null이거나 비어 있습니다. JSON: {json}");
                    return Array.Empty<JiraFieldInfo>();
                }

                // FieldRaw → JiraFieldInfo 변환
                var result = new JiraFieldInfo[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    var raw = items[i];
                    result[i] = new JiraFieldInfo
                    {
                        fieldId = raw.fieldId,
                        name = raw.name,
                        required = raw.required,
                        schemaType = raw.schema?.type ?? "string",
                        allowedValues = raw.allowedValues ?? Array.Empty<JiraFieldAllowedValue>(),
                    };
                }
                Debug.Log($"[Rekon] 필드 파싱 완료: {items.Length}개 필드");
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[Rekon] 필드 목록 조회 실패 (프로젝트: {projectKey}, 이슈타입: {issueTypeId}): {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 현재 인증된 사용자 정보를 조회합니다.
        /// </summary>
        public async Task<JiraUser> GetMyselfAsync(CancellationToken ct = default)
        {
            try
            {
                string json = await _apiClient.GetAsync("/myself", ct);
                Debug.Log($"[Rekon] /myself 응답 (처음 200자): {(json.Length > 200 ? json.Substring(0, 200) : json)}");
                var user = JsonUtility.FromJson<JiraUser>(json);
                return user;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 현재 사용자 조회 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 프로젝트에 할당 가능한 사용자 목록을 조회합니다.
        /// </summary>
        public async Task<JiraUser[]> GetAssignableUsersAsync(string projectKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(projectKey))
                throw new ArgumentException("projectKey는 필수입니다.", nameof(projectKey));

            try
            {
                string json = await _apiClient.GetAsync(
                    $"/user/assignable/search?project={projectKey}&maxResults=100", ct);
                Debug.Log($"[Rekon] assignable users 응답 (처음 300자): {(json.Length > 300 ? json.Substring(0, 300) : json)}");

                // 최상위 배열 → 래퍼로 변환
                if (json.TrimStart().StartsWith("["))
                    json = "{\"users\":" + json + "}";

                var wrapper = JsonUtility.FromJson<UserArrayWrapper>(json);
                return wrapper?.users ?? Array.Empty<JiraUser>();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"[Rekon] 할당 가능 사용자 조회 실패: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 프로젝트의 보드 목록을 조회합니다 (Agile API).
        /// </summary>
        public async Task<JiraBoard[]> GetBoardsAsync(string projectKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(projectKey))
                throw new ArgumentException("projectKey는 필수입니다.", nameof(projectKey));

            try
            {
                // Agile API - /rest/agile/ 로 시작하므로 BuildUrl이 절대 경로로 처리
                string json = await _apiClient.GetAsync(
                    $"/rest/agile/1.0/board?projectKeyOrId={projectKey}&maxResults=50", ct);
                Debug.Log($"[Rekon] boards 응답 (처음 300자): {(json.Length > 300 ? json.Substring(0, 300) : json)}");

                var response = JsonUtility.FromJson<BoardSearchResponse>(json);
                return response?.GetItems() ?? Array.Empty<JiraBoard>();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 401 에러는 Agile API OAuth scope 누락을 의미합니다.
                // Jira 앱 설정에서 'read:board-scope:jira-software' scope를 추가해야 합니다.
                if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                {
                    Debug.LogWarning(
                        "[Rekon] Agile API 접근 권한이 없습니다. " +
                        "Jira 앱 설정에서 'read:board-scope:jira-software' scope를 추가하세요. " +
                        "(connect-jira-start의 JIRA_SCOPES 및 connect-jira-callback의 scopes 배열도 함께 업데이트 필요) " +
                        $"원본 에러: {ex.Message}");
                }
                else
                {
                    Debug.LogWarning($"[Rekon] 보드 조회 실패 (스프린트 조회 불가): {ex.Message}");
                }
                return Array.Empty<JiraBoard>();
            }
        }

        /// <summary>
        /// 보드의 스프린트 목록을 조회합니다 (활성/미래만).
        /// </summary>
        public async Task<JiraSprint[]> GetSprintsAsync(int boardId, CancellationToken ct = default)
        {
            try
            {
                string json = await _apiClient.GetAsync(
                    $"/rest/agile/1.0/board/{boardId}/sprint?state=active,future&maxResults=50", ct);
                Debug.Log($"[Rekon] sprints 응답 (처음 300자): {(json.Length > 300 ? json.Substring(0, 300) : json)}");

                var response = JsonUtility.FromJson<SprintSearchResponse>(json);
                return response?.GetItems() ?? Array.Empty<JiraSprint>();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 401 에러는 Agile API OAuth scope 누락을 의미합니다.
                // Jira 앱 설정에서 'read:sprint:jira-software' scope를 추가해야 합니다.
                if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                {
                    Debug.LogWarning(
                        "[Rekon] Agile API 접근 권한이 없습니다. " +
                        "Jira 앱 설정에서 'read:sprint:jira-software' scope를 추가하세요. " +
                        "(connect-jira-start의 JIRA_SCOPES 및 connect-jira-callback의 scopes 배열도 함께 업데이트 필요) " +
                        $"원본 에러: {ex.Message}");
                }
                else
                {
                    Debug.LogWarning($"[Rekon] 스프린트 조회 실패: {ex.Message}");
                }
                return Array.Empty<JiraSprint>();
            }
        }

        /// <summary>
        /// Jira 서버 설정에서 첨부 파일 최대 크기(바이트)를 조회합니다.
        /// GET /rest/api/3/configuration API를 시도하고, 실패 시 기본값(250MB)을 반환합니다.
        /// </summary>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>첨부 파일 최대 크기(바이트). 조회 실패 시 262144000(250MB) 반환.</returns>
        public async Task<long> GetAttachmentSizeLimitBytesAsync(CancellationToken cancellationToken = default)
        {
            // Jira Cloud 기본값: 250MB
            const long defaultLimitBytes = 250L * 1024L * 1024L; // 262144000

            try
            {
                string json = await _apiClient.GetAsync("/configuration", cancellationToken);
                Debug.Log($"[Rekon] /configuration 응답 (처음 300자): {(json.Length > 300 ? json.Substring(0, 300) : json)}");
                var config = JsonUtility.FromJson<JiraConfigurationResponse>(json);
                if (config != null && config.attachmentSize > 0)
                {
                    Debug.Log($"[Rekon] 첨부파일 크기 제한 조회 성공: {config.attachmentSize} 바이트");
                    return config.attachmentSize;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] /configuration 조회 실패: {ex.Message}. 기본값({defaultLimitBytes} 바이트)을 사용합니다.");
            }

            // 기본값 반환 (Jira Cloud 250MB)
            return defaultLimitBytes;
        }

        /// <summary>
        /// Jira 서버 정보를 조회합니다.
        /// GET /rest/api/3/serverInfo
        /// baseUrl 필드에 실제 Jira 사이트 URL(예: https://yourcompany.atlassian.net)이 포함됩니다.
        /// </summary>
        /// <param name="ct">취소 토큰</param>
        /// <returns>서버 정보. 조회 실패 시 null.</returns>
        public async Task<JiraServerInfo> GetServerInfoAsync(CancellationToken ct = default)
        {
            try
            {
                string json = await _apiClient.GetAsync("/serverInfo", ct);
                Debug.Log($"[Rekon] /serverInfo 응답 (처음 300자): {(json.Length > 300 ? json.Substring(0, 300) : json)}");
                return JsonUtility.FromJson<JiraServerInfo>(json);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] /serverInfo 조회 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// JQL로 이슈를 검색합니다 (에픽, 연결 이슈용).
        /// </summary>
        public async Task<JiraIssueSummary[]> SearchIssuesAsync(
            string projectKey, string issueType = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(projectKey))
                throw new ArgumentException("projectKey는 필수입니다.", nameof(projectKey));

            try
            {
                string jql = $"project={projectKey}";
                if (!string.IsNullOrEmpty(issueType))
                    jql += $" AND issuetype=\"{issueType}\"";
                jql += " ORDER BY updated DESC";

                string encodedJql = UnityWebRequest.EscapeURL(jql);
                string json = await _apiClient.GetAsync(
                    $"/rest/api/3/search/jql?jql={encodedJql}&fields=summary&maxResults=100", ct);
                Debug.Log($"[Rekon] search 응답 (처음 300자): {(json.Length > 300 ? json.Substring(0, 300) : json)}");

                var response = JsonUtility.FromJson<IssueSearchResponse>(json);
                return response?.GetItems() ?? Array.Empty<JiraIssueSummary>();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] 이슈 검색 실패: {ex.Message}");
                return Array.Empty<JiraIssueSummary>();
            }
        }

        /// <summary>
        /// 프로젝트의 에픽 목록을 검색합니다.
        /// </summary>
        public async Task<JiraIssueSummary[]> SearchEpicsAsync(string projectKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(projectKey))
                throw new ArgumentException("projectKey는 필수입니다.", nameof(projectKey));

            try
            {
                string jql = UnityWebRequest.EscapeURL($"project={projectKey} AND issuetype=Epic ORDER BY created DESC");
                string json = await _apiClient.GetAsync(
                    $"/rest/api/3/search?jql={jql}&fields=summary&maxResults=50", ct);
                Debug.Log($"[Rekon] SearchEpicsAsync 응답 (처음 300자): {(json.Length > 300 ? json.Substring(0, 300) : json)}");

                var response = JsonUtility.FromJson<IssueSearchResponse>(json);
                return response?.GetItems() ?? Array.Empty<JiraIssueSummary>();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Rekon] 에픽 검색 실패 (프로젝트: {projectKey}): {ex.Message}");
                return Array.Empty<JiraIssueSummary>();
            }
        }

        // ─── JSON 파싱용 private 래퍼 클래스 ─────────────────────────────────────
        // JsonUtility.FromJson은 최상위 배열을 직접 파싱하지 못하므로 래퍼 클래스 필수.
        // private 클래스에도 [Serializable] 필수 (JsonUtility 요구사항).

        /// <summary>GET /rest/api/3/project/search 응답 래퍼</summary>
        [Serializable]
        private class ProjectSearchResponse
        {
            public JiraProject[] values;
            public JiraProject[] projects;  // 대체 키

            public JiraProject[] GetItems()
            {
                if (values != null && values.Length > 0) return values;
                if (projects != null && projects.Length > 0) return projects;
                return Array.Empty<JiraProject>();
            }
        }

        /// <summary>GET /rest/api/3/issue/createmeta/{projectKey}/issuetypes 응답 래퍼</summary>
        [Serializable]
        private class IssueTypesResponse
        {
            public JiraIssueTypeInfo[] values;
            public JiraIssueTypeInfo[] issueTypes;  // 대체 키

            public JiraIssueTypeInfo[] GetItems()
            {
                if (values != null && values.Length > 0) return values;
                if (issueTypes != null && issueTypes.Length > 0) return issueTypes;
                return Array.Empty<JiraIssueTypeInfo>();
            }
        }

        /// <summary>GET /rest/api/3/issue/createmeta/{projectKey}/issuetypes/{issueTypeId} 응답 래퍼</summary>
        [Serializable]
        private class FieldsResponse
        {
            public FieldRaw[] fields;
            public FieldRaw[] values;  // 대체 키

            public FieldRaw[] GetItems()
            {
                if (fields != null && fields.Length > 0) return fields;
                if (values != null && values.Length > 0) return values;
                return Array.Empty<FieldRaw>();
            }
        }

        /// <summary>필드 메타데이터 원시 응답 항목 (schema 중첩 객체 포함)</summary>
        [Serializable]
        private class FieldRaw
        {
            public string fieldId;
            public string name;
            public bool required;
            public SchemaRaw schema;
            public JiraFieldAllowedValue[] allowedValues;
        }

        /// <summary>필드 schema 중첩 객체</summary>
        [Serializable]
        private class SchemaRaw
        {
            public string type;    // "string", "array", "option", "priority" 등
            public string items;   // type이 "array"일 때 배열 아이템 타입
            public string system;  // 시스템 필드 식별자
        }

        /// <summary>GET /rest/agile/1.0/board 응답 래퍼</summary>
        [Serializable]
        private class BoardSearchResponse
        {
            public JiraBoard[] values;

            public JiraBoard[] GetItems()
            {
                return values ?? Array.Empty<JiraBoard>();
            }
        }

        /// <summary>GET /rest/agile/1.0/board/{boardId}/sprint 응답 래퍼</summary>
        [Serializable]
        private class SprintSearchResponse
        {
            public JiraSprint[] values;

            public JiraSprint[] GetItems()
            {
                return values ?? Array.Empty<JiraSprint>();
            }
        }

        /// <summary>GET /rest/api/3/search 응답 래퍼</summary>
        [Serializable]
        private class IssueSearchResponse
        {
            public JiraIssueSummary[] issues;

            public JiraIssueSummary[] GetItems()
            {
                return issues ?? Array.Empty<JiraIssueSummary>();
            }
        }

        /// <summary>최상위 배열 응답을 래핑하기 위한 JiraUser 배열 래퍼</summary>
        [Serializable]
        private class UserArrayWrapper
        {
            public JiraUser[] users;
        }

        /// <summary>GET /rest/api/3/configuration 응답 래퍼</summary>
        [Serializable]
        private class JiraConfigurationResponse
        {
            /// <summary>첨부 파일 최대 크기 (바이트)</summary>
            public long attachmentSize;
        }
    }

    /// <summary>
    /// GET /rest/api/3/serverInfo 응답 모델.
    /// baseUrl에 실제 Jira 사이트 URL이 포함됩니다.
    /// </summary>
    [Serializable]
    public class JiraServerInfo
    {
        /// <summary>Jira 사이트 기본 URL (예: https://yourcompany.atlassian.net)</summary>
        public string baseUrl;

        /// <summary>Jira 버전 문자열</summary>
        public string version;

        /// <summary>서버 제목 (사이트 이름)</summary>
        public string serverTitle;
    }
}
