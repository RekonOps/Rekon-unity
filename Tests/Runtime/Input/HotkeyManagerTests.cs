using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.BugOneTouch;

namespace RekonOps.BugOneTouch.Tests
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
        }

        private class NeverTriggerProvider : IHotkeyProvider
        {
            public bool IsTriggered(KeyCode key) => false;
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
        }

        // ──────────────────────────────────────────────────────────────
        // 테스트 픽스처
        // ──────────────────────────────────────────────────────────────

        private GameObject _gameObject;
        private HotkeyManager _manager;
        private BugOneTouchSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("HotkeyManagerTest");
            _manager = _gameObject.AddComponent<HotkeyManager>();

            _settings = ScriptableObject.CreateInstance<BugOneTouchSettings>();
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
        public void NewInputSystemProvider_IsTriggered_ReturnsBool()
        {
            // NewInputSystemProvider가 IHotkeyProvider를 올바르게 구현하는지 확인
            var provider = new NewInputSystemProvider();
            bool result = provider.IsTriggered(KeyCode.F12);
            // New Input System 비활성 또는 키보드 없으면 false
            Assert.IsFalse(result, "키 입력 없이는 false를 반환해야 합니다.");
        }
    }
}
