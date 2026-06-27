using System;
using UnityEngine;

namespace RekonOps.Rekon
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

        // ─── 스크린샷 미니 바 상수 ───────────────────────────────────────────────

        /// <summary>미니 바 너비 (픽셀) — 좌하단, 기존 오버레이(우하단)와 겹치지 않음</summary>
        private const float MiniBarWidth = 150f;

        /// <summary>미니 바 높이 (픽셀)</summary>
        private const float MiniBarHeight = 30f;

        /// <summary>미니 바 좌측 여백 (픽셀)</summary>
        private const float MiniBarMarginX = 16f;

        /// <summary>미니 바 하단 여백 (픽셀)</summary>
        private const float MiniBarMarginY = 16f;

        // ─── 토스트 상태 ──────────────────────────────────────────────────────────

        /// <summary>토스트 메시지 텍스트 (빈 문자열이면 비표시)</summary>
        private string _toastText = "";

        /// <summary>토스트 잔여 표시 시간 (초)</summary>
        private float _toastTimer = 0f;

        /// <summary>토스트 표시 지속 시간 (초)</summary>
        private const float ToastDuration = 2f;

        // ─── 롱프레스 홀드 상태 ───────────────────────────────────────────────────

        /// <summary>홀드 진행률 (0.0~1.0). 0이면 미표시.</summary>
        private float _holdProgress = 0f;

        /// <summary>홀드 프로그레스 바 너비 (픽셀)</summary>
        private const float HoldBarWidth = 280f;

        /// <summary>홀드 프로그레스 바 높이 (픽셀)</summary>
        private const float HoldBarHeight = 44f;

        /// <summary>홀드 프로그레스 바 하단 여백 (픽셀, 토스트 위)</summary>
        private const float HoldBarMarginBottom = 100f;

        // ─── 바인딩된 HotkeyManager ───────────────────────────────────────────────

        private HotkeyManager _hotkeyManager;

        // ─── GUI 스타일 캐시 ──────────────────────────────────────────────────────

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _progressBarBgStyle;
        private GUIStyle _progressBarFillStyle;
        private GUIStyle _miniBarLabelStyle;
        private GUIStyle _miniBarBoxStyle;
        private GUIStyle _miniBarCloseBtnStyle;
        private Texture2D _miniBarBgTex;
        private bool _stylesInitialized = false;

        // ─── 바인딩된 오케스트레이터 ─────────────────────────────────────────────

        private ICaptureOrchestrator _orchestrator;

        // ─── 바인딩된 스크린샷 큐 ────────────────────────────────────────────────

        private ScreenshotQueue _screenshotQueue;

        // ─── 바인딩된 설정 ────────────────────────────────────────────────────────

        private RekonSettings _settings;

        // ─── 정적 팩토리 ──────────────────────────────────────────────────────────

        /// <summary>
        /// CaptureOverlay 인스턴스를 반환합니다. 없으면 생성합니다.
        /// DontDestroyOnLoad로 씬 전환 시에도 유지됩니다.
        /// </summary>
        public static CaptureOverlay EnsureInstance()
        {
            CaptureOverlay existing = FindObjectOfType<CaptureOverlay>();
            if (existing != null) return existing;

            GameObject go = new GameObject("[Rekon] CaptureOverlay");
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

                // OnScreenshotQueued / OnScreenshotSubmitCompleted 이벤트 해제
                if (_orchestrator is CaptureOrchestrator concreteOrch)
                {
                    concreteOrch.OnScreenshotQueued -= HandleScreenshotQueued;
                    concreteOrch.OnScreenshotSubmitCompleted -= HandleScreenshotSubmitCompleted;
                }
            }

            _orchestrator = orchestrator;

            if (_orchestrator != null)
            {
                _orchestrator.OnProgress += HandleProgress;

                // OnScreenshotQueued / OnScreenshotSubmitCompleted 이벤트 바인딩
                if (_orchestrator is CaptureOrchestrator concreteOrch2)
                {
                    concreteOrch2.OnScreenshotQueued += HandleScreenshotQueued;
                    concreteOrch2.OnScreenshotSubmitCompleted += HandleScreenshotSubmitCompleted;
                }

                Debug.Log("[Rekon] CaptureOverlay: 오케스트레이터 바인딩 완료");
            }
        }

        /// <summary>
        /// ScreenshotQueue를 바인딩합니다.
        /// 미니 바에 현재 큐 잔량을 표시하는 데 사용됩니다.
        /// </summary>
        /// <param name="queue">연동할 스크린샷 큐</param>
        public void BindScreenshotQueue(ScreenshotQueue queue)
        {
            _screenshotQueue = queue;
        }

        /// <summary>
        /// HotkeyManager를 바인딩합니다.
        /// 롱프레스 진행률 이벤트를 구독하여 프로그레스 바를 표시합니다.
        /// </summary>
        /// <param name="hotkeyManager">연동할 HotkeyManager</param>
        public void BindHotkeyManager(HotkeyManager hotkeyManager)
        {
            // 기존 바인딩 해제
            if (_hotkeyManager != null)
            {
                _hotkeyManager.OnScreenshotHoldProgress -= HandleScreenshotHoldProgress;
                _hotkeyManager.OnScreenshotLongPress -= HandleScreenshotLongPress;
            }

            _hotkeyManager = hotkeyManager;

            if (_hotkeyManager != null)
            {
                _hotkeyManager.OnScreenshotHoldProgress += HandleScreenshotHoldProgress;
                _hotkeyManager.OnScreenshotLongPress += HandleScreenshotLongPress;
                Debug.Log("[Rekon] CaptureOverlay: HotkeyManager 바인딩 완료");
            }
        }

        /// <summary>
        /// RekonSettings를 바인딩합니다.
        /// 미니 바 표시 위치 등 설정 값을 참조하는 데 사용됩니다.
        /// </summary>
        /// <param name="settings">연동할 설정 오브젝트</param>
        public void BindSettings(RekonSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Silent 모드를 설정합니다.
        /// Silent 모드에서는 간소화된 최소 인디케이터만 표시됩니다.
        /// </summary>
        /// <param name="silent">true: 간소화 UI, false: 기존 상세 UI</param>
        public void SetSilentMode(bool silent)
        {
            _silentMode = silent;
            Debug.Log($"[Rekon] CaptureOverlay: Silent 모드 {(silent ? "활성화" : "비활성화")}");
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

                // OnScreenshotQueued, OnScreenshotSubmitCompleted 이벤트 해제
                if (_orchestrator is CaptureOrchestrator concreteOrch)
                {
                    concreteOrch.OnScreenshotQueued -= HandleScreenshotQueued;
                    concreteOrch.OnScreenshotSubmitCompleted -= HandleScreenshotSubmitCompleted;
                }
            }

            if (_hotkeyManager != null)
            {
                _hotkeyManager.OnScreenshotHoldProgress -= HandleScreenshotHoldProgress;
                _hotkeyManager.OnScreenshotLongPress -= HandleScreenshotLongPress;
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

            // 토스트 타이머 감소
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f) _toastText = "";
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

            // ── 스크린샷 미니 바 (좌하단) ──────────────────────────────────────────
            DrawScreenshotMiniBar();

            // ── 롱프레스 홀드 프로그레스 바 (화면 하단 중앙) ───────────────────────
            if (_holdProgress > 0f)
                DrawScreenshotHoldProgress();

            // ── 토스트 (화면 하단 중앙) ────────────────────────────────────────────
            DrawScreenshotToast();
        }

        // ─── 이벤트 핸들러 ────────────────────────────────────────────────────────

        /// <summary>
        /// CaptureOrchestrator.OnScreenshotQueued 이벤트 핸들러.
        /// 스크린샷이 큐에 추가될 때마다 토스트를 표시합니다.
        /// </summary>
        private void HandleScreenshotQueued(int count, bool evicted)
        {
            int cap = _screenshotQueue != null ? _screenshotQueue.Capacity : count;
            if (evicted)
                _toastText = $"캡처 완료 ({count}/{cap}) — 가장 오래된 스크린샷이 교체됨";
            else
                _toastText = $"캡처 완료 ({count}/{cap})";
            _toastTimer = ToastDuration;
        }

        /// <summary>
        /// HotkeyManager.OnScreenshotHoldProgress 이벤트 핸들러.
        /// 0이면 홀드 취소(기존 스크린샷 캡처로 처리됨), 0 초과면 홀드 진행 중.
        /// </summary>
        private void HandleScreenshotHoldProgress(float progress)
        {
            _holdProgress = progress;

            // 홀드 시작 시 토스트 표시 (0 → 양수 전환 시 한 번만)
            if (progress > 0f && _toastTimer <= 0f)
            {
                _toastText  = "리포트 발송 중... (1초 유지)";
                _toastTimer = 1.5f; // 롱프레스 임계값(1초)보다 조금 길게 유지
            }

            // 홀드 취소(0으로 리셋) → 토스트 제거
            if (progress <= 0f)
            {
                // 롱프레스 완료 토스트가 표시 중이면 그대로 유지
                if (_toastText == "리포트 발송 중... (1초 유지)")
                {
                    _toastText  = "";
                    _toastTimer = 0f;
                }
            }
        }

        /// <summary>
        /// HotkeyManager.OnScreenshotLongPress 이벤트 핸들러.
        /// 1초 롱프레스 완료 시 홀드 프로그레스 바만 리셋합니다.
        /// 실제 토스트는 발송 결과(OnScreenshotSubmitCompleted) 이벤트에서 표시됩니다.
        /// </summary>
        private void HandleScreenshotLongPress()
        {
            _holdProgress = 0f;
        }

        /// <summary>
        /// CaptureOrchestrator.OnScreenshotSubmitCompleted 이벤트 핸들러.
        /// 발송 결과에 따라 성공/실패 토스트를 분기하여 표시합니다.
        /// </summary>
        /// <param name="success">발송 성공 여부</param>
        /// <param name="count">발송된 스크린샷 장수</param>
        private void HandleScreenshotSubmitCompleted(bool success, int count)
        {
            if (success)
            {
                _toastText  = $"리포트 발송 완료 ({count}장)";
            }
            else
            {
                _toastText  = "발송할 스크린샷이 없습니다";
            }
            _toastTimer = ToastDuration;
        }

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
        /// screenshotMiniBarPosition 설정에 따라 미니 바 위치 Rect을 반환합니다.
        /// </summary>
        private Rect GetMiniBarRect()
        {
            float x, y;
            OverlayPosition pos = _settings != null
                ? _settings.screenshotMiniBarPosition
                : OverlayPosition.BottomLeft;

            switch (pos)
            {
                case OverlayPosition.BottomLeft:
                    x = MiniBarMarginX;
                    y = Screen.height - MiniBarHeight - MiniBarMarginY;
                    break;
                case OverlayPosition.BottomRight:
                    x = Screen.width - MiniBarWidth - MiniBarMarginX;
                    y = Screen.height - MiniBarHeight - MiniBarMarginY;
                    break;
                case OverlayPosition.TopLeft:
                    x = MiniBarMarginX;
                    y = MiniBarMarginY;
                    break;
                case OverlayPosition.TopRight:
                    x = Screen.width - MiniBarWidth - MiniBarMarginX;
                    y = MiniBarMarginY;
                    break;
                default:
                    x = MiniBarMarginX;
                    y = Screen.height - MiniBarHeight - MiniBarMarginY;
                    break;
            }
            return new Rect(x, y, MiniBarWidth, MiniBarHeight);
        }

        /// <summary>
        /// 스크린샷 큐 잔량을 미니 바로 표시합니다.
        /// 큐가 비어 있으면 표시하지 않습니다.
        /// 표시 위치는 RekonSettings.screenshotMiniBarPosition 설정을 따릅니다.
        /// [✕] 버튼을 누르면 큐를 비우고 미니 바를 숨깁니다.
        /// </summary>
        private void DrawScreenshotMiniBar()
        {
            if (_screenshotQueue == null || _screenshotQueue.Count == 0) return;

            int count = _screenshotQueue.Count;
            Rect barRect = GetMiniBarRect();

            // ── 배경 박스 (어두운 반투명) ──
            GUI.Box(barRect, GUIContent.none, _miniBarBoxStyle ?? _boxStyle);

            // ── 내부 레이아웃 ──
            const float padX = 10f;
            const float padY = 2f;
            const float closeBtnSize = 22f;
            const float separatorWidth = 1f;
            const float separatorGap = 6f;

            // 텍스트 영역
            float labelWidth = barRect.width - padX - closeBtnSize - separatorWidth - separatorGap * 2f - padX;
            Rect labelRect = new Rect(
                barRect.x + padX,
                barRect.y + padY,
                labelWidth,
                barRect.height - padY * 2f);

            GUI.Label(
                labelRect,
                $"\ud83d\udcf8 {count}/{_screenshotQueue.Capacity}장",
                _miniBarLabelStyle ?? GUI.skin.label);

            // 세로 구분선
            float sepX = barRect.x + padX + labelWidth + separatorGap;
            Rect separatorRect = new Rect(sepX, barRect.y + 5f, separatorWidth, barRect.height - 10f);
            DrawRect(separatorRect, new Color(1f, 1f, 1f, 0.25f));

            // ✕ 버튼 (구분선 우측)
            Rect closeBtnRect = new Rect(
                sepX + separatorWidth + separatorGap,
                barRect.y + (barRect.height - closeBtnSize) * 0.5f,
                closeBtnSize,
                closeBtnSize);

            if (GUI.Button(closeBtnRect, "\u2715", _miniBarCloseBtnStyle ?? GUI.skin.label))
            {
                _screenshotQueue.Clear();
                Debug.Log("[Rekon] 스크린샷 큐 수동 삭제");
            }
        }

        /// <summary>
        /// 스크린샷 핫키 홀드 진행률을 화면 하단 중앙에 프로그레스 바로 표시합니다.
        /// _holdProgress가 0이면 표시하지 않습니다.
        /// </summary>
        private void DrawScreenshotHoldProgress()
        {
            float barW = HoldBarWidth;
            float barH = HoldBarHeight;
            float x = (Screen.width - barW) * 0.5f;
            float y = Screen.height - barH - HoldBarMarginBottom;
            Rect boxRect = new Rect(x, y, barW, barH);

            // 반투명 배경 박스
            Color original = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.80f);
            GUI.Box(boxRect, GUIContent.none, _boxStyle);
            GUI.color = original;

            float padding = 8f;
            float innerX = x + padding;
            float innerW = barW - padding * 2f;

            // 안내 텍스트
            Rect textRect = new Rect(innerX, y + 5f, innerW, 16f);
            GUI.Label(textRect, "리포트 발송 중... (1초 유지)", _labelStyle ?? GUI.skin.label);

            // 프로그레스 바 배경
            float fillY = y + 24f;
            Rect bgBar = new Rect(innerX, fillY, innerW, 10f);
            GUI.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            GUI.Box(bgBar, GUIContent.none, _progressBarBgStyle ?? GUI.skin.box);
            GUI.color = original;

            // 프로그레스 바 채우기 (오렌지 계열 — 스크린샷 캡처 녹색과 구분)
            float fillWidth = Mathf.Max(4f, innerW * Mathf.Clamp01(_holdProgress));
            Rect fillBar = new Rect(innerX, fillY, fillWidth, 10f);
            GUI.color = new Color(1.0f, 0.65f, 0.0f, 1f);
            GUI.Box(fillBar, GUIContent.none, _progressBarFillStyle ?? GUI.skin.box);
            GUI.color = original;
        }

        /// <summary>
        /// 스크린샷 큐 추가 시 화면 하단 중앙에 토스트 메시지를 표시합니다.
        /// _toastTimer 가 0 이하면 렌더링을 건너뜁니다.
        /// </summary>
        private void DrawScreenshotToast()
        {
            if (_toastTimer <= 0f || string.IsNullOrEmpty(_toastText)) return;

            // 텍스트 길이에 따라 너비를 동적으로 계산 (잘림 방지)
            GUIStyle boxStyle = GUI.skin.box;
            Vector2 textSize = boxStyle.CalcSize(new GUIContent(_toastText));
            float toastWidth  = Mathf.Max(200f, textSize.x + 32f);
            float toastHeight = 30f;
            float x = (Screen.width  - toastWidth)  / 2f;
            float y =  Screen.height - toastHeight   - 60f;
            Rect toastRect = new Rect(x, y, toastWidth, toastHeight);

            // 페이드 아웃 효과 (잔여 시간 0.5초 이하부터 투명해짐)
            float alpha = Mathf.Clamp01(_toastTimer / 0.5f);
            Color prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Box(toastRect, _toastText);
            GUI.color = prevColor;
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

            _miniBarLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = Color.white },
            };

            // 미니 바 배경 텍스처 (라운드 느낌의 어두운 반투명)
            _miniBarBgTex = new Texture2D(1, 1);
            _miniBarBgTex.SetPixel(0, 0, new Color(0.12f, 0.12f, 0.14f, 0.88f));
            _miniBarBgTex.Apply();

            _miniBarBoxStyle = new GUIStyle()
            {
                normal    = { background = _miniBarBgTex },
                border    = new RectOffset(4, 4, 4, 4),
                padding   = new RectOffset(8, 8, 4, 4),
            };

            _miniBarCloseBtnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(1f, 1f, 1f, 0.7f) },
                hover     = { textColor = Color.white },
            };
        }
    }
}
