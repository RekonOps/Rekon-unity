using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace RekonOps.BugBeacon.Editor
{
    /// <summary>
    /// 로컬 번들 목록을 표시하고 관리하는 에디터 윈도우.
    /// Window/BugBeacon/Bundles 메뉴에서 열립니다.
    ///
    /// 기능:
    ///   - 번들 목록 표시 (날짜, 제목, 상태 배지 색상 코딩)
    ///   - 상태별 필터 (전체 / 미제출 / 제출됨 / 실패)
    ///   - 각 번들 액션: Jira 제출, 재시도, 삭제, 폴더 열기
    ///   - 하단: 디스크 사용량, 보관 정책 적용 버튼
    ///   - BundleRepository 사용
    /// </summary>
    public class BundleManagerWindow : EditorWindow
    {
        // ─── 필터 정의 ────────────────────────────────────────────────────────────

        private enum BundleFilter
        {
            /// <summary>전체 번들 표시</summary>
            All,

            /// <summary>미제출 번들 (Created, Pending, Failed)</summary>
            Unsubmitted,

            /// <summary>제출 완료 번들 (Submitted)</summary>
            Submitted,

            /// <summary>실패한 번들 (Failed)</summary>
            Failed,
        }

        private static readonly string[] FilterLabels = { "전체", "미제출", "제출됨", "실패" };

        // ─── 내부 상태 ─────────────────────────────────────────────────────────────

        private BundleRepository _repository;
        private List<BundleManifest> _allBundles     = new List<BundleManifest>();
        private List<BundleManifest> _filteredBundles = new List<BundleManifest>();
        private BundleFilter _currentFilter = BundleFilter.All;
        private Vector2 _scrollPos;

        // 비동기 로딩 상태
        private bool   _isLoading    = false;
        private string _loadError    = "";
        private string _actionStatus = "";

        // 디스크 사용량 캐시
        private long _totalDiskUsageBytes = 0L;

        // 마지막 새로고침 시간
        private double _lastRefreshTime;
        private const double AutoRefreshIntervalSeconds = 30.0;

        // ─── 메뉴 등록 ────────────────────────────────────────────────────────────

        [MenuItem(BugBeaconEditorInfo.MenuRoot + "/Bundles")]
        public static void OpenWindow()
        {
            var window = GetWindow<BundleManagerWindow>("번들 관리자");
            window.minSize = new Vector2(580f, 400f);
            window.Show();
        }

        // ─── 생명주기 ─────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            _repository = new BundleRepository();
            _ = RefreshBundlesAsync();
        }

        private void OnGUI()
        {
            // 자동 새로고침
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRefreshTime > AutoRefreshIntervalSeconds && !_isLoading)
            {
                _ = RefreshBundlesAsync();
            }

            DrawToolbar();
            DrawBundleList();
            DrawFooter();

            // 액션 상태 메시지
            if (!string.IsNullOrEmpty(_actionStatus))
            {
                Rect statusRect = new Rect(0, position.height - 20f, position.width, 20f);
                EditorGUI.LabelField(statusRect, _actionStatus, EditorStyles.helpBox);
            }
        }

        // ─── 툴바 ─────────────────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // 필터 드롭다운
                EditorGUILayout.LabelField("필터:", GUILayout.Width(30f));
                BundleFilter newFilter = (BundleFilter)EditorGUILayout.Popup(
                    (int)_currentFilter,
                    FilterLabels,
                    GUILayout.Width(80f));

                if (newFilter != _currentFilter)
                {
                    _currentFilter = newFilter;
                    ApplyFilter();
                }

                GUILayout.FlexibleSpace();

                // 디스크 사용량 표시
                EditorGUILayout.LabelField(
                    $"디스크: {FormatBytes(_totalDiskUsageBytes)}",
                    EditorStyles.miniLabel,
                    GUILayout.Width(160f));

                // 새로고침 버튼
                if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    _ = RefreshBundlesAsync();
                }
            }

            // 로딩 중 표시
            if (_isLoading)
            {
                EditorGUILayout.HelpBox("번들 목록을 불러오는 중...", MessageType.None);
            }

            // 오류 표시
            if (!string.IsNullOrEmpty(_loadError))
            {
                EditorGUILayout.HelpBox(_loadError, MessageType.Error);
            }
        }

        // ─── 번들 목록 ────────────────────────────────────────────────────────────

        private void DrawBundleList()
        {
            if (_filteredBundles.Count == 0 && !_isLoading)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.LabelField("표시할 번들이 없습니다.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // 헤더
            DrawListHeader();
            DrawSeparator();

            // 스크롤 영역 (하단 푸터 높이 제외)
            float footerHeight = 50f;
            float listHeight   = position.height - EditorStyles.toolbar.fixedHeight - 40f - footerHeight;
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(listHeight));

            foreach (var bundle in _filteredBundles)
            {
                DrawBundleRow(bundle);
                DrawSeparator();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawListHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("날짜/시간", EditorStyles.boldLabel, GUILayout.Width(130f));
                EditorGUILayout.LabelField("제목", EditorStyles.boldLabel, GUILayout.MinWidth(200f));
                EditorGUILayout.LabelField("크기", EditorStyles.boldLabel, GUILayout.Width(70f));
                EditorGUILayout.LabelField("상태", EditorStyles.boldLabel, GUILayout.Width(70f));
                EditorGUILayout.LabelField("액션", EditorStyles.boldLabel, GUILayout.Width(200f));
            }
        }

        private void DrawBundleRow(BundleManifest bundle)
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // 날짜/시간
                    string dateStr = FormatBundleDate(bundle.created_at);
                    EditorGUILayout.LabelField(dateStr, EditorStyles.miniLabel, GUILayout.Width(130f));

                    // 제목 (없으면 ID 일부 표시)
                    string title = string.IsNullOrEmpty(bundle.title)
                        ? $"(번들 {bundle.id?[..Math.Min(8, bundle.id?.Length ?? 0)]})"
                        : bundle.title;
                    EditorGUILayout.LabelField(title, GUILayout.MinWidth(200f));

                    // 크기
                    EditorGUILayout.LabelField(
                        FormatBytes(bundle.total_size_bytes),
                        EditorStyles.miniLabel,
                        GUILayout.Width(70f));

                    // 상태 배지
                    DrawStateBadge(bundle.state);

                    // 액션 버튼
                    DrawBundleActions(bundle);
                }

                // Jira 이슈 키 (제출된 경우)
                if (!string.IsNullOrEmpty(bundle.jira_issue_key))
                {
                    EditorGUILayout.LabelField(
                        $"  → Jira: {bundle.jira_issue_key}",
                        EditorStyles.miniLabel);
                }

                // 재시도 횟수 (실패한 경우)
                if (bundle.state == BundleState.Failed && bundle.retry_count > 0)
                {
                    EditorGUILayout.LabelField(
                        $"  재시도 횟수: {bundle.retry_count}",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(2f);
            }
        }

        // ─── 상태 배지 ────────────────────────────────────────────────────────────

        private static void DrawStateBadge(BundleState state)
        {
            (string label, Color color) = GetBadgeStyle(state);

            Color originalBg   = GUI.backgroundColor;
            Color originalContent = GUI.contentColor;
            GUI.backgroundColor = color;
            GUI.contentColor    = Color.white;

            GUILayout.Label(label, GetBadgeLabelStyle(), GUILayout.Width(70f), GUILayout.Height(18f));

            GUI.backgroundColor = originalBg;
            GUI.contentColor    = originalContent;
        }

        private static (string label, Color color) GetBadgeStyle(BundleState state)
        {
            return state switch
            {
                BundleState.Created    => ("생성됨",  new Color(0.50f, 0.50f, 0.50f)),
                BundleState.Pending    => ("대기중",  new Color(0.90f, 0.65f, 0.10f)),
                BundleState.Submitting => ("제출중",  new Color(0.27f, 0.53f, 1.00f)),
                BundleState.Submitted  => ("성공",    new Color(0.27f, 0.73f, 0.27f)),
                BundleState.Failed     => ("실패",    new Color(1.00f, 0.27f, 0.27f)),
                _                      => ("알수없음", Color.gray),
            };
        }

        private static GUIStyle _badgeLabelStyle;
        private static GUIStyle GetBadgeLabelStyle()
        {
            if (_badgeLabelStyle == null)
            {
                _badgeLabelStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize  = 10,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = Color.white },
                };
            }
            return _badgeLabelStyle;
        }

        // ─── 번들 액션 버튼 ───────────────────────────────────────────────────────

        private void DrawBundleActions(BundleManifest bundle)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(200f)))
            {
                // Jira 제출 버튼 (Created, Pending, Failed 상태)
                bool canSubmit = bundle.state == BundleState.Created
                              || bundle.state == BundleState.Pending
                              || bundle.state == BundleState.Failed;

                if (canSubmit)
                {
                    if (GUILayout.Button("Jira 제출", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        _ = SubmitBundleToJiraAsync(bundle);
                    }
                }

                // 재시도 버튼 (Failed 상태)
                if (bundle.state == BundleState.Failed)
                {
                    if (GUILayout.Button("재시도", EditorStyles.miniButton, GUILayout.Width(45f)))
                    {
                        _ = RetrySubmitAsync(bundle);
                    }
                }

                // 삭제 버튼
                Color originalBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("삭제", EditorStyles.miniButton, GUILayout.Width(36f)))
                {
                    if (EditorUtility.DisplayDialog(
                        "번들 삭제",
                        $"번들을 삭제하시겠습니까?\n제목: {bundle.title}\nID: {bundle.id?[..Math.Min(8, bundle.id?.Length ?? 0)]}...",
                        "삭제",
                        "취소"))
                    {
                        _ = DeleteBundleAsync(bundle);
                    }
                }
                GUI.backgroundColor = originalBg;

                // 폴더 열기 버튼
                if (GUILayout.Button("폴더", EditorStyles.miniButton, GUILayout.Width(36f)))
                {
                    OpenBundleFolder(bundle);
                }
            }
        }

        // ─── 푸터 ─────────────────────────────────────────────────────────────────

        private void DrawFooter()
        {
            DrawSeparator();
            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"총 {_filteredBundles.Count}개 번들",
                    EditorStyles.miniLabel,
                    GUILayout.Width(120f));

                GUILayout.FlexibleSpace();

                // 보관 정책 적용 버튼
                if (GUILayout.Button("보관 정책 적용", GUILayout.Width(120f), GUILayout.Height(22f)))
                {
                    _ = ApplyRetentionPolicyAsync();
                }
            }

            EditorGUILayout.Space(4f);
        }

        // ─── 비동기 데이터 로딩 ───────────────────────────────────────────────────

        /// <summary>
        /// 번들 목록을 비동기로 새로고침합니다.
        /// </summary>
        internal async Task RefreshBundlesAsync()
        {
            if (_isLoading) return;

            _isLoading   = true;
            _loadError   = "";
            _lastRefreshTime = EditorApplication.timeSinceStartup;

            try
            {
                List<BundleManifest> bundles = await _repository.GetAllAsync();
                _allBundles = bundles ?? new List<BundleManifest>();

                // 최신 순으로 정렬 (created_at 역순)
                _allBundles.Sort((a, b) =>
                    string.Compare(b.created_at, a.created_at, StringComparison.Ordinal));

                // 디스크 사용량 합산
                _totalDiskUsageBytes = 0L;
                foreach (var b in _allBundles)
                    _totalDiskUsageBytes += b.total_size_bytes;

                ApplyFilter();
            }
            catch (Exception ex)
            {
                _loadError = $"번들 로딩 오류: {ex.Message}";
                Debug.LogError($"[BugBeacon] BundleManagerWindow 로딩 오류: {ex}");
            }
            finally
            {
                _isLoading = false;
                Repaint();
            }
        }

        // ─── 필터 적용 ────────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            _filteredBundles.Clear();

            foreach (var bundle in _allBundles)
            {
                if (MatchesFilter(bundle, _currentFilter))
                    _filteredBundles.Add(bundle);
            }
        }

        private static bool MatchesFilter(BundleManifest bundle, BundleFilter filter)
        {
            return filter switch
            {
                BundleFilter.All         => true,
                BundleFilter.Unsubmitted => bundle.state == BundleState.Created
                                         || bundle.state == BundleState.Pending
                                         || bundle.state == BundleState.Failed,
                BundleFilter.Submitted   => bundle.state == BundleState.Submitted,
                BundleFilter.Failed      => bundle.state == BundleState.Failed,
                _                        => true,
            };
        }

        // ─── 번들 액션 비동기 처리 ────────────────────────────────────────────────

        /// <summary>
        /// 번들을 Pending 상태로 변경합니다 (실제 Jira 제출은 SubmissionQueue가 처리).
        /// </summary>
        private async Task SubmitBundleToJiraAsync(BundleManifest bundle)
        {
            SetActionStatus($"번들 '{bundle.title}' Jira 제출 준비 중...");
            try
            {
                await _repository.UpdateStateAsync(bundle.id, BundleState.Pending);
                SetActionStatus($"번들 상태를 'Pending'으로 변경했습니다. 다음 제출 주기에 자동 처리됩니다.");
                await RefreshBundlesAsync();
            }
            catch (Exception ex)
            {
                SetActionStatus($"오류: {ex.Message}");
                Debug.LogError($"[BugBeacon] 번들 제출 준비 오류: {ex}");
            }
        }

        /// <summary>
        /// 실패한 번들을 Pending으로 되돌려 재시도를 준비합니다.
        /// </summary>
        private async Task RetrySubmitAsync(BundleManifest bundle)
        {
            SetActionStatus($"번들 '{bundle.title}' 재시도 준비 중...");
            try
            {
                await _repository.UpdateStateAsync(bundle.id, BundleState.Pending);
                SetActionStatus("재시도 준비 완료. 다음 제출 주기에 자동 처리됩니다.");
                await RefreshBundlesAsync();
            }
            catch (Exception ex)
            {
                SetActionStatus($"재시도 준비 오류: {ex.Message}");
                Debug.LogError($"[BugBeacon] 재시도 준비 오류: {ex}");
            }
        }

        /// <summary>
        /// 번들 디렉토리를 삭제합니다.
        /// </summary>
        private async Task DeleteBundleAsync(BundleManifest bundle)
        {
            SetActionStatus($"번들 '{bundle.title}' 삭제 중...");
            try
            {
                await _repository.DeleteAsync(bundle.id);
                SetActionStatus($"번들 삭제 완료: {bundle.id?[..Math.Min(8, bundle.id?.Length ?? 0)]}");
                await RefreshBundlesAsync();
            }
            catch (Exception ex)
            {
                SetActionStatus($"삭제 오류: {ex.Message}");
                Debug.LogError($"[BugBeacon] 번들 삭제 오류: {ex}");
            }
        }

        /// <summary>
        /// OS 파일 탐색기에서 번들 폴더를 엽니다.
        /// </summary>
        private static void OpenBundleFolder(BundleManifest bundle)
        {
            string bundleDir = BundleWriter.GetBundleDirectory(bundle.id);
            if (Directory.Exists(bundleDir))
            {
                EditorUtility.RevealInFinder(bundleDir);
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "폴더 없음",
                    $"번들 폴더를 찾을 수 없습니다:\n{bundleDir}",
                    "확인");
            }
        }

        /// <summary>
        /// 보관 정책(기간 초과, 개수 초과)을 적용하여 오래된 번들을 삭제합니다.
        /// </summary>
        private async Task ApplyRetentionPolicyAsync()
        {
            BugBeaconSettings settings = BugBeaconSettingsProvider.Settings;

            bool confirm = EditorUtility.DisplayDialog(
                "보관 정책 적용",
                $"설정된 보관 정책을 적용하시겠습니까?\n" +
                $"• 최대 번들 수: {settings.maxBundles}개\n" +
                $"• 최대 디스크 용량: {settings.maxDiskUsageMB} MB\n" +
                $"기준을 초과하는 오래된 번들이 삭제됩니다.",
                "적용",
                "취소");

            if (!confirm) return;

            SetActionStatus("보관 정책 적용 중...");
            try
            {
                int deleted = await ApplyRetentionInternal(settings);
                SetActionStatus($"보관 정책 완료: {deleted}개 번들 삭제됨");
                await RefreshBundlesAsync();
            }
            catch (Exception ex)
            {
                SetActionStatus($"보관 정책 오류: {ex.Message}");
                Debug.LogError($"[BugBeacon] 보관 정책 오류: {ex}");
            }
        }

        /// <summary>
        /// 보관 정책 내부 로직: 초과 번들 삭제.
        /// Submitted, Failed, Created 순서로 오래된 것부터 삭제합니다.
        /// </summary>
        private async Task<int> ApplyRetentionInternal(BugBeaconSettings settings)
        {
            int deleted = 0;
            long maxBytes = (long)settings.maxDiskUsageMB * 1024 * 1024;

            // 전체 목록 최신 새로고침
            List<BundleManifest> all = await _repository.GetAllAsync();

            // 오래된 것이 앞에 오도록 정렬
            all.Sort((a, b) => string.Compare(a.created_at, b.created_at, StringComparison.Ordinal));

            long totalBytes = 0L;
            foreach (var b in all) totalBytes += b.total_size_bytes;

            // 최대 개수 초과 제거 (오래된 순)
            while (all.Count > settings.maxBundles)
            {
                var oldest = all[0];
                await _repository.DeleteAsync(oldest.id);
                totalBytes -= oldest.total_size_bytes;
                all.RemoveAt(0);
                deleted++;
                Debug.Log($"[BugBeacon] 보관 정책 삭제(개수 초과): {oldest.id}");
            }

            // 최대 디스크 용량 초과 제거 (오래된 순)
            while (totalBytes > maxBytes && all.Count > 0)
            {
                var oldest = all[0];
                await _repository.DeleteAsync(oldest.id);
                totalBytes -= oldest.total_size_bytes;
                all.RemoveAt(0);
                deleted++;
                Debug.Log($"[BugBeacon] 보관 정책 삭제(용량 초과): {oldest.id}");
            }

            return deleted;
        }

        // ─── 헬퍼 ─────────────────────────────────────────────────────────────────

        private void SetActionStatus(string message)
        {
            _actionStatus = message;
            Repaint();
            // 5초 후 상태 메시지 지우기
            EditorApplication.delayCall += () =>
            {
                // 같은 메시지인 경우에만 지우기
                if (_actionStatus == message)
                    _actionStatus = "";
                Repaint();
            };
        }

        /// <summary>
        /// 번들 created_at ISO 8601 문자열을 표시용 형식으로 변환합니다.
        /// </summary>
        private static string FormatBundleDate(string isoDate)
        {
            if (string.IsNullOrEmpty(isoDate)) return "-";
            if (DateTime.TryParse(isoDate, out DateTime dt))
                return dt.ToLocalTime().ToString("MM-dd HH:mm:ss");
            return isoDate;
        }

        /// <summary>
        /// 바이트를 사람이 읽기 쉬운 형식으로 변환합니다.
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024L)         return $"{bytes} B";
            if (bytes < 1024L * 1024)  return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        /// <summary>
        /// 수평 구분선을 그립니다.
        /// </summary>
        private static void DrawSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.4f, 0.4f, 0.4f, 0.5f));
        }
    }
}
