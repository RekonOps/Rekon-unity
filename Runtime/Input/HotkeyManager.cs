using System;
using UnityEngine;

namespace RekonOps.BugOneTouch
{
    /// <summary>
    /// 핫키 입력을 감지하여 OnCaptureTrigger 이벤트를 발행하는 MonoBehaviour.
    /// Play Mode에서만 동작하며, Edit Mode에서는 입력 처리를 건너뜁니다.
    /// </summary>
    public class HotkeyManager : MonoBehaviour
    {
        /// <summary>
        /// 캡처 핫키가 눌렸을 때 발행되는 이벤트.
        /// </summary>
        public event Action OnCaptureTrigger;

        [SerializeField]
        [Tooltip("사용할 버그 캡처 설정 에셋")]
        private BugOneTouchSettings _settings;

        private IHotkeyProvider _provider;

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
        public void SetSettings(BugOneTouchSettings settings)
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
            if (!Application.isPlaying)
                return;

            if (_settings == null)
                return;

            if (_provider == null)
                return;

            // 메인 키 트리거 확인
            if (!_provider.IsTriggered(_settings.captureHotkey)) return;

            // 수식키 조합 확인
            if (_settings.hotkeyCtrlOrCmd && !_provider.IsCtrlOrCmdHeld()) return;
            if (_settings.hotkeyShift && !_provider.IsShiftHeld()) return;
            if (_settings.hotkeyAlt && !_provider.IsAltHeld()) return;

            OnCaptureTrigger?.Invoke();
        }

        /// <summary>
        /// 현재 환경에 맞는 기본 핫키 제공자를 생성합니다.
        /// New Input System이 활성화되어 있으면 NewInputSystemProvider,
        /// 그렇지 않으면 LegacyInputProvider를 반환합니다.
        /// </summary>
        private static IHotkeyProvider CreateDefaultProvider()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return new NewInputSystemProvider();
#else
            return new LegacyInputProvider();
#endif
        }
    }
}
