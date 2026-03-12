using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GaoZombie.BugOneTouch.Editor
{
    /// <summary>
    /// 크래시 번들을 Jira 이슈로 자동 제출하는 Editor 전용 클래스.
    ///
    /// 기능:
    ///   - 크래시 번들 → Jira 이슈 자동 채움
    ///   - 제목: "[Crash] {crash_type}: {crash_message 첫 50자}" (AC-23)
    ///   - 설명: 크래시 로그 + 시스템 정보 + 재현 단계(빈 템플릿)
    ///   - 제출 후 manifest.json의 jira_issue_key + registered_at + registered 갱신
    ///   - JiraSubmissionService 활용 (실제 API 호출은 Runtime 레이어로 위임)
    ///   - auto-crash-report 레이블 자동 추가 (AC-23)
    ///
    /// ⚠️ JAM.dev 패턴 적용 (ADR-047):
    /// 크래시 번들 파일은 R2에 저장되며, Jira description에 R2 URL 링크로 삽입됩니다.
    /// Jira 직접 첨부파일 업로드는 더 이상 지원되지 않습니다.
    ///
    /// 주의: Editor 전용. Runtime 환경에서는 사용 불가.
    /// </summary>
    public class CrashJiraSubmitter
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        /// <summary>크래시 번들 자동 제출 시 항상 추가되는 레이블 (AC-23)</summary>
        private const string AutoCrashReportLabel = "auto-crash-report";

        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        private readonly MappedFileWriter _fileWriter;
        private readonly JiraSubmissionService _submissionService;

        // ──────────────────────────────────────────────────────────────
        // 제출 결과 모델
        // ──────────────────────────────────────────────────────────────

        /// <summary>크래시 Jira 제출 결과</summary>
        public class SubmitResult
        {
            /// <summary>제출 성공 여부</summary>
            public bool Success { get; set; }

            /// <summary>생성된 이슈 키 (예: "PROJ-123")</summary>
            public string IssueKey { get; set; }

            /// <summary>오류 메시지 (실패 시)</summary>
            public string ErrorMessage { get; set; }
        }

        // ──────────────────────────────────────────────────────────────
        // 생성자
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// CrashJiraSubmitter를 초기화합니다.
        /// </summary>
        /// <param name="submissionService">실제 Jira API 호출에 사용할 서비스 (null이면 API 미연동 모드)</param>
        public CrashJiraSubmitter(JiraSubmissionService submissionService = null)
        {
            _fileWriter = new MappedFileWriter();
            _submissionService = submissionService;
        }

        // ──────────────────────────────────────────────────────────────
        // 공개 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 크래시 번들을 Jira 이슈로 제출합니다.
        ///
        /// JiraSubmissionService가 주입된 경우 실제 Jira API를 호출하여:
        ///   1. JiraIssueCreator로 이슈 생성 (R2 URL 있으면 description에 링크 자동 삽입)
        ///   2. 성공 시 manifest에 jira_issue_key + registered_at + registered=true 갱신
        ///
        /// JiraSubmissionService가 없는 경우(null) 시뮬레이션 모드로 동작합니다.
        /// </summary>
        /// <param name="manifest">제출할 크래시 번들 매니페스트</param>
        /// <param name="projectKey">Jira 프로젝트 키</param>
        /// <param name="cancellationToken">취소 토큰</param>
        /// <returns>제출 결과</returns>
        public async Task<SubmitResult> SubmitAsync(
            CrashBundleManifest manifest,
            string projectKey,
            CancellationToken cancellationToken = default)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            if (string.IsNullOrEmpty(projectKey))
                throw new ArgumentException("프로젝트 키는 필수입니다.", nameof(projectKey));

            try
            {
                // 이슈 제목 생성 (AC-23: "[Crash] {crash_type}: {crash_message 첫 50자}" 형식)
                string summary = BuildIssueSummary(manifest);

                // 이슈 설명 생성
                string description = BuildIssueDescription(manifest);

                Debug.Log($"[BugOneTouch] 크래시 Jira 제출 시작: {summary}");

                string issueKey;

                if (_submissionService != null)
                {
                    // 실제 Jira API 호출 (Critical 3 구현)
                    issueKey = await SubmitViaServiceAsync(
                        manifest, projectKey, summary, description, cancellationToken);
                }
                else
                {
                    // 시뮬레이션 모드 (JiraSubmissionService 미주입 시 폴백)
                    Debug.LogWarning("[BugOneTouch] JiraSubmissionService가 주입되지 않아 시뮬레이션 모드로 동작합니다.");
                    issueKey = await SimulateJiraSubmission(projectKey, summary, description, cancellationToken);
                }

                if (!string.IsNullOrEmpty(issueKey))
                {
                    // manifest 갱신 (jira_issue_key + registered_at + registered=true)
                    await UpdateManifestAsync(manifest, issueKey);

                    Debug.Log($"[BugOneTouch] 크래시 Jira 제출 완료: {issueKey}");

                    return new SubmitResult
                    {
                        Success = true,
                        IssueKey = issueKey,
                    };
                }

                return new SubmitResult
                {
                    Success = false,
                    ErrorMessage = "이슈 키를 받지 못했습니다.",
                };
            }
            catch (OperationCanceledException)
            {
                return new SubmitResult
                {
                    Success = false,
                    ErrorMessage = "제출이 취소되었습니다.",
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugOneTouch] 크래시 Jira 제출 실패: {ex.Message}");
                return new SubmitResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                };
            }
        }

        /// <summary>
        /// JiraSubmissionService를 통해 실제 Jira API로 크래시 이슈를 제출합니다.
        ///   1. JiraIssueCreator로 이슈 생성 (R2 URL이 있으면 description에 링크 자동 삽입)
        ///
        /// ⚠️ JAM.dev 패턴 (ADR-047): Jira 직접 첨부파일 업로드 대신 R2 URL 링크를 사용합니다.
        /// </summary>
        private async Task<string> SubmitViaServiceAsync(
            CrashBundleManifest manifest,
            string projectKey,
            string summary,
            string description,
            CancellationToken cancellationToken)
        {
            var submissionRequest = new JiraSubmissionService.SubmissionRequest
            {
                BundleId = manifest.id,
                IssueRequest = new JiraIssueCreator.CreateIssueRequest
                {
                    ProjectKey = projectKey,
                    IssueType  = "Bug",
                    Summary    = summary,
                    Description = description,
                    // AC-23: auto-crash-report 레이블을 기본으로 추가
                    AdditionalLabels = new[] { AutoCrashReportLabel },
                    Priority   = "High",
                    // R2Urls는 크래시 번들의 경우 현재 미지원 (R2 업로드 파이프라인 미연결)
                    // TODO: CrashBundleWriter에서 R2 업로드 연동 후 R2Urls 설정
                },
            };

            var result = await _submissionService.SubmitAsync(submissionRequest, cancellationToken);

            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "Jira 이슈 생성에 실패했습니다.");

            return result.IssueKey;
        }

        // ──────────────────────────────────────────────────────────────
        // 이슈 내용 빌드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Jira 이슈 제목을 생성합니다.
        /// 형식: "[Crash] {crash_type}: {crash_message 첫 50자}" (AC-23)
        /// </summary>
        public static string BuildIssueSummary(CrashBundleManifest manifest)
        {
            // crash_type 결정: exception_type 우선, 없으면 crash_type 사용
            string crashType = !string.IsNullOrEmpty(manifest.exception_type)
                ? manifest.exception_type
                : manifest.crash_type ?? "UnknownCrash";

            // crash_message 첫 50자 추출 (AC-23)
            string crashMessage = manifest.exception_message ?? string.Empty;
            if (crashMessage.Length > 50)
                crashMessage = crashMessage.Substring(0, 50);

            // 줄바꿈 문자를 공백으로 대체하여 제목에서 사용 가능하게 처리
            crashMessage = crashMessage.Replace('\n', ' ').Replace('\r', ' ').Trim();

            if (string.IsNullOrEmpty(crashMessage))
                return $"[Crash] {crashType}";

            return $"[Crash] {crashType}: {crashMessage}";
        }

        /// <summary>
        /// Jira 이슈 설명을 생성합니다.
        /// 크래시 로그 + 시스템 정보 + 재현 단계(빈 템플릿) 포함.
        /// </summary>
        public static string BuildIssueDescription(CrashBundleManifest manifest)
        {
            var sb = new StringBuilder();

            // 크래시 개요
            sb.AppendLine("## 크래시 개요");
            sb.AppendLine($"- **발생 시각**: {manifest.created_at}");
            sb.AppendLine($"- **크래시 유형**: {manifest.crash_type}");
            sb.AppendLine($"- **예외 타입**: {manifest.exception_type ?? "-"}");
            sb.AppendLine($"- **Unity 버전**: {manifest.unity_version}");
            sb.AppendLine();

            // 예외 메시지
            if (!string.IsNullOrEmpty(manifest.exception_message))
            {
                sb.AppendLine("## 예외 메시지");
                sb.AppendLine("```");
                sb.AppendLine(manifest.exception_message);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            // 스택 트레이스
            if (!string.IsNullOrEmpty(manifest.stack_trace))
            {
                sb.AppendLine("## 스택 트레이스");
                sb.AppendLine("```");
                sb.AppendLine(manifest.stack_trace);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            // 데이터 무결성
            if (manifest.data_integrity != null)
            {
                sb.AppendLine("## 첨부 데이터 무결성");
                sb.AppendLine($"- 로그: {(manifest.data_integrity.logs_ok ? "✅" : "❌")}");
                sb.AppendLine($"- 상태: {(manifest.data_integrity.state_ok ? "✅" : "❌")}");
                sb.AppendLine($"- 영상: {(manifest.data_integrity.video_ok ? "✅" : "❌")}");
                sb.AppendLine($"- 전체 상태: {manifest.data_integrity.overall}");
                sb.AppendLine();
            }

            // 재현 단계 템플릿 (빈 상태로 제공)
            sb.AppendLine("## 재현 단계");
            sb.AppendLine("1. ");
            sb.AppendLine("2. ");
            sb.AppendLine("3. ");
            sb.AppendLine();

            sb.AppendLine("## 예상 결과");
            sb.AppendLine("(여기에 입력하세요)");
            sb.AppendLine();

            sb.AppendLine("## 실제 결과");
            sb.AppendLine("크래시 발생");

            return sb.ToString();
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 구현
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Jira 제출을 시뮬레이션합니다 (JiraSubmissionService 미주입 시 폴백).
        /// Editor 환경에서 인증 파이프라인 없이도 UI 흐름을 검증할 수 있도록 더미 이슈 키를 생성합니다.
        /// </summary>
        private static async Task<string> SimulateJiraSubmission(
            string projectKey,
            string summary,
            string description,
            CancellationToken cancellationToken)
        {
            // 실제 API 호출을 시뮬레이션 (네트워크 지연)
            await Task.Delay(500, cancellationToken);

            // 더미 이슈 키 생성 (실제 구현에서는 Jira API 응답에서 받음)
            long issueNumber = DateTime.UtcNow.Ticks % 1000;
            return $"{projectKey}-{issueNumber}";
        }

        /// <summary>
        /// 크래시 번들 manifest.json을 갱신합니다.
        /// jira_issue_key, registered_at, registered 필드를 업데이트합니다 (AC-20/24).
        /// </summary>
        private async Task UpdateManifestAsync(CrashBundleManifest manifest, string issueKey)
        {
            manifest.jira_issue_key = issueKey;
            manifest.registered_at  = DateTime.UtcNow.ToString("O");
            manifest.registered     = true; // Jira 등록 완료 표시 (AC-20/24)

            string bundleDir    = Path.Combine(CrashBundleWriter.CrashBundlesDir, manifest.id);
            string manifestPath = Path.Combine(bundleDir, "manifest.json");

            if (!Directory.Exists(bundleDir))
            {
                Debug.LogWarning($"[BugOneTouch] 크래시 번들 디렉토리가 없습니다: {bundleDir}");
                return;
            }

            string json = JsonUtility.ToJson(manifest, prettyPrint: true);
            bool ok = await _fileWriter.WriteTextAsync(manifestPath, json);

            if (ok)
                Debug.Log($"[BugOneTouch] 크래시 번들 manifest 갱신 완료: {manifest.id} → {issueKey} (registered=true)");
            else
                Debug.LogWarning($"[BugOneTouch] 크래시 번들 manifest 갱신 실패: {manifest.id}");
        }

        // ──────────────────────────────────────────────────────────────
        // 유틸리티
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// ISO 8601 타임스탬프를 제목용 형식으로 변환합니다.
        /// </summary>
        private static string FormatTimestampForTitle(string isoTimestamp)
        {
            if (string.IsNullOrEmpty(isoTimestamp))
                return "Unknown";

            if (DateTime.TryParse(isoTimestamp, null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out DateTime dt))
            {
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }

            return isoTimestamp;
        }
    }
}
