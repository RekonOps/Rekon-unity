using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GaoZombie.BugBeacon;

namespace GaoZombie.BugBeacon.Tests
{
    /// <summary>
    /// ContextProviderRegistry 및 BugBeaconContext 단위 테스트.
    /// </summary>
    [TestFixture]
    public class ContextProviderTests
    {
        private ContextProviderRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new ContextProviderRegistry();
            BugBeaconContext.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            BugBeaconContext.Clear();
        }

        // ──────────────────────────────────────────────────────────────
        // ContextProviderRegistry 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Register_NullProvider_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => _registry.Register(null));
        }

        [Test]
        public void Unregister_NullProvider_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => _registry.Unregister(null));
        }

        [Test]
        public void Register_ValidProvider_CountIncreases()
        {
            var p = new DummyProvider(new Dictionary<string, string>());
            _registry.Register(p);
            Assert.AreEqual(1, _registry.Count);
        }

        [Test]
        public void Register_SameProviderTwice_NotDuplicated()
        {
            var p = new DummyProvider(new Dictionary<string, string>());
            _registry.Register(p);
            _registry.Register(p);
            Assert.AreEqual(1, _registry.Count, "동일 프로바이더 중복 등록 방지");
        }

        [Test]
        public void Unregister_RegisteredProvider_CountDecreases()
        {
            var p = new DummyProvider(new Dictionary<string, string>());
            _registry.Register(p);
            _registry.Unregister(p);
            Assert.AreEqual(0, _registry.Count);
        }

        [Test]
        public void Unregister_NotRegisteredProvider_NoError()
        {
            var p = new DummyProvider(new Dictionary<string, string>());
            Assert.DoesNotThrow(() => _registry.Unregister(p));
        }

        [Test]
        public void CollectAll_NoProviders_ReturnsEmptyDictionary()
        {
            var result = _registry.CollectAll();
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void CollectAll_SingleProvider_ReturnsData()
        {
            var data = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" },
            };
            _registry.Register(new DummyProvider(data));

            var result = _registry.CollectAll();

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("value1", result["key1"]);
            Assert.AreEqual("value2", result["key2"]);
        }

        [Test]
        public void CollectAll_MultipleProviders_MergesData()
        {
            _registry.Register(new DummyProvider(new Dictionary<string, string>
            {
                { "a", "1" },
                { "b", "2" },
            }));
            _registry.Register(new DummyProvider(new Dictionary<string, string>
            {
                { "c", "3" },
            }));

            var result = _registry.CollectAll();

            Assert.AreEqual(3, result.Count);
        }

        [Test]
        public void CollectAll_KeyConflict_LaterProviderWins()
        {
            _registry.Register(new DummyProvider(new Dictionary<string, string>
            {
                { "key", "first" },
            }));
            _registry.Register(new DummyProvider(new Dictionary<string, string>
            {
                { "key", "second" },
            }));

            var result = _registry.CollectAll();

            Assert.AreEqual("second", result["key"], "나중 등록 프로바이더가 우선해야 합니다.");
        }

        [Test]
        public void CollectAll_ProviderThrowsException_OtherProvidersStillCollected()
        {
            _registry.Register(new ThrowingProvider());
            _registry.Register(new DummyProvider(new Dictionary<string, string> { { "ok", "yes" } }));

            Dictionary<string, string> result = null;
            // 예외 프로바이더가 있어도 전체가 실패하지 않아야 함
            LogAssert.ignoreFailingMessages = true;
            Assert.DoesNotThrow(() => result = _registry.CollectAll());
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(result);
            Assert.IsTrue(result.ContainsKey("ok"));
        }

        [Test]
        public void CollectAll_NullReturningProvider_SkippedGracefully()
        {
            _registry.Register(new NullReturningProvider());
            _registry.Register(new DummyProvider(new Dictionary<string, string> { { "valid", "data" } }));

            var result = _registry.CollectAll();

            Assert.IsTrue(result.ContainsKey("valid"), "null 반환 프로바이더는 건너뛰어야 합니다.");
        }

        [Test]
        public void Clear_RemovesAllProviders()
        {
            _registry.Register(new DummyProvider(new Dictionary<string, string>()));
            _registry.Register(new DummyProvider(new Dictionary<string, string>()));
            _registry.Clear();

            Assert.AreEqual(0, _registry.Count);
        }

        // ──────────────────────────────────────────────────────────────
        // BugBeaconContext 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void BugBeaconContext_Add_StoresValue()
        {
            BugBeaconContext.Add("testKey", "testValue");
            var snapshot = BugBeaconContext.GetSnapshot();
            Assert.IsTrue(snapshot.ContainsKey("testKey"));
            Assert.AreEqual("testValue", snapshot["testKey"]);
        }

        [Test]
        public void BugBeaconContext_Add_UpdatesExistingKey()
        {
            BugBeaconContext.Add("key", "first");
            BugBeaconContext.Add("key", "second");
            var snapshot = BugBeaconContext.GetSnapshot();
            Assert.AreEqual("second", snapshot["key"]);
        }

        [Test]
        public void BugBeaconContext_Add_NullKey_ThrowsException()
        {
            Assert.Throws<System.ArgumentNullException>(() => BugBeaconContext.Add(null, "value"));
            Assert.Throws<System.ArgumentNullException>(() => BugBeaconContext.Add("", "value"));
        }

        [Test]
        public void BugBeaconContext_Remove_DeletesKey()
        {
            BugBeaconContext.Add("removeMe", "value");
            BugBeaconContext.Remove("removeMe");
            var snapshot = BugBeaconContext.GetSnapshot();
            Assert.IsFalse(snapshot.ContainsKey("removeMe"));
        }

        [Test]
        public void BugBeaconContext_Remove_NonExistentKey_NoError()
        {
            Assert.DoesNotThrow(() => BugBeaconContext.Remove("nonexistent"));
        }

        [Test]
        public void BugBeaconContext_Clear_RemovesAll()
        {
            BugBeaconContext.Add("a", "1");
            BugBeaconContext.Add("b", "2");
            BugBeaconContext.Clear();
            Assert.AreEqual(0, BugBeaconContext.Count);
        }

        [Test]
        public void BugBeaconContext_AsProvider_ReturnsContextData()
        {
            BugBeaconContext.Add("level", "42");
            var provider = BugBeaconContext.AsProvider();
            var context = provider.GetContext();
            Assert.IsTrue(context.ContainsKey("level"));
            Assert.AreEqual("42", context["level"]);
        }

        [Test]
        public void BugBeaconContext_GetSnapshot_ReturnsCopy()
        {
            BugBeaconContext.Add("original", "value");
            var snapshot = BugBeaconContext.GetSnapshot();

            // 스냅샷 수정이 원본에 영향을 주지 않아야 함
            snapshot["original"] = "modified";
            var snapshot2 = BugBeaconContext.GetSnapshot();
            Assert.AreEqual("value", snapshot2["original"], "스냅샷은 복사본이어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        private class DummyProvider : IContextProvider
        {
            private readonly Dictionary<string, string> _data;
            public DummyProvider(Dictionary<string, string> data) => _data = data;
            public Dictionary<string, string> GetContext() => _data;
        }

        private class ThrowingProvider : IContextProvider
        {
            public Dictionary<string, string> GetContext()
                => throw new System.InvalidOperationException("의도적인 테스트 예외");
        }

        private class NullReturningProvider : IContextProvider
        {
            public Dictionary<string, string> GetContext() => null;
        }
    }
}
