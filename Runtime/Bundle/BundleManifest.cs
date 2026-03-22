using System;
using System.Collections.Generic;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 메타데이터 키-값 쌍. JsonUtility 직렬화를 위해 클래스로 정의.
    /// </summary>
    [Serializable]
    public class MetadataEntry
    {
        public string key;
        public string value;

        public MetadataEntry() { }

        public MetadataEntry(string key, string value)
        {
            this.key = key;
            this.value = value;
        }
    }

    /// <summary>
    /// Jira 연동 정보.
    /// </summary>
    [Serializable]
    public class JiraIntegrationInfo
    {
        /// <summary>Jira 연결 여부.</summary>
        public bool connected;

        /// <summary>Jira Cloud ID.</summary>
        public string cloudId = "";

        /// <summary>Jira 프로젝트 키 (예: BUG, PROJ).</summary>
        public string projectKey = "";

        /// <summary>생성된 Jira 이슈 키 (예: BUG-123). 제출 전 빈 문자열.</summary>
        public string issueKey = "";
    }

    /// <summary>
    /// 번들의 외부 시스템 연동 정보.
    /// </summary>
    [Serializable]
    public class BundleIntegrations
    {
        /// <summary>Jira 연동 정보.</summary>
        public JiraIntegrationInfo jira = new JiraIntegrationInfo();
    }

    /// <summary>
    /// 번들에 포함된 개별 아티팩트의 메타데이터.
    /// </summary>
    [Serializable]
    public class BundleArtifact
    {
        /// <summary>아티팩트 종류.</summary>
        public BundleArtifactType type;

        /// <summary>번들 디렉토리 내 파일명 또는 폴더명.</summary>
        public string file_name;

        /// <summary>파일 크기 (바이트). 디렉토리의 경우 하위 파일 총합.</summary>
        public long size_bytes;

        /// <summary>SHA-256 해시 (hex 문자열). 디렉토리는 빈 문자열.</summary>
        public string sha256_hash;
    }

    /// <summary>
    /// 아티팩트 종류 열거형.
    /// </summary>
    public enum BundleArtifactType
    {
        /// <summary>스크린샷 PNG 파일.</summary>
        Screenshot,

        /// <summary>로그 ZIP 파일.</summary>
        Log,

        /// <summary>상태 스냅샷 JSON 파일.</summary>
        State,

        /// <summary>영상 세그먼트 디렉토리.</summary>
        Video,
    }

    /// <summary>
    /// 번들 상태 열거형.
    /// Created → Pending → Submitting → Submitted 또는 Failed 순으로 전환됩니다.
    /// </summary>
    public enum BundleState
    {
        /// <summary>BundleWriter가 생성 직후의 초기 상태.</summary>
        Created,

        /// <summary>사용자 승인 후 제출 대기 중.</summary>
        Pending,

        /// <summary>Jira API 호출 진행 중.</summary>
        Submitting,

        /// <summary>Jira 등록 성공 완료.</summary>
        Submitted,

        /// <summary>제출 실패 (네트워크 오류 등).</summary>
        Failed,
    }

    /// <summary>
    /// 하나의 버그 번들에 대한 모든 메타데이터를 담는 데이터 모델.
    /// manifest.json으로 직렬화되어 번들 디렉토리에 저장됩니다.
    /// </summary>
    [Serializable]
    public class BundleManifest
    {
        /// <summary>번들 고유 식별자 (GUID).</summary>
        public string id;

        /// <summary>번들 생성 시각 (ISO 8601 UTC 형식).</summary>
        public string created_at;

        /// <summary>플러그인 버전.</summary>
        public string plugin_version;

        /// <summary>Unity 버전.</summary>
        public string unity_version;

        // ── PRD 필수 필드 (AC-17) ────────────────────────────────────────

        /// <summary>엔진 이름 (항상 "Unity").</summary>
        public string engine = "Unity";

        /// <summary>Unity 엔진 버전 (Application.unityVersion).</summary>
        public string engine_version;

        /// <summary>앱 버전 (Application.version).</summary>
        public string app_version;

        /// <summary>빌드 번호 (Application.buildGUID).</summary>
        public string build_number;

        /// <summary>실행 플랫폼 (Application.platform.ToString()).</summary>
        public string platform;

        /// <summary>디바이스 모델 (SystemInfo.deviceModel).</summary>
        public string device;

        /// <summary>운영체제 정보 (SystemInfo.operatingSystem).</summary>
        public string os;

        /// <summary>현재 씬 이름 (SceneManager.GetActiveScene().name).</summary>
        public string scene;

        /// <summary>재현 단계 (사용자 입력).</summary>
        public string repro_steps;

        /// <summary>예상 결과 (사용자 입력, nullable).</summary>
        public string expected;

        /// <summary>실제 결과 (사용자 입력, nullable).</summary>
        public string actual;

        /// <summary>심각도: "critical"/"major"/"minor"/"trivial". 기본값 "major".</summary>
        public string severity = "major";

        // ──────────────────────────────────────────────────────────────────

        /// <summary>버그 제목 (사용자 입력용, 초기값 빈 문자열).</summary>
        public string title;

        /// <summary>버그 설명 (사용자 입력용, 초기값 빈 문자열).</summary>
        public string description;

        /// <summary>번들에 포함된 아티팩트 목록.</summary>
        public List<BundleArtifact> artifacts;

        /// <summary>모든 아티팩트의 총 크기 (바이트).</summary>
        public long total_size_bytes;

        /// <summary>현재 번들 상태.</summary>
        public BundleState state;

        /// <summary>Jira 이슈 키 (예: BUG-123). 미등록 시 null.</summary>
        public string jira_issue_key;

        /// <summary>Jira 등록 완료 시각 (ISO 8601 UTC). 미등록 시 null.</summary>
        public string registered_at;

        /// <summary>
        /// 재시도 횟수. SubmissionQueue에서 관리합니다.
        /// </summary>
        public int retry_count;

        /// <summary>사용자 정의 메타데이터 (키-값 쌍). Silent Submit 시 환경 정보 저장용.</summary>
        public List<MetadataEntry> metadata = new List<MetadataEntry>();

        /// <summary>외부 시스템 연동 정보 (Jira 등).</summary>
        public BundleIntegrations integrations = new BundleIntegrations();

        /// <summary>
        /// 필수 필드가 모두 유효한지 검사합니다.
        /// </summary>
        /// <returns>유효하면 true, 그렇지 않으면 false.</returns>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(id)
                && !string.IsNullOrEmpty(created_at)
                && !string.IsNullOrEmpty(plugin_version)
                && !string.IsNullOrEmpty(unity_version)
                && artifacts != null;
        }

        /// <summary>
        /// 아티팩트 크기 합계를 계산하여 total_size_bytes 를 갱신합니다.
        /// </summary>
        public void RecalculateTotalSize()
        {
            total_size_bytes = 0L;
            if (artifacts == null) return;
            foreach (var artifact in artifacts)
                total_size_bytes += artifact.size_bytes;
        }

        public override string ToString()
        {
            return $"BundleManifest(id={id}, state={state}, artifacts={artifacts?.Count ?? 0}, total={total_size_bytes}B)";
        }
    }
}
