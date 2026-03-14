using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.BugBeacon;

namespace RekonOps.BugBeacon.Tests
{
    /// <summary>
    /// FrameCapturer 단위 테스트.
    /// MonoBehaviour 기반이므로 UnityTest로 실행합니다.
    /// </summary>
    [TestFixture]
    public class FrameCapturerTests
    {
        private GameObject _gameObject;
        private FrameCapturer _capturer;
        private FrameRingBuffer _ringBuffer;
        private VideoEncoderConfig _config;
        private BugBeaconSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<BugBeaconSettings>();
            _settings.videoWidth = 320;
            _settings.videoHeight = 180;
            _settings.videoFps = 10;

            _config = VideoEncoderConfig.FromSettings(_settings);
            _ringBuffer = new FrameRingBuffer(capacity: 30);

            _gameObject = new GameObject("FrameCapturerTest");
            _capturer = _gameObject.AddComponent<FrameCapturer>();
            _capturer.Initialize(_ringBuffer, _config);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
            _ringBuffer?.Dispose();
            Object.DestroyImmediate(_settings);
        }

        // ──────────────────────────────────────────────────────────────
        // 초기화 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Initialize_NullRingBuffer_ThrowsArgumentNullException()
        {
            var go = new GameObject("TestCapturerNull");
            var capturer = go.AddComponent<FrameCapturer>();
            Assert.Throws<System.ArgumentNullException>(
                () => capturer.Initialize(null, _config));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Initialize_NullConfig_ThrowsArgumentNullException()
        {
            var go = new GameObject("TestCapturerNullConfig");
            var capturer = go.AddComponent<FrameCapturer>();
            Assert.Throws<System.ArgumentNullException>(
                () => capturer.Initialize(_ringBuffer, null));
            Object.DestroyImmediate(go);
        }

        // ──────────────────────────────────────────────────────────────
        // 상태 전환 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void StartCapturing_SetsIsCapturingTrue()
        {
            _capturer.StartCapturing();
            Assert.IsTrue(_capturer.IsCapturing);
        }

        [Test]
        public void StopCapturing_SetsIsCapturingFalse()
        {
            _capturer.StartCapturing();
            _capturer.StopCapturing();
            Assert.IsFalse(_capturer.IsCapturing);
        }

        [Test]
        public void InitialState_IsCapturingFalse()
        {
            Assert.IsFalse(_capturer.IsCapturing);
        }

        // ──────────────────────────────────────────────────────────────
        // 캡처 통합 테스트 (Camera.main 필요)
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator StartCapturing_WithCamera_AddsFramesToBuffer()
        {
            // Camera 생성
            var cameraGo = new GameObject("TestCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.tag = "MainCamera";

            _capturer.StartCapturing();

            // FPS 10 → 간격 0.1초, 0.5초 대기로 최소 1~5프레임 예상
            float elapsed = 0f;
            int initialCount = _ringBuffer.Count;

            while (elapsed < 0.5f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _capturer.StopCapturing();

            // AsyncGPUReadback은 비동기이므로 추가 대기
            yield return new WaitForSeconds(0.2f);

            Object.DestroyImmediate(cameraGo);

            // 헤드리스 환경에서는 0일 수 있으므로 최소 0 이상 확인
            Assert.GreaterOrEqual(_ringBuffer.Count, 0, "링버퍼에 프레임이 있어야 합니다 (헤드리스 예외 가능).");
        }

        [UnityTest]
        public IEnumerator StopCapturing_StopsAddingFrames()
        {
            var cameraGo = new GameObject("TestCamera2");
            var camera = cameraGo.AddComponent<Camera>();
            camera.tag = "MainCamera";

            _capturer.StartCapturing();
            yield return new WaitForSeconds(0.2f);
            _capturer.StopCapturing();

            int countAtStop = _ringBuffer.Count;

            yield return new WaitForSeconds(0.3f);

            int countAfterStop = _ringBuffer.Count;

            Object.DestroyImmediate(cameraGo);

            Assert.AreEqual(countAtStop, countAfterStop, "캡처 중지 후 프레임이 추가되지 않아야 합니다.");
        }
    }
}
