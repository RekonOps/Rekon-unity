using System;
using UnityEngine;

namespace GaoZombie.BugOneTouch
{
    /// <summary>
    /// 캡처 진행 상황을 Play Mode 화면에 오버레이로 표시하는 컴포넌트.
    ///
    /// 기능:
    ///   - 캡처 시작 시: 화면 가장자리 녹색 플래시 (0.3초)
    ///   - 캡처 진행 중: 우하단 반투명 박스에 프로그레스 바 + 단계 텍스트
    ///   - IMGUI (OnGUI) 기반으로 UI Toolkit / uGUI 의존 없이 동작
    ///
    /// 사용법:
    ///   CaptureOverlay overlay = CaptureOverlay.EnsureInstance();
    ///   overlay.BindOrchestrator(captureOrchestrator);
    /// </summary>
    [DisallowMultipleComponent]
    public class CaptureOverlay : MonoBehaviour
    {
        // ─── 상수 ─────────────────────────────────────────────────────────────────

        /// <summary>가장자리 플래시 지속 시간 (초)</summary>
        private const float FlashDuration = 0.3f;

        /// <summary>플래시 가장자리 두께 (픽셀)</summary>
        private const float FlashBorderThickness = 12f;

        /// <summary>오버레이 박스 너비 (픽셀)</summary>
        private const float OverlayWidth = 200f;

        /// <summary>오버레이 박스 높이 (픽셀)</summary>
        private const float OverlayHeight = 70f;

        /// <summary>오버레이 박스 여백 (픽셀)</summary>
        private const float OverlayMargin = 16f;

        // ─── Silent 모드 상수 ─────────────────────────────────────────────────────

        /// <summary>최소 인디케이터 크기 (픽셀)</summary>
        private const float IndicatorSize = 40f;

        /// <summary>최소 인디케이터 여백 (픽셀)</summary>
        private const float IndicatorMargin = 20f;

        /// <summary>인디케이터 내부 원 크기 (픽셀)</summary>
        private const float IndicatorDotSize = 16f;

        /// <summary>깜빡임 주기 (초)</summary>
        private const float BlinkInterval = 0.5f;

        /// <summary>완료 표시 지속 시간 (초)</summary>
        private const float CompletionDisplayDuration = 1.0f;

        // ─── 플래시 상태 ──────────────────────────────────────────────────────────

        /// <summary>플래시 잔여 시간 (0이면 비활성)</summary>
        private float _flashTimer = 0f;

        // ─── 프로그레스 상태 ──────────────────────────────────────────────────────

        /// <summary>현재 진행률 (0.0 ~ 1.0)</summary>
        private float _progress = 0f;

        /// <summary>현재 단계 이름</summary>
        private string _stageText = "";

        /// <summary>오버레이 표시 여부</summary>
        private bool _isVisible = false;

        // ─── Silent 모드 상태 ─────────────────────────────────────────────────────

        /// <summary>Silent 모드 여부 (간소화 UI 표시)</summary>
        private bool _silentMode = false;

        /// <summary>깜빡임 타이머</summary>
        private float _blinkTimer = 0f;

        /// <summary>깜빡임 상태 (true: 표시, false: 숨김)</summary>
        private bool _blinkVisible = true;

        /// <summary>Silent 모드 완료 표시 여부</summary>
        private bool _silentCompleted = false;

        // _silentSubmitting 제거됨: CaptureProgressEvent에 "submit" 단계가 없으므로 불필요

        // ─── GUI 스타일 캐시 ──────────────────────────────────────────────────────

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _progressBarBgStyle;
        private GUIStyle _progressBarFillStyle;
        private bool _stylesInitialized = false;

        // ─── 바인딩된 오케스트레이터 ─────────────────────────────────────────────

        private ICaptureOrchestrator _orchestrator;

        // ─── 정적 팩토리 ──────────────────────────────────────────────────────────

        /// <summary>
        /// CaptureOverlay 인스턴스를 반환합니다. 없으면 생성합니다.
        /// DontDestroyOnLoad로 씬 전환 시에도 유지됩니다.
        /// </summary>
        public static CaptureOverlay EnsureInstance()
        {
            CaptureOverlay existing = FindObjectOfType<CaptureOverlay>();
            if (existing != null) return existing;

            GameObject go = new GameObject("[BugOneTouch] CaptureOverlay");
            DontDestroyOnLoad(go);
            return go.AddComponent<CaptureOverlay>();
        }

        // ─── 공개 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// CaptureOrchestrator의 이벤트를 구독하여 오버레이를 연동합니다.
        /// </summary>
        /// <param name="orchestrator">연동할 오케스트레이터</param>
        public void BindOrchestrator(ICaptureOrchestrator orchestrator)
        {
            // 기존 바인딩 해제
            if (_orchestrator != null)
            {
                _orchestrator.OnProgress -= HandleProgress;
            }

            _orchestrator = orchestrator;

            if (_orchestrator != null)
            {
                _orchestrator.OnProgress += HandleProgress;
                Debug.Log("[BugOneTouch] CaptureOverlay: 오케스트레이터 바인딩 완료");
            }
        }

        /// <summary>
        /// Silent 모드를 설정합니다.
        /// Silent 모드에서는 간소화된 최소 인디케이터만 표시됩니다.
        /// </summary>
        /// <param name="silent">true: 간소화 UI, false: 기존 상세 UI</param>
        public void SetSilentMode(bool silent)
        {
            _silentMode = silent;
            Debug.Log($"[BugOneTouch] CaptureOverlay: Silent 모드 {(silent ? "활성화" : "비활성화")}");
        }

        /// <summary>
        /// 오버레이를 즉시 숨깁니다.
        /// </summary>
        public void Hide()
        {
            _isVisible = false;
            _progress  = 0f;
            _stageText = "";
            _silentCompleted = false;
        }

        // ─── Unity 생명주기 ───────────────────────────────────────────────────────

        private void OnDestroy()
        {
            if (_orchestrator != null)
            {
                _orchestrator.OnProgress -= HandleProgress;
            }
        }

        private void Update()
        {
            // 플래시 타이머 감소
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.unscaledDeltaTime;
                if (_flashTimer < 0f) _flashTimer = 0f;
            }

            // Silent 모드 깜빡임 업데이트
            if (_silentMode && _isVisible && !_silentCompleted)
            {
                _blinkTimer += Time.unscaledDeltaTime;
                if (_blinkTimer >= BlinkInterval)
                {
                    _blinkTimer -= BlinkInterval;
                    _blinkVisible = !_blinkVisible;
                }
            }
        }

        private void OnGUI()
        {
            // 스타일 지연 초기화 (OnGUI에서만 GUI.skin에 접근 가능)
            if (!_stylesInitialized)
            {
                InitializeStyles();
                _stylesInitialized = true;
            }

            // 가장자리 플래시 렌더링
            if (_flashTimer > 0f)
            {
                DrawFlash();
            }

            // 프로그레스 오버레이 렌더링
            if (_isVisible)
            {
                if (_silentMode)
                    DrawMinimalIndicator();
                else
                    DrawProgressOverlay();
            }
        }

        // ─── 이벤트 핸들러 ────────────────────────────────────────────────────────

        /// <summary>
        /// CaptureOrchestrator.OnProgress 이벤트 핸들러.
        /// </summary>
        private void HandleProgress(CaptureProgressEvent evt)
        {
            if (evt == null) return;

            _progress = evt.Progress;

            // 단계명을 한글로 변환
            _stageText = TranslateStageName(evt.Stage, evt.IsSuccess);

            if (evt.Stage == "complete" || evt.Progress >= 1.0f)
            {
                if (_silentMode)
                {
                    // Silent 모드: 완료 체크마크 표시 후 빠르게 숨기기
                    _silentCompleted = true;
                    _blinkVisible = true; // 완료 시 항상 표시
                    Invoke(nameof(Hide), CompletionDisplayDuration);
                }
                else
                {
                    // 기존 모드: 캡처 완료 텍스트 표시 후 숨기기
                    _stageText = "캡처 완료!";
                    Invoke(nameof(Hide), 1.0f);
                }
            }
            else if (!_isVisible)
            {
                // 첫 진행 이벤트: 플래시 시작 + 오버레이 표시
                _flashTimer = FlashDuration;
                _isVisible  = true;
                _blinkTimer = 0f;
                _blinkVisible = true;
                _silentCompleted = false;
            }
        }

        // ─── 렌더링 ───────────────────────────────────────────────────────────────

        /// <summary>
        /// 화면 가장자리 녹색 플래시를 그립니다.
        /// </summary>
        private void DrawFlash()
        {
            // 알파: 플래시 시작 시 1.0, 끝날 때 0.0
            float alpha = _flashTimer / FlashDuration;
            Color flashColor = new Color(0f, 1f, 0f, alpha * 0.8f);

            float w = Screen.width;
            float h = Screen.height;
            float t = FlashBorderThickness;

            // 상단
            DrawRect(new Rect(0, 0, w, t), flashColor);
            // 하단
            DrawRect(new Rect(0, h - t, w, t), flashColor);
            // 좌측
            DrawRect(new Rect(0, t, t, h - t * 2f), flashColor);
            // 우측
            DrawRect(new Rect(w - t, t, t, h - t * 2f), flashColor);
        }

        /// <summary>
        /// 우하단 프로그레스 박스를 그립니다.
        /// </summary>
        private void DrawProgressOverlay()
        {
            float x = Screen.width  - OverlayWidth  - OverlayMargin;
            float y = Screen.height - OverlayHeight - OverlayMargin;
            Rect boxRect = new Rect(x, y, OverlayWidth, OverlayHeight);

            // 반투명 배경 박스
            Color originalColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.Box(boxRect, GUIContent.none, _boxStyle);
            GUI.color = originalColor;

            float padding = 8f;
            float innerX  = x + padding;
            float innerW  = OverlayWidth - padding * 2f;

            // 단계 텍스트
            Rect labelRect = new Rect(innerX, y + 8f, innerW, 20f);
            GUI.Label(labelRect, _stageText, _labelStyle);

            // 프로그레스 바 배경
            float barY = y + 32f;
            Rect barBg = new Rect(innerX, barY, innerW, 14f);
            GUI.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            GUI.Box(barBg, GUIContent.none, _progressBarBgStyle);
            GUI.color = originalColor;

            // 프로그레스 바 채우기
            float fillWidth = Mathf.Max(4f, innerW * Mathf.Clamp01(_progress));
            Rect barFill = new Rect(innerX, barY, fillWidth, 14f);
            GUI.color = new Color(0.2f, 0.8f, 0.2f, 1f);
            GUI.Box(barFill, GUIContent.none, _progressBarFillStyle);
            GUI.color = originalColor;

            // 퍼센트 텍스트
            Rect percentRect = new Rect(innerX, barY + 18f, innerW, 16f);
            GUI.Label(percentRect, $"{(_progress * 100f):F0}%", _labelStyle);
        }

        /// <summary>
        /// Silent 모드용 최소 인디케이터를 그립니다.
        /// 우하단에 작은 원(캡처 중: 녹색) 또는 체크마크(완료) 표시.
        /// </summary>
        private void DrawMinimalIndicator()
        {
            float x = Screen.width  - IndicatorSize - IndicatorMargin;
            float y = Screen.height - IndicatorSize - IndicatorMargin;
            Rect bgRect = new Rect(x, y, IndicatorSize, IndicatorSize);

            // 반투명 배경
            DrawRect(bgRect, new Color(0f, 0f, 0f, 0.6f));

            if (_silentCompleted)
            {
                // 완료: 녹색 체크마크
                DrawCheckmark(bgRect);
            }
            else if (_blinkVisible)
            {
                // 캡처 중: 깜빡이는 녹색 원
                Color dotColor = new Color(0.2f, 0.9f, 0.2f, 1.0f);

                float dotX = x + (IndicatorSize - IndicatorDotSize) * 0.5f;
                float dotY = y + (IndicatorSize - IndicatorDotSize) * 0.5f;
                DrawCircle(new Rect(dotX, dotY, IndicatorDotSize, IndicatorDotSize), dotColor);
            }
        }

        /// <summary>
        /// 간단한 원을 IMGUI로 그립니다 (사각형으로 근사).
        /// IMGUI에서는 원 프리미티브가 없으므로 둥근 텍스처 대신
        /// 여러 겹의 사각형으로 원을 근사합니다.
        /// </summary>
        private static void DrawCircle(Rect rect, Color color)
        {
            // 중심과 반지름 계산
            float cx = rect.x + rect.width * 0.5f;
            float cy = rect.y + rect.height * 0.5f;
            float radius = rect.width * 0.5f;

            // 수평 슬라이스로 원 근사 (부드러운 원형)
            int slices = Mathf.Max(8, (int)(radius * 2));
            for (int i = 0; i < slices; i++)
            {
                float t = (i + 0.5f) / slices;              // 0~1 정규화
                float dy = (t - 0.5f) * 2f * radius;        // -radius ~ +radius
                float halfWidth = Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
                float sliceY = cy + dy - 0.5f;
                Rect sliceRect = new Rect(cx - halfWidth, sliceY, halfWidth * 2f, 1f);
                DrawRect(sliceRect, color);
            }
        }

        /// <summary>
        /// 체크마크(✓)를 IMGUI로 그립니다.
        /// </summary>
        private static void DrawCheckmark(Rect bgRect)
        {
            Color checkColor = new Color(0.2f, 0.9f, 0.2f, 1.0f);
            float cx = bgRect.x + bgRect.width * 0.5f;
            float cy = bgRect.y + bgRect.height * 0.5f;
            float scale = bgRect.width * 0.3f;

            // 체크마크를 2개의 선분으로 구성 (왼쪽 아래 꺾임 + 오른쪽 위)
            // 선분 1: 좌하 → 중하 (짧은 선)
            DrawLine(cx - scale * 0.8f, cy, cx - scale * 0.1f, cy + scale * 0.7f, 2f, checkColor);
            // 선분 2: 중하 → 우상 (긴 선)
            DrawLine(cx - scale * 0.1f, cy + scale * 0.7f, cx + scale * 0.9f, cy - scale * 0.5f, 2f, checkColor);
        }

        /// <summary>
        /// 두 점 사이에 직선을 그립니다 (Bresenham 근사, IMGUI용).
        /// </summary>
        private static void DrawLine(float x0, float y0, float x1, float y1, float thickness, Color color)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            int steps = Mathf.Max(1, (int)(length * 2f));

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float px = Mathf.Lerp(x0, x1, t);
                float py = Mathf.Lerp(y0, y1, t);
                DrawRect(new Rect(px - thickness * 0.5f, py - thickness * 0.5f, thickness, thickness), color);
            }
        }

        // ─── 헬퍼 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 단색 사각형을 그립니다 (IMGUI용).
        /// </summary>
        private static void DrawRect(Rect rect, Color color)
        {
            Color original = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = original;
        }

        /// <summary>
        /// 영문 단계명을 한글로 변환합니다.
        /// </summary>
        private static string TranslateStageName(string stage, bool isSuccess)
        {
            if (!isSuccess) return $"오류 발생 ({stage})";

            return stage switch
            {
                "screenshot" => "스크린샷 캡처 중...",
                "logs"       => "로그 수집 중...",
                "state"      => "상태 저장 중...",
                "video"      => "영상 저장 중...",
                "complete"   => "캡처 완료!",
                _            => $"{stage} 처리 중...",
            };
        }

        /// <summary>
        /// IMGUI 스타일을 초기화합니다. OnGUI 내부에서 한 번만 호출됩니다.
        /// </summary>
        private void InitializeStyles()
        {
            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.blackTexture }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = Color.white },
            };

            _progressBarBgStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.grayTexture }
            };

            _progressBarFillStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.whiteTexture }
            };
        }
    }
}
