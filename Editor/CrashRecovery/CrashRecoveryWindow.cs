using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GaoZombie.BugOneTouch.Editor
{
    /// <summary>
    /// 크래시 번들 목록을 표시하고 Jira 제출을 지원하는 EditorWindow.
    ///
    /// 기능:
    ///   - 시간순 크래시 번들 목록 (최신이 위)
    ///   - 각 항목: 날짜, 시간, 데이터 무결성 배지 (초록/노랑/빨강)
    ///   - 미리보기: 스크린샷 썸네일, 로그 요약
    ///   - 상태: unregistered / registered
    ///   - Jira 제출 버튼
    ///
    /// 메뉴: Window/Bug-OneTouch/Crash Recovery
    /// </summary>
    public class CrashRecoveryWindow : EditorWindow
    {
        // ──────────────────────────────────────────────────────────────
        // 상수
        // ──────────────────────────────────────────────────────────────

        private const string WindowTitle = "크래시 복구";
        private const string MenuPath = BugOneTouchEditorInfo.MenuRoot + "/Crash Recovery";

        // UI 색상
        private static readonly Color ColorOk = new Color(0.2f, 0.8f, 0.2f);
        private static readonly Color ColorPartial = new Color(0.9f, 0.7f, 0.1f);
        private static readonly Color ColorMissing = new Color(0.9f, 0.2f, 0.2f);
        private static readonly Color ColorRegistered = new Color(0.2f, 0.6f, 0.9f);
        private static readonly Color ColorUnregistered = new Color(0.7f, 0.7f, 0.7f);

        // ──────────────────────────────────────────────────────────────
        // 내부 상태
        // ──────────────────────────────────────────────────────────────

        private List<CrashBundleManifest> _bundles = new List<CrashBundleManifest>();
        private Vector2 _scrollPosition;
        private int _selectedIndex = -1;
        private double _lastRefreshTime;

        // 선택된 번들 미리보기 상태
        private string _previewLogSummary = "";
        private bool _isLoadingPreview;

        // Jira 제출 입력 필드
        private string _jiraProjectKey = "";
        private bool _isSubmitting;

        // 제출기
        private CrashJiraSubmitter _submitter;

        // ──────────────────────────────────────────────────────────────
        // 메뉴 및 창 열기
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 메뉴에서 창을 열거나 기존 창을 포커스합니다.
        /// </summary>
        [MenuItem(MenuPath)]
        public static void OpenFromMenu()
        {
            OpenWindow();
        }

        /// <summary>
        /// 프로그래밍 방식으로 창을 열거나 기존 창을 포커스합니다.
        /// CrashBundleScanner에서 자동으로 호출합니다.
        /// </summary>
        public static void OpenWindow()
        {
            var window = GetWindow<CrashRecoveryWindow>(utility: false, title: WindowTitle, focus: true);
            window.minSize = new Vector2(600f, 400f);
            window.RefreshBundles();
        }

        // ──────────────────────────────────────────────────────────────
        // 라이프사이클
        // ──────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            _submitter = new CrashJiraSubmitter();
            RefreshBundles();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_bundles.Count == 0)
            {
                DrawEmptyState();
                return;
            }

            DrawBundleListAndDetail();
        }

        // ──────────────────────────────────────────────────────────────
        // UI 그리기 - 툴바
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 상단 툴바를 그립니다.
        /// </summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField(
                $"크래시 번들 ({_bundles.Count}개)",
                EditorStyles.boldLabel,
                GUILayout.Width(150f));

            GUILayout.FlexibleSpace();

            // 비정상 종료 플래그 표시
            if (CrashBundleScanner.CheckAbnormalExitFlag())
            {
                var prevColor = GUI.color;
                GUI.color = ColorPartial;
                EditorGUILayout.LabelField("⚠ 비정상 종료 플래그 감지", GUILayout.Width(180f));
                GUI.color = prevColor;
            }

            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                RefreshBundles();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ──────────────────────────────────────────────────────────────
        // UI 그리기 - 빈 상태
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 번들이 없을 때 빈 상태 메시지를 표시합니다.
        /// </summary>
        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("크래시 번들이 없습니다.", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        // ──────────────────────────────────────────────────────────────
        // UI 그리기 - 번들 목록 + 상세
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 번들 목록 (좌) + 상세 패널 (우) 레이아웃을 그립니다.
        /// </summary>
        private void DrawBundleListAndDetail()
        {
            EditorGUILayout.BeginHorizontal();

            // 좌측: 번들 목록
            EditorGUILayout.BeginVertical(GUILayout.Width(280f));
            DrawBundleList();
            EditorGUILayout.EndVertical();

            // 구분선
            var separatorRect = EditorGUILayout.GetControlRect(GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(separatorRect, new Color(0.3f, 0.3f, 0.3f));

            // 우측: 상세 패널
            EditorGUILayout.BeginVertical();
            if (_selectedIndex >= 0 && _selectedIndex < _bundles.Count)
                DrawBundleDetail(_bundles[_selectedIndex]);
            else
                EditorGUILayout.LabelField("번들을 선택하세요.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 번들 목록을 그립니다 (최신순).
        /// </summary>
        private void DrawBundleList()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // 최신이 위에 표시되도록 역순으로 순회
            for (int i = _bundles.Count - 1; i >= 0; i--)
            {
                DrawBundleListItem(i, _bundles[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 번들 목록 항목 하나를 그립니다.
        /// </summary>
        private void DrawBundleListItem(int index, CrashBundleManifest manifest)
        {
            bool isSelected = _selectedIndex == index;
            var bgColor = isSelected
                ? new Color(0.2f, 0.4f, 0.8f, 0.5f)
                : new Color(0f, 0f, 0f, 0f);

            var rect = EditorGUILayout.BeginVertical();

            // 선택 배경
            if (isSelected)
                EditorGUI.DrawRect(rect, bgColor);

            EditorGUILayout.BeginHorizontal();

            // 무결성 배지
            DrawIntegrityBadge(manifest.data_integrity);

            // 날짜/시간 + 타입
            EditorGUILayout.BeginVertical();
            string formattedTime = FormatTimestamp(manifest.created_at);
            EditorGUILayout.LabelField(formattedTime, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(manifest.crash_type ?? "unknown", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            // 등록 상태 뱃지
            DrawRegistrationBadge(manifest);

            EditorGUILayout.EndHorizontal();

            // 클릭 감지
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selectedIndex = index;
                LoadPreview(manifest);
                Repaint();
            }

            EditorGUILayout.EndVertical();

            // 구분선
            var lineRect = EditorGUILayout.GetControlRect(GUILayout.Height(1f));
            EditorGUI.DrawRect(lineRect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
        }

        // ──────────────────────────────────────────────────────────────
        // UI 그리기 - 배지
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 데이터 무결성 배지를 그립니다.
        /// </summary>
        private static void DrawIntegrityBadge(DataIntegrity integrity)
        {
            if (integrity == null)
            {
                DrawColoredBadge("?", ColorMissing, 20f);
                return;
            }

            switch (integrity.overall)
            {
                case "complete": // PRD 스펙 AC-26: "ok" 대신 "complete" 사용
                    DrawColoredBadge("●", ColorOk, 20f);
                    break;
                case "partial":
                    DrawColoredBadge("◑", ColorPartial, 20f);
                    break;
                default:
                    DrawColoredBadge("○", ColorMissing, 20f);
                    break;
            }
        }

        /// <summary>
        /// 등록 상태 배지를 그립니다 (registered / unregistered).
        /// </summary>
        private static void DrawRegistrationBadge(CrashBundleManifest manifest)
        {
            bool isRegistered = !string.IsNullOrEmpty(manifest.jira_issue_key);
            var prevColor = GUI.color;

            if (isRegistered)
            {
                GUI.color = ColorRegistered;
                EditorGUILayout.LabelField(manifest.jira_issue_key, EditorStyles.miniLabel, GUILayout.Width(70f));
            }
            else
            {
                GUI.color = ColorUnregistered;
                EditorGUILayout.LabelField("미등록", EditorStyles.miniLabel, GUILayout.Width(40f));
            }

            GUI.color = prevColor;
        }

        /// <summary>
        /// 색상이 있는 레이블 배지를 그립니다.
        /// </summary>
        private static void DrawColoredBadge(string text, Color color, float width)
        {
            var prevColor = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField(text, GUILayout.Width(width));
            GUI.color = prevColor;
        }

        // ──────────────────────────────────────────────────────────────
        // UI 그리기 - 상세 패널
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 선택된 번들의 상세 정보를 그립니다.
        /// </summary>
        private void DrawBundleDetail(CrashBundleManifest manifest)
        {
            EditorGUILayout.Space(4f);

            // 헤더
            EditorGUILayout.LabelField($"크래시 상세: {manifest.id}", EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            // 기본 정보
            DrawLabeledField("생성 시각", FormatTimestamp(manifest.created_at));
            DrawLabeledField("크래시 유형", manifest.crash_type ?? "-");
            DrawLabeledField("예외 타입", manifest.exception_type ?? "-");
            DrawLabeledField("Unity 버전", manifest.unity_version ?? "-");

            EditorGUILayout.Space(4f);

            // 무결성 상태
            EditorGUILayout.LabelField("데이터 무결성", EditorStyles.boldLabel);
            if (manifest.data_integrity != null)
            {
                DrawLabeledField("로그", manifest.data_integrity.logs_ok ? "✓" : "✗");
                DrawLabeledField("상태", manifest.data_integrity.state_ok ? "✓" : "✗");
                DrawLabeledField("영상", manifest.data_integrity.video_ok ? "✓" : "✗");
                DrawLabeledField("전체", manifest.data_integrity.overall ?? "-");
            }

            EditorGUILayout.Space(4f);

            // 예외 메시지
            if (!string.IsNullOrEmpty(manifest.exception_message))
            {
                EditorGUILayout.LabelField("예외 메시지", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(manifest.exception_message, MessageType.Error);
            }

            // 로그 요약
            if (!string.IsNullOrEmpty(_previewLogSummary))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("로그 요약 (마지막 10줄)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(_previewLogSummary, MessageType.Info);
            }

            EditorGUILayout.Space(8f);

            // Jira 등록 상태
            if (!string.IsNullOrEmpty(manifest.jira_issue_key))
            {
                EditorGUILayout.LabelField("Jira 이슈", EditorStyles.boldLabel);
                DrawLabeledField("이슈 키", manifest.jira_issue_key);
                DrawLabeledField("등록 시각", manifest.registered_at ?? "-");

                if (GUILayout.Button("Jira에서 열기", GUILayout.Height(30f)))
                    OpenJiraIssue(manifest.jira_issue_key);
            }
            else
            {
                DrawJiraSubmitSection(manifest);
            }
        }

        /// <summary>
        /// Jira 제출 섹션을 그립니다.
        /// </summary>
        private void DrawJiraSubmitSection(CrashBundleManifest manifest)
        {
            EditorGUILayout.LabelField("Jira 이슈 등록", EditorStyles.boldLabel);

            _jiraProjectKey = EditorGUILayout.TextField("프로젝트 키", _jiraProjectKey);

            EditorGUI.BeginDisabledGroup(_isSubmitting || string.IsNullOrEmpty(_jiraProjectKey));

            if (GUILayout.Button(_isSubmitting ? "제출 중..." : "Jira에 제출", GUILayout.Height(32f)))
            {
                SubmitToJira(manifest);
            }

            EditorGUI.EndDisabledGroup();
        }

        // ──────────────────────────────────────────────────────────────
        // 데이터 작업
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 크래시 번들 목록을 새로고침합니다.
        /// </summary>
        public void RefreshBundles()
        {
            _bundles = CrashBundleWriter.ScanAllBundles();
            _selectedIndex = -1;
            _previewLogSummary = "";
            _lastRefreshTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        /// <summary>
        /// 선택된 번들의 미리보기 데이터를 로드합니다.
        /// </summary>
        private void LoadPreview(CrashBundleManifest manifest)
        {
            _previewLogSummary = "";

            // 스택 트레이스가 있으면 요약으로 사용
            if (!string.IsNullOrEmpty(manifest.stack_trace))
            {
                var lines = manifest.stack_trace.Split('\n');
                int maxLines = Mathf.Min(lines.Length, 10);
                _previewLogSummary = string.Join("\n", lines, 0, maxLines);
                if (lines.Length > 10)
                    _previewLogSummary += $"\n... ({lines.Length - 10}줄 더)";
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Jira 제출
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 크래시 번들을 Jira에 제출합니다.
        /// </summary>
        private async void SubmitToJira(CrashBundleManifest manifest)
        {
            if (_isSubmitting)
                return;

            if (string.IsNullOrEmpty(_jiraProjectKey))
            {
                EditorUtility.DisplayDialog("입력 오류", "Jira 프로젝트 키를 입력하세요.", "확인");
                return;
            }

            _isSubmitting = true;
            Repaint();

            try
            {
                var result = await _submitter.SubmitAsync(manifest, _jiraProjectKey);

                if (result.Success)
                {
                    EditorUtility.DisplayDialog(
                        "제출 완료",
                        $"Jira 이슈가 생성되었습니다: {result.IssueKey}",
                        "확인");

                    RefreshBundles();
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "제출 실패",
                        $"Jira 이슈 생성에 실패했습니다:\n{result.ErrorMessage}",
                        "확인");
                }
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "제출 오류",
                    $"예외가 발생했습니다:\n{ex.Message}",
                    "확인");
            }
            finally
            {
                _isSubmitting = false;
                Repaint();
            }
        }

        /// <summary>
        /// Jira 이슈를 기본 브라우저에서 엽니다.
        /// Settings의 jiraSiteUrl을 사용하여 URL을 구성합니다.
        /// </summary>
        private static void OpenJiraIssue(string issueKey)
        {
            var settings = BugOneTouchSettingsProvider.Settings;
            if (settings != null && !string.IsNullOrEmpty(settings.jiraSiteUrl))
            {
                string url = $"{settings.jiraSiteUrl}/browse/{issueKey}";
                Application.OpenURL(url);
            }
            else
            {
                Debug.LogWarning($"[BugOneTouch] Jira 사이트 URL이 설정되지 않았습니다. Settings에서 구성하세요. 이슈 키: {issueKey}");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // UI 유틸리티
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 레이블 + 값 쌍을 수평으로 표시합니다.
        /// </summary>
        private static void DrawLabeledField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(80f));
            EditorGUILayout.LabelField(value ?? "-", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// ISO 8601 타임스탬프를 읽기 쉬운 형식으로 변환합니다.
        /// </summary>
        private static string FormatTimestamp(string isoTimestamp)
        {
            if (string.IsNullOrEmpty(isoTimestamp))
                return "-";

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
