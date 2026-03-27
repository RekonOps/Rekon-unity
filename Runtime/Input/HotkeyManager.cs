using System;
using UnityEngine;

namespace RekonOps.Rekon
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
        private RekonSettings _settings;

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
