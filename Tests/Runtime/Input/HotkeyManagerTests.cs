using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// HotkeyManager 단위 테스트.
    /// </summary>
    [TestFixture]
    public class HotkeyManagerTests
    {
        // ──────────────────────────────────────────────────────────────
        // 테스트용 핫키 제공자 스텁
        // ──────────────────────────────────────────────────────────────

        private class AlwaysTriggerProvider : IHotkeyProvider
        {
            public bool IsTriggered(KeyCode key) => true;
            public bool IsHeld(KeyCode key) => true;
            public bool IsCtrlOrCmdHeld() => true;
            public bool IsShiftHeld() => true;
            public bool IsAltHeld() => true;
        }

        private class NeverTriggerProvider : IHotkeyProvider
        {
            public bool IsTriggered(KeyCode key) => false;
            public bool IsHeld(KeyCode key) => false;
            public bool IsCtrlOrCmdHeld() => false;
            public bool IsShiftHeld() => false;
            public bool IsAltHeld() => false;
        }

        private class CountingProvider : IHotkeyProvider
        {
            public int TriggerCount { get; private set; }
            private bool _shouldTrigger;

            public void SetShouldTrigger(bool value) => _shouldTrigger = value;

            public bool IsTriggered(KeyCode key)
            {
                if (_shouldTrigger)
                {
                    TriggerCount++;
                    return true;
                }
                return false;
            }

            // CountingProvider는 홀드 감지에 사용하지 않으므로 false 반환
            public bool IsHeld(KeyCode key) => false;
            public bool IsCtrlOrCmdHeld() => true;
            public bool IsShiftHeld() => true;
            public bool IsAltHeld() => false;
        }

        /// <summary>
        /// 특정 KeyCode에만 반응하는 스텁.
        /// 영상/스크린샷 핫키를 서로 다른 키로 설정했을 때 선택적 발행을 테스트하는 데 사용합니다.
        /// PressKey/ReleaseKey로 IsHeld 상태를 외부에서 제어할 수 있습니다.
        /// </summary>
        private class SelectiveKeyProvider : IHotkeyProvider
        {
            private readonly KeyCode _targetKey;
            private bool _held;

            public SelectiveKeyProvider(KeyCode targetKey)
            {
                _targetKey = targetKey;
                _held = true; // 기본값: 처음 프레임은 홀드 상태로 시작
            }

            /// <summary>키 홀드 상태를 해제합니다 (키를 뗀 것처럼 동작).</summary>
            public void ReleaseKey() => _held = false;

            public bool IsTriggered(KeyCode key) => key == _targetKey;
            public bool IsHeld(KeyCode key) => _held && key == _targetKey;
            public bool IsCtrlOrCmdHeld() => true;
            public bool IsShiftHeld() => true;
            public bool IsAltHeld() => false;
        }

        /// <summary>
        /// 메인 키는 항상 눌렸지만 수식키(Ctrl/Cmd, Shift, Alt)는 모두 누르지 않은 스텁.
        /// CheckHotkey 수식키 불일치 시나리오를 테스트하는 데 사용합니다.
        /// </summary>
        private class NoModifierProvider : IHotkeyProvider
        {
            public bool IsTriggered(KeyCode key) => true;
            public bool IsHeld(KeyCode key) => true;
            public bool IsCtrlOrCmdHeld() => false;
            public bool IsShiftHeld() => false;
            public bool IsAltHeld() => false;
        }

        // ──────────────────────────────────────────────────────────────
        // 테스트 픽스처
        // ──────────────────────────────────────────────────────────────

        private GameObject _gameObject;
        private HotkeyManager _manager;
        private RekonSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("HotkeyManagerTest");
            _manager = _gameObject.AddComponent<HotkeyManager>();

            _settings = ScriptableObject.CreateInstance<RekonSettings>();
            _settings.captureHotkey = KeyCode.F12;
            _manager.SetSettings(_settings);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
            Object.DestroyImmediate(_settings);
        }

        // ──────────────────────────────────────────────────────────────
        // 테스트 케이스
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void SetProvider_NullArgument_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => _manager.SetProvider(null));
        }

        [Test]
        public void SetSettings_NullArgument_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => _manager.SetSettings(null));
        }

        [UnityTest]
        public IEnumerator OnCaptureTrigger_WhenProviderTriggered_EventFired()
        {
            // Arrange
            var provider = new AlwaysTriggerProvider();
            _manager.SetProvider(provider);

            bool eventFired = false;
            _manager.OnCaptureTrigger += () => eventFired = true;

            // Act: 한 프레임 대기 (Update 호출)
            yield return null;

            // Assert
            Assert.IsTrue(eventFired, "OnCaptureTrigger 이벤트가 발행되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator OnCaptureTrigger_WhenProviderNotTriggered_EventNotFired()
        {
            // Arrange
            var provider = new NeverTriggerProvider();
            _manager.SetProvider(provider);

            bool eventFired = false;
            _manager.OnCaptureTrigger += () => eventFired = true;

            // Act
            yield return null;

            // Assert
            Assert.IsFalse(eventFired, "OnCaptureTrigger 이벤트가 발행되지 않아야 합니다.");
        }

        [UnityTest]
        public IEnumerator OnCaptureTrigger_MultipleFrames_OnlyFiredWhenTriggered()
        {
            // Arrange
            var provider = new CountingProvider();
            _manager.SetProvider(provider);

            int fireCount = 0;
            _manager.OnCaptureTrigger += () => fireCount++;

            // Act: 2프레임 동안 트리거 없음
            provider.SetShouldTrigger(false);
            yield return null;
            yield return null;

            // 1프레임 동안 트리거
            provider.SetShouldTrigger(true);
            yield return null;

            // 1프레임 다시 트리거 없음
            provider.SetShouldTrigger(false);
            yield return null;

            // Assert
            Assert.AreEqual(1, fireCount, "이벤트는 정확히 1회 발행되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator OnCaptureTrigger_WithNoSettings_NoEventFired()
        {
            // Arrange: 설정이 없는 새 매니저
            var go = new GameObject("NoSettingsManager");
            var manager = go.AddComponent<HotkeyManager>();
            manager.SetProvider(new AlwaysTriggerProvider());
            // SetSettings 호출 안 함 → _settings == null

            bool eventFired = false;
            manager.OnCaptureTrigger += () => eventFired = true;

            // Act
            yield return null;

            // Assert
            Assert.IsFalse(eventFired, "설정이 없으면 이벤트가 발행되지 않아야 합니다.");

            Object.DestroyImmediate(go);
        }

        [UnityTest]
        public IEnumerator OnScreenshotTrigger_스크린샷_핫키_누르면_발행됨()
        {
            // Arrange: 영상 핫키 = F12, 스크린샷 핫키 = F11 (서로 다른 키)
            _settings.captureHotkey = KeyCode.F12;
            _settings.hotkeyCtrlOrCmd = true;
            _settings.hotkeyShift = true;
            _settings.hotkeyAlt = false;

            _settings.screenshotHotkey = KeyCode.F11;
            _settings.screenshotHotkeyCtrlOrCmd = true;
            _settings.screenshotHotkeyShift = true;
            _settings.screenshotHotkeyAlt = false;

            // F11만 반응하는 제공자 → 영상 핫키(F12) 불발, 스크린샷 핫키(F11) 홀드 시작
            var provider = new SelectiveKeyProvider(KeyCode.F11);
            _manager.SetProvider(provider);

            bool screenshotFired = false;
            bool captureFired = false;
            _manager.OnScreenshotTrigger += () => screenshotFired = true;
            _manager.OnCaptureTrigger += () => captureFired = true;

            // Act: 1프레임(홀드 시작) → 키 뗌 → 1프레임(OnScreenshotTrigger 발행)
            yield return null;                // 홀드 시작 프레임
            provider.ReleaseKey();            // 키 뗌 시뮬레이션
            yield return null;                // 키 뗌 감지 → 짧은 누름 → 트리거 발행

            // Assert
            Assert.IsTrue(screenshotFired, "OnScreenshotTrigger 이벤트가 발행되어야 합니다.");
            Assert.IsFalse(captureFired, "OnCaptureTrigger 이벤트는 발행되지 않아야 합니다.");
        }

        [UnityTest]
        public IEnumerator OnScreenshotTrigger_영상_핫키_누르면_발행_안됨()
        {
            // Arrange: 영상 핫키 = F12, 스크린샷 핫키 = F11
            _settings.captureHotkey = KeyCode.F12;
            _settings.hotkeyCtrlOrCmd = true;
            _settings.hotkeyShift = true;
            _settings.hotkeyAlt = false;

            _settings.screenshotHotkey = KeyCode.F11;
            _settings.screenshotHotkeyCtrlOrCmd = true;
            _settings.screenshotHotkeyShift = true;
            _settings.screenshotHotkeyAlt = false;

            // F12만 반응하는 제공자 → 영상 핫키(F12) 발화 → Update에서 return → 스크린샷 홀드 차단
            var provider = new SelectiveKeyProvider(KeyCode.F12);
            _manager.SetProvider(provider);

            bool screenshotFired = false;
            bool captureFired = false;
            _manager.OnScreenshotTrigger += () => screenshotFired = true;
            _manager.OnCaptureTrigger += () => captureFired = true;

            // Act: 영상 핫키는 IsTriggered(GetKeyDown)로 즉시 발화, 스크린샷은 차단
            yield return null;

            // Assert
            Assert.IsTrue(captureFired, "OnCaptureTrigger 이벤트가 발행되어야 합니다.");
            Assert.IsFalse(screenshotFired, "영상 핫키가 우선하므로 OnScreenshotTrigger는 발행되지 않아야 합니다.");
        }

        [UnityTest]
        public IEnumerator 영상_핫키_우선순위_같은_핫키_시_영상만_발행()
        {
            // Arrange: 영상/스크린샷 핫키를 동일하게 설정
            _settings.captureHotkey = KeyCode.F12;
            _settings.hotkeyCtrlOrCmd = true;
            _settings.hotkeyShift = true;
            _settings.hotkeyAlt = false;

            _settings.screenshotHotkey = KeyCode.F12; // 동일 키
            _settings.screenshotHotkeyCtrlOrCmd = true;
            _settings.screenshotHotkeyShift = true;
            _settings.screenshotHotkeyAlt = false;

            // 두 핫키 모두 동일 키 → AlwaysTriggerProvider로 충분
            // 영상 핫키(IsTriggered) 매칭 → return → UpdateScreenshotHold 미호출 → 스크린샷 차단
            var provider = new AlwaysTriggerProvider();
            _manager.SetProvider(provider);

            bool screenshotFired = false;
            bool captureFired = false;
            _manager.OnScreenshotTrigger += () => screenshotFired = true;
            _manager.OnCaptureTrigger += () => captureFired = true;

            // Act: 영상 핫키 IsTriggered 매칭으로 1 프레임에 즉시 발화
            yield return null;

            // Assert: Update 로직상 영상 핫키 매칭 후 return → 스크린샷 이벤트 차단
            Assert.IsTrue(captureFired, "동일 핫키 시 OnCaptureTrigger(영상)가 발행되어야 합니다.");
            Assert.IsFalse(screenshotFired, "동일 핫키 시 OnScreenshotTrigger는 차단되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator CheckHotkey_수식키_불일치_시_트리거_안됨()
        {
            // Arrange: 영상 핫키에 Ctrl/Cmd 필요 설정, 스크린샷 핫키에 Shift 필요 설정
            _settings.captureHotkey = KeyCode.F12;
            _settings.hotkeyCtrlOrCmd = true;  // 필요하지만 NoModifierProvider는 false 반환
            _settings.hotkeyShift = false;
            _settings.hotkeyAlt = false;

            _settings.screenshotHotkey = KeyCode.F11;
            _settings.screenshotHotkeyCtrlOrCmd = false;
            _settings.screenshotHotkeyShift = true;  // 필요하지만 NoModifierProvider는 false 반환
            _settings.screenshotHotkeyAlt = false;

            // 수식키를 절대 누르지 않는 제공자 → 두 핫키 모두 수식키 불일치
            var provider = new NoModifierProvider();
            _manager.SetProvider(provider);

            bool screenshotFired = false;
            bool captureFired = false;
            _manager.OnScreenshotTrigger += () => screenshotFired = true;
            _manager.OnCaptureTrigger += () => captureFired = true;

            // Act
            yield return null;

            // Assert
            Assert.IsFalse(captureFired, "수식키 불일치 시 OnCaptureTrigger가 발행되지 않아야 합니다.");
            Assert.IsFalse(screenshotFired, "수식키 불일치 시 OnScreenshotTrigger가 발행되지 않아야 합니다.");
        }

        [Test]
        public void LegacyInputProvider_IsTriggered_ReturnsBool()
        {
            // LegacyInputProvider가 IHotkeyProvider를 올바르게 구현하는지 확인
            var provider = new LegacyInputProvider();
            // Play Mode 테스트에서 실제 키 입력 없이 호출 → false여야 함
            bool result = provider.IsTriggered(KeyCode.F12);
            Assert.IsFalse(result, "키 입력 없이는 false를 반환해야 합니다.");
        }

        [Test]
#if ENABLE_INPUT_SYSTEM
        public void NewInputSystemProvider_IsTriggered_ReturnsBool()
        {
            // NewInputSystemProvider가 IHotkeyProvider를 올바르게 구현하는지 확인
            var provider = new NewInputSystemProvider();
            bool result = provider.IsTriggered(KeyCode.F12);
            // New Input System 비활성 또는 키보드 없으면 false
            Assert.IsFalse(result, "키 입력 없이는 false를 반환해야 합니다.");
        }
#else
        public void NewInputSystemProvider_IsTriggered_ReturnsBool()
        {
            // New Input System이 비활성화된 환경에서는 테스트 건너뜀
            Assert.Ignore("ENABLE_INPUT_SYSTEM 심볼이 없어 NewInputSystemProvider를 테스트할 수 없습니다.");
        }
#endif
    }
}
