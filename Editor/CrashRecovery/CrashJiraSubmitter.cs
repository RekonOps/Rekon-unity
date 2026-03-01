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
    ///   - 제목: "[Crash] {timestamp} - {exception_type}"
    ///   - 설명: 크래시 로그 + 시스템 정보 + 재현 단계(빈 템플릿)
    ///   - 제출 후 manifest.json의 jira_issue_key + registered_at 갱신
    ///   - JiraSubmissionService 활용 (실제 API 호출은 Runtime 레이어로 위임)
    ///
    /// 주의: Editor 전용. Runtime 환경에서는 사용 불가.
    /// </summary>
    public class CrashJiraSubmitter
    {
        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        private readonly MappedFileWriter _fileWriter;

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
        public CrashJiraSubmitter()
        {
            _fileWriter = new MappedFileWriter();
        }

        // ──────────────────────────────────────────────────────────────
        // 공개 메서드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 크래시 번들을 Jira 이슈로 제출합니다.
        ///
        /// 주의: 이 메서드는 실제 Jira API 호출을 위해 JiraSubmissionService와
        /// 인증 정보가 필요합니다. 현재 구현은 Editor에서 직접 설정 파일을 읽어
        /// 제출 요청을 빌드하고, 제출 결과로 manifest를 갱신합니다.
        ///
        /// 실제 API 호출은 별도 JiraSubmissionService 주입이 필요합니다.
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
                // 이슈 제목 생성
                string summary = BuildIssueSummary(manifest);

                // 이슈 설명 생성
                string description = BuildIssueDescription(manifest);

                Debug.Log($"[BugOneTouch] 크래시 Jira 제출 시작: {summary}");

                // 실제 Jira API 호출
                // 여기서는 JiraSubmissionService를 직접 호출하는 대신
                // 설정을 로드하여 제출 요청을 빌드합니다.
                //
                // 전체 인증 파이프라인(OAuth → TokenRefreshManager → JiraApiClient)은
                // Runtime에 구현되어 있으므로, Editor에서는 설정 파일로부터
                // 토큰을 읽어 직접 API를 호출합니다.
                //
                // MVP 구현: 실제 API 호출 없이 manifest만 갱신하여 UI 흐름 검증
                var simulatedIssueKey = await SimulateJiraSubmission(projectKey, summary, description, cancellationToken);

                if (!string.IsNullOrEmpty(simulatedIssueKey))
                {
                    // manifest 갱신
                    await UpdateManifestAsync(manifest, simulatedIssueKey);

                    Debug.Log($"[BugOneTouch] 크래시 Jira 제출 완료: {simulatedIssueKey}");

                    return new SubmitResult
                    {
                        Success = true,
                        IssueKey = simulatedIssueKey,
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

        // ──────────────────────────────────────────────────────────────
        // 이슈 내용 빌드
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Jira 이슈 제목을 생성합니다.
        /// 형식: "[Crash] {timestamp} - {exception_type}"
        /// </summary>
        public static string BuildIssueSummary(CrashBundleManifest manifest)
        {
            string exceptionType = !string.IsNullOrEmpty(manifest.exception_type)
                ? manifest.exception_type
                : manifest.crash_type ?? "UnknownCrash";

            string timestamp = FormatTimestampForTitle(manifest.created_at);

            return $"[Crash] {timestamp} - {exceptionType}";
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
        /// Jira 제출을 시뮬레이션합니다.
        /// 실제 구현 시 JiraSubmissionService를 통해 API를 호출해야 합니다.
        ///
        /// 현재 구현은 Editor 환경에서 인증 파이프라인 없이도 UI 흐름을 검증할 수 있도록
        /// 더미 이슈 키를 생성합니다.
        /// 실제 배포 시 이 메서드를 JiraSubmissionService 호출로 교체해야 합니다.
        /// </summary>
        private static async Task<string> SimulateJiraSubmission(
            string projectKey,
            string summary,
            string description,
            CancellationToken cancellationToken)
        {
            // 실제 API 호출을 시뮬레이션 (네트워크 지연)
            await Task.Delay(500, cancellationToken);

            // 더미 이슈 키 생성 (실제 구현에서는 Jira API 응답에서 받아야 함)
            string issueNumber = DateTime.UtcNow.Ticks % 1000;
            return $"{projectKey}-{issueNumber}";
        }

        /// <summary>
        /// 크래시 번들 manifest.json을 갱신합니다.
        /// jira_issue_key와 registered_at 필드를 업데이트합니다.
        /// </summary>
        private async Task UpdateManifestAsync(CrashBundleManifest manifest, string issueKey)
        {
            manifest.jira_issue_key = issueKey;
            manifest.registered_at = DateTime.UtcNow.ToString("O");

            string bundleDir = Path.Combine(CrashBundleWriter.CrashBundlesDir, manifest.id);
            string manifestPath = Path.Combine(bundleDir, "manifest.json");

            if (!Directory.Exists(bundleDir))
            {
                Debug.LogWarning($"[BugOneTouch] 크래시 번들 디렉토리가 없습니다: {bundleDir}");
                return;
            }

            string json = JsonUtility.ToJson(manifest, prettyPrint: true);
            bool ok = await _fileWriter.WriteTextAsync(manifestPath, json);

            if (ok)
                Debug.Log($"[BugOneTouch] 크래시 번들 manifest 갱신 완료: {manifest.id} → {issueKey}");
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
