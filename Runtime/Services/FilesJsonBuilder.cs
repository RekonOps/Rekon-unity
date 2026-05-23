using System.Collections.Generic;
using System.Text;

namespace RekonOps.Rekon
{
    /// <summary>
    /// create-report 요청의 files JSON 배열을 빌드하는 공개 헬퍼.
    ///
    /// ReportSubmitService.CallCreateReportAsync 내부 로직을 분리하여
    /// 단위 테스트에서 직접 검증 가능하도록 합니다.
    ///
    /// team_pro + 스크린샷 + CapturedTAbs 있음 → "captured_t_abs" 필드 포함.
    /// free/team, 비스크린샷, CapturedTAbs = null → 미포함.
    /// </summary>
    public static class FilesJsonBuilder
    {
        /// <summary>
        /// FileAttachment 목록을 files JSON 배열 문자열로 직렬화합니다.
        /// </summary>
        /// <param name="files">첨부 파일 목록</param>
        /// <param name="isTeamPro">team_pro 플랜 여부 (false 이면 captured_t_abs 미포함)</param>
        /// <returns>JSON 배열 문자열 (예: [{"type":"screenshot","filename":"...","file_size":123}])</returns>
        public static string Build(IList<FileAttachment> files, bool isTeamPro)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"type\":\"{EscapeJson(file.FileType)}\",");
                sb.Append($"\"filename\":\"{EscapeJson(file.FileName)}\",");
                sb.Append($"\"file_size\":{(file.Data != null ? file.Data.Length : 0)}");

                // team_pro 전용: 스크린샷 캡처 시각(realtimeSinceStartup) → captured_t_abs
                // CapturedTAbs 가 있을 때만 포함 — free/team 또는 비스크린샷은 null
                if (isTeamPro && file.CapturedTAbs.HasValue)
                {
                    string tAbsStr = file.CapturedTAbs.Value.ToString(
                        "R", System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append($",\"captured_t_abs\":{tAbsStr}");
                }
                sb.Append("}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>JSON 문자열 값 내 특수문자를 이스케이프합니다.</summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
