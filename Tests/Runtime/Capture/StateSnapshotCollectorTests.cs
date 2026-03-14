using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GaoZombie.BugBeacon;

namespace GaoZombie.BugBeacon.Tests
{
    /// <summary>
    /// StateSnapshotCollector 단위 테스트.
    /// </summary>
    [TestFixture]
    public class StateSnapshotCollectorTests
    {
        private ContextProviderRegistry _registry;
        private StateSnapshotCollector _collector;

        [SetUp]
        public void SetUp()
        {
            _registry = new ContextProviderRegistry();
            _collector = new StateSnapshotCollector(_registry);
        }

        // ──────────────────────────────────────────────────────────────
        // 생성자 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Constructor_NullRegistry_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() => new StateSnapshotCollector(null));
        }

        // ──────────────────────────────────────────────────────────────
        // CollectAsync 기본 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator CollectAsync_ReturnsNonNullSnapshot()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotNull(task.Result, "스냅샷은 null이 아니어야 합니다.");
        }

        [UnityTest]
        public IEnumerator CollectAsync_EngineIsUnity()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.AreEqual("Unity", task.Result.engine);
        }

        [UnityTest]
        public IEnumerator CollectAsync_EngineVersionNotEmpty()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotEmpty(task.Result.engine_version, "engine_version은 비어 있지 않아야 합니다.");
        }

        [UnityTest]
        public IEnumerator CollectAsync_PlatformNotEmpty()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotEmpty(task.Result.platform, "platform은 비어 있지 않아야 합니다.");
        }

        [UnityTest]
        public IEnumerator CollectAsync_ScreenDimensionsPositive()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            // 에디터 Play Mode에서는 화면 해상도가 양수여야 함
            Assert.Greater(task.Result.screen_width, 0, "화면 너비는 양수여야 합니다.");
            Assert.Greater(task.Result.screen_height, 0, "화면 높이는 양수여야 합니다.");
        }

        [UnityTest]
        public IEnumerator CollectAsync_TimeSinceStartupPositive()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.GreaterOrEqual(task.Result.time_since_startup, 0f);
        }

        [UnityTest]
        public IEnumerator CollectAsync_FrameCountPositive()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.GreaterOrEqual(task.Result.frame_count, 0);
        }

        [UnityTest]
        public IEnumerator CollectAsync_CapturedAtIsIso8601()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotEmpty(task.Result.captured_at);
            // ISO 8601 형식 확인 (DateTime 파싱 성공 여부)
            bool parsed = System.DateTime.TryParse(task.Result.captured_at, out _);
            Assert.IsTrue(parsed, "captured_at은 유효한 날짜 형식이어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 커스텀 컨텍스트 통합 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator CollectAsync_WithContextProvider_IncludesCustomContext()
        {
            // Arrange: 커스텀 프로바이더 등록
            var provider = new DummyContextProvider(new System.Collections.Generic.Dictionary<string, string>
            {
                { "level", "5" },
                { "score", "12345" },
            });
            _registry.Register(provider);

            // Act
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            // Assert
            var context = task.Result.GetCustomContextDictionary();
            Assert.IsTrue(context.ContainsKey("level"), "level 키가 포함되어야 합니다.");
            Assert.AreEqual("5", context["level"]);
            Assert.IsTrue(context.ContainsKey("score"));
            Assert.AreEqual("12345", context["score"]);
        }

        [UnityTest]
        public IEnumerator CollectAsync_NoContextProvider_EmptyCustomContext()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            var context = task.Result.GetCustomContextDictionary();
            Assert.AreEqual(0, context.Count, "프로바이더가 없으면 커스텀 컨텍스트가 비어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // StateSnapshot JSON 직렬화 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator CollectAsync_SnapshotSerializableToJson()
        {
            var task = _collector.CollectAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            string json = UnityEngine.JsonUtility.ToJson(task.Result);

            Assert.IsNotEmpty(json, "JSON 직렬화 결과는 비어 있지 않아야 합니다.");
            StringAssert.Contains("Unity", json, "JSON에 'Unity'가 포함되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        private class DummyContextProvider : IContextProvider
        {
            private readonly System.Collections.Generic.Dictionary<string, string> _data;

            public DummyContextProvider(System.Collections.Generic.Dictionary<string, string> data)
            {
                _data = data;
            }

            public System.Collections.Generic.Dictionary<string, string> GetContext() => _data;
        }
    }
}
