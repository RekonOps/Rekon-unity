using System;
using UnityEngine;

namespace RekonOps.Rekon
{
    /// <summary>
    /// 핫키 입력을 감지하여 OnCaptureTrigger / OnScreenshotTrigger 이벤트를 발행하는 MonoBehaviour.
    /// Play Mode에서만 동작하며, Edit Mode에서는 입력 처리를 건너뜁니다.
    ///
    /// 스크린샷 핫키 롱프레스 동작:
    ///   - 짧게 누름 (< 1초): OnScreenshotTrigger 발행 (스크린샷 캡처)
    ///   - 1초 홀드: OnScreenshotLongPress 발행 (리포트 발송)
    ///   - 홀드 중 매 프레임: OnScreenshotHoldProgress 발행 (0.0~1.0 진행률)
    /// </summary>
    public class HotkeyManager : MonoBehaviour
    {
        /// <summary>
        /// 영상 캡처 핫키가 눌렸을 때 발행되는 이벤트.
        /// </summary>
        public event Action OnCaptureTrigger;

        /// <summary>
        /// 스크린샷 핫키가 짧게 눌렸을 때 발행되는 이벤트 (< 1초).
        /// </summary>
        public event Action OnScreenshotTrigger;

        /// <summary>
        /// 스크린샷 핫키 홀드 진행률 이벤트. 0.0~1.0 범위. 0이면 홀드 취소.
        /// </summary>
        public event Action<float> OnScreenshotHoldProgress;

        /// <summary>
        /// 스크린샷 핫키 1초 롱프레스 완료 시 발행되는 이벤트.
        /// </summary>
        public event Action OnScreenshotLongPress;

        [SerializeField]
        [Tooltip("사용할 버그 캡처 설정 에셋")]
        private RekonSettings _settings;

        private IHotkeyProvider _provider;

        // ── 롱프레스 상태 ───────────────────────────────────────────────────────

        /// <summary>스크린샷 핫키 홀드 타이머 (초)</summary>
        private float _screenshotHoldTimer = 0f;

        /// <summary>현재 홀드 중 여부</summary>
        private bool _screenshotHolding = false;

        /// <summary>이번 홀드에서 롱프레스가 이미 발행되었는지 여부 (중복 발행 방지)</summary>
        private bool _screenshotLongPressTriggered = false;

        /// <summary>롱프레스 임계값 (초)</summary>
        private const float LongPressThreshold = 1f;

        /// <summary>
        /// 핫키 제공자를 외부에서 주입합니다 (테스트 및 DI 지원).
        /// </summary>
        /// <param name="provider">사용할 핫키 제공자 구현체</param>
        public void SetProvider(IHotkeyProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// 설정을 외부에서 주입합니다 (테스트 지원).
        /// </summary>
        /// <param name="settings">사용할 설정 에셋</param>
        public void SetSettings(RekonSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        private void Awake()
        {
            // 제공자가 미리 주입되지 않은 경우 기본값 설정
            if (_provider == null)
            {
                _provider = CreateDefaultProvider();
            }
        }

        private void Update()
        {
            // Edit Mode 또는 설정 미지정 시 처리 건너뜀
            if (!Application.isPlaying || _settings == null || _provider == null)
                return;

            // 영상 캡처 핫키 체크 — 매칭되면 스크린샷 이벤트 차단
            if (CheckHotkey(_settings.captureHotkey, _settings.hotkeyCtrlOrCmd, _settings.hotkeyShift, _settings.hotkeyAlt))
            {
                // 영상 핫키가 눌리면 스크린샷 홀드 상태 초기화 (영상 핫키 우선)
                if (_screenshotHolding)
                    ResetScreenshotHoldState();

                OnCaptureTrigger?.Invoke();
                return;
            }

            // 스크린샷 핫키 홀드 감지
            UpdateScreenshotHold();
        }

        /// <summary>
        /// 스크린샷 핫키의 홀드/롱프레스 상태를 매 프레임 갱신합니다.
        /// </summary>
        private void UpdateScreenshotHold()
        {
            bool modifiersHeld = CheckScreenshotModifiers();
            bool keyHeld = modifiersHeld && _provider.IsHeld(_settings.screenshotHotkey);

            if (keyHeld)
            {
                if (!_screenshotHolding)
                {
                    // 홀드 시작
                    _screenshotHolding = true;
                    _screenshotHoldTimer = 0f;
                    _screenshotLongPressTriggered = false;
                }

                // 롱프레스가 이미 발행된 경우 타이머 증가 중단
                if (!_screenshotLongPressTriggered)
                {
                    _screenshotHoldTimer += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(_screenshotHoldTimer / LongPressThreshold);
                    OnScreenshotHoldProgress?.Invoke(progress);

                    if (_screenshotHoldTimer >= LongPressThreshold)
                    {
                        // 1초 롱프레스 완료
                        _screenshotLongPressTriggered = true;
                        OnScreenshotHoldProgress?.Invoke(1f);
                        OnScreenshotLongPress?.Invoke();
                        Debug.Log("[Rekon] 스크린샷 핫키 롱프레스 완료 — 리포트 발송 트리거");
                    }
                }
            }
            else if (_screenshotHolding)
            {
                // 키를 뗐을 때 처리
                bool wasLongPress = _screenshotLongPressTriggered;
                ResetScreenshotHoldState();

                if (!wasLongPress)
                {
                    // 짧게 눌렀다가 뗀 경우 → 스크린샷 캡처
                    OnScreenshotTrigger?.Invoke();
                }
                // 롱프레스 완료 후 키를 뗀 경우 → 아무것도 안 함
            }
        }

        /// <summary>
        /// 스크린샷 홀드 상태를 초기화하고 진행률 0을 발행합니다.
        /// </summary>
        private void ResetScreenshotHoldState()
        {
            _screenshotHolding = false;
            _screenshotHoldTimer = 0f;
            _screenshotLongPressTriggered = false;
            OnScreenshotHoldProgress?.Invoke(0f);
        }

        /// <summary>
        /// 스크린샷 핫키의 수식키 조건이 충족되는지 확인합니다.
        /// </summary>
        private bool CheckScreenshotModifiers()
        {
            if (_settings.screenshotHotkeyCtrlOrCmd && !_provider.IsCtrlOrCmdHeld()) return false;
            if (_settings.screenshotHotkeyShift && !_provider.IsShiftHeld()) return false;
            if (_settings.screenshotHotkeyAlt && !_provider.IsAltHeld()) return false;
            return true;
        }

        /// <summary>
        /// 지정된 키 조합이 현재 프레임에 입력되었는지 확인합니다.
        /// </summary>
        /// <param name="key">감지할 메인 키</param>
        /// <param name="requireCtrlOrCmd">Ctrl/Cmd 수식키 필요 여부</param>
        /// <param name="requireShift">Shift 수식키 필요 여부</param>
        /// <param name="requireAlt">Alt 수식키 필요 여부</param>
        /// <returns>조합 전체가 일치하면 true</returns>
        private bool CheckHotkey(KeyCode key, bool requireCtrlOrCmd, bool requireShift, bool requireAlt)
        {
            if (!_provider.IsTriggered(key)) return false;
            if (requireCtrlOrCmd && !_provider.IsCtrlOrCmdHeld()) return false;
            if (requireShift && !_provider.IsShiftHeld()) return false;
            if (requireAlt && !_provider.IsAltHeld()) return false;
            return true;
        }

        /// <summary>
        /// 현재 환경에 맞는 기본 핫키 제공자를 생성합니다.
        /// New Input System이 활성화되어 있으면 NewInputSystemProvider(리플렉션),
        /// 그렇지 않으면 LegacyInputProvider를 반환합니다.
        /// NewInputSystemProvider는 별도 어셈블리(Rekon.Runtime.InputSystem)에 있으므로
        /// 직접 참조 대신 리플렉션으로 생성합니다.
        /// </summary>
        private static IHotkeyProvider CreateDefaultProvider()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // NewInputSystemProvider는 Rekon.Runtime.InputSystem 어셈블리에 있으므로 리플렉션으로 생성
            var type = System.Type.GetType(
                "RekonOps.Rekon.NewInputSystemProvider, Rekon.Runtime.InputSystem");
            if (type != null)
                return (IHotkeyProvider)System.Activator.CreateInstance(type);
#endif
            return new LegacyInputProvider();
        }
    }
}
