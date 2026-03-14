using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.BugBeacon;

namespace RekonOps.BugBeacon.Tests
{
    /// <summary>
    /// CaptureOrchestrator 통합 테스트.
    /// 각 서브시스템의 목(Mock)을 주입하여 오케스트레이션 흐름을 검증합니다.
    /// </summary>
    [TestFixture]
    public class CaptureOrchestratorTests
    {
        // ──────────────────────────────────────────────────────────────
        // 목(Mock) 구현체
        // ──────────────────────────────────────────────────────────────

        private class MockScreenshotCapturer : IScreenshotCapturer
        {
            public bool CaptureWasCalled { get; private set; }
            public bool SaveWasCalled { get; private set; }
            public byte[] ReturnValue { get; set; } = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG 매직
            public Exception ThrowOnCapture { get; set; }

            public Task<byte[]> CaptureAsync()
            {
                CaptureWasCalled = true;
                if (ThrowOnCapture != null) throw ThrowOnCapture;
                return Task.FromResult(ReturnValue);
            }

            public Task SaveAsync(byte[] pngBytes, string filePath)
            {
                SaveWasCalled = true;
                // 실제 파일 저장
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllBytes(filePath, pngBytes ?? Array.Empty<byte>());
                return Task.CompletedTask;
            }
        }

        private class MockLogCollector : ILogCollector
        {
            public LogEntry[] Entries { get; set; } = Array.Empty<LogEntry>();
            public int Count => Entries.Length;
            public LogEntry[] GetEntries() => Entries;
        }

        private class MockStateCollector : IStateSnapshotCollector
        {
            public bool CollectWasCalled { get; private set; }
            public StateSnapshot ReturnValue { get; set; } = new StateSnapshot
            {
                engine = "Unity",
                engine_version = "2022.3.0f1",
                captured_at = DateTime.UtcNow.ToString("O"),
            };

            public Task<StateSnapshot> CollectAsync()
            {
                CollectWasCalled = true;
                return Task.FromResult(ReturnValue);
            }
        }

        private class MockVideoEncoder : IVideoEncoder
        {
            public bool EncodeWasCalled { get; private set; }
            public string LastOutputPath { get; private set; }

            public Task EncodeAsync(FrameData[] frames, string outputPath, VideoEncoderConfig config)
            {
                EncodeWasCalled = true;
                LastOutputPath = outputPath;
                Directory.CreateDirectory(outputPath);
                return Task.CompletedTask;
            }
        }

        private class FailingScreenshotCapturer : IScreenshotCapturer
        {
            public Task<byte[]> CaptureAsync() => throw new InvalidOperationException("테스트 실패 스크린샷");
            public Task SaveAsync(byte[] bytes, string path) => Task.CompletedTask;
        }

        private class SlowScreenshotCapturer : IScreenshotCapturer
        {
            public async Task<byte[]> CaptureAsync()
            {
                await Task.Delay(TimeSpan.FromSeconds(10)); // 10초 대기 → 타임아웃 유발
                return new byte[] { 0x89, 0x50 };
            }

            public Task SaveAsync(byte[] bytes, string path) => Task.CompletedTask;
        }

        // ──────────────────────────────────────────────────────────────
        // 테스트 픽스처
        // ──────────────────────────────────────────────────────────────

        private MockScreenshotCapturer _screenshot;
        private MockLogCollector _logs;
        private LogSerializer _logSerializer;
        private MockStateCollector _state;
        private FrameRingBuffer _frameBuffer;
        private MockVideoEncoder _videoEncoder;
        private VideoEncoderConfig _videoConfig;
        private BugBeaconSettings _settings;
        private CaptureOrchestrator _orchestrator;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _screenshot = new MockScreenshotCapturer();
            _logs = new MockLogCollector();
            _logSerializer = new LogSerializer();
            _state = new MockStateCollector();
            _frameBuffer = new FrameRingBuffer(10);
            _videoEncoder = new MockVideoEncoder();
            _videoConfig = new VideoEncoderConfig { Width = 320, Height = 180, Fps = 10 };

            _settings = ScriptableObject.CreateInstance<BugBeaconSettings>();
            _settings.videoEnabled = true;

            _orchestrator = new CaptureOrchestrator(
                _screenshot,
                _logs,
                _logSerializer,
                _state,
                _frameBuffer,
                _videoEncoder,
                _videoConfig,
                _settings);

            _tempDir = Path.Combine(Path.GetTempPath(), "OrchestratorTests_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            _orchestrator?.Dispose();
            _frameBuffer?.Dispose();
            Object.DestroyImmediate(_settings);

            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ──────────────────────────────────────────────────────────────
        // 생성자 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Constructor_NullScreenshotCapturer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CaptureOrchestrator(
                null, _logs, _logSerializer, _state, _frameBuffer, _videoEncoder, _videoConfig, _settings));
        }

        [Test]
        public void Constructor_NullLogCollector_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CaptureOrchestrator(
                _screenshot, null, _logSerializer, _state, _frameBuffer, _videoEncoder, _videoConfig, _settings));
        }

        [Test]
        public void Constructor_NullSettings_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CaptureOrchestrator(
                _screenshot, _logs, _logSerializer, _state, _frameBuffer, _videoEncoder, _videoConfig, null));
        }

        // ──────────────────────────────────────────────────────────────
        // StartAsync 기본 흐름 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator StartAsync_ReturnsNonNullResult()
        {
            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotNull(task.Result, "CaptureResult는 null이 아니어야 합니다.");
        }

        [UnityTest]
        public IEnumerator StartAsync_CallsScreenshotCapturer()
        {
            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsTrue(_screenshot.CaptureWasCalled, "스크린샷 캡처가 호출되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator StartAsync_CallsStateCollector()
        {
            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsTrue(_state.CollectWasCalled, "상태 수집이 호출되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator StartAsync_ScreenshotPathSet_WhenCaptureSucceeds()
        {
            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotEmpty(task.Result.ScreenshotPath, "스크린샷 경로가 설정되어야 합니다.");
            Assert.IsTrue(task.Result.ScreenshotPath.EndsWith(".png"), "PNG 파일이어야 합니다.");
        }

        [UnityTest]
        public IEnumerator StartAsync_LogsPathSet()
        {
            _logs.Entries = new[]
            {
                new LogEntry(1.0, LogType.Log, "테스트 로그", ""),
            };

            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotEmpty(task.Result.LogsPath, "로그 경로가 설정되어야 합니다.");
            Assert.IsTrue(task.Result.LogsPath.EndsWith(".zip"), "ZIP 파일이어야 합니다.");
        }

        [UnityTest]
        public IEnumerator StartAsync_StatePathSet()
        {
            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsNotEmpty(task.Result.StatePath, "상태 경로가 설정되어야 합니다.");
            Assert.IsTrue(task.Result.StatePath.EndsWith(".json"), "JSON 파일이어야 합니다.");
        }

        [UnityTest]
        public IEnumerator StartAsync_TimestampSet()
        {
            var before = DateTime.UtcNow;
            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);
            var after = DateTime.UtcNow;

            Assert.GreaterOrEqual(task.Result.Timestamp, before.AddSeconds(-1));
            Assert.LessOrEqual(task.Result.Timestamp, after.AddSeconds(1));
        }

        // ──────────────────────────────────────────────────────────────
        // 영상 수집 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator StartAsync_VideoEnabled_WithFrames_CallsEncoder()
        {
            // 프레임 추가
            _frameBuffer.Add(new FrameData(new byte[100], 10, 10, 1.0));
            _frameBuffer.Add(new FrameData(new byte[100], 10, 10, 2.0));

            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsTrue(_videoEncoder.EncodeWasCalled, "프레임이 있으면 인코더가 호출되어야 합니다.");
            Assert.IsNotEmpty(task.Result.VideoPath, "비디오 경로가 설정되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator StartAsync_VideoDisabled_DoesNotCallEncoder()
        {
            _settings.videoEnabled = false;
            _frameBuffer.Add(new FrameData(new byte[100], 10, 10, 1.0));

            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsFalse(_videoEncoder.EncodeWasCalled, "영상 비활성 시 인코더를 호출하지 않아야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 진행 이벤트 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator StartAsync_FiresProgressEvents()
        {
            var stages = new List<string>();
            _orchestrator.OnProgress += evt => stages.Add(evt.Stage);

            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.IsTrue(stages.Contains("screenshot"), "screenshot 단계 이벤트가 발행되어야 합니다.");
            Assert.IsTrue(stages.Contains("logs"), "logs 단계 이벤트가 발행되어야 합니다.");
            Assert.IsTrue(stages.Contains("state"), "state 단계 이벤트가 발행되어야 합니다.");
            Assert.IsTrue(stages.Contains("complete"), "complete 단계 이벤트가 발행되어야 합니다.");
        }

        [UnityTest]
        public IEnumerator StartAsync_ProgressValues_Increase()
        {
            var progressValues = new List<float>();
            _orchestrator.OnProgress += evt => progressValues.Add(evt.Progress);

            var task = _orchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            Assert.Greater(progressValues.Count, 0, "진행 이벤트가 최소 1개 발행되어야 합니다.");
            // 마지막 진행률은 1.0이어야 함
            Assert.AreEqual(1.0f, progressValues[progressValues.Count - 1], 0.001f);
        }

        // ──────────────────────────────────────────────────────────────
        // 에러 격리 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator StartAsync_ScreenshotFails_OtherArtifactsStillCaptured()
        {
            // 스크린샷 실패하는 오케스트레이터 생성
            var failOrchestrator = new CaptureOrchestrator(
                new FailingScreenshotCapturer(),
                _logs,
                _logSerializer,
                _state,
                _frameBuffer,
                _videoEncoder,
                _videoConfig,
                _settings);

            LogAssert.ignoreFailingMessages = true;
            var task = failOrchestrator.StartAsync();
            yield return new WaitUntil(() => task.IsCompleted);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNotNull(task.Result, "스크린샷 실패해도 결과가 반환되어야 합니다.");
            // 로그와 상태는 여전히 수집되어야 함
            Assert.IsNotEmpty(task.Result.LogsPath, "스크린샷 실패해도 로그는 수집되어야 합니다.");
            Assert.IsNotEmpty(task.Result.StatePath, "스크린샷 실패해도 상태는 수집되어야 합니다.");

            failOrchestrator.Dispose();
        }

        // ──────────────────────────────────────────────────────────────
        // 중복 실행 방지 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator StartAsync_CalledTwiceSimultaneously_SecondCallReturnsNull()
        {
            // 첫 번째 호출 시작
            var task1 = _orchestrator.StartAsync();

            // 즉시 두 번째 호출
            var task2 = _orchestrator.StartAsync();

            yield return new WaitUntil(() => task1.IsCompleted && task2.IsCompleted);

            // 두 번째 호출은 null 반환 (이미 진행 중)
            Assert.IsNull(task2.Result, "동시 캡처 시 두 번째 호출은 null을 반환해야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // CaptureResult 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CaptureResult_IsPartialSuccess_TrueWhenAtLeastOnePathSet()
        {
            var result = new CaptureResult { ScreenshotPath = "/some/path.png" };
            Assert.IsTrue(result.IsPartialSuccess);
        }

        [Test]
        public void CaptureResult_IsPartialSuccess_FalseWhenNothingSet()
        {
            var result = new CaptureResult();
            Assert.IsFalse(result.IsPartialSuccess);
        }

        [Test]
        public void CaptureResult_IsFullSuccess_TrueWhenAllPathsSet()
        {
            var result = new CaptureResult
            {
                ScreenshotPath = "/a.png",
                LogsPath = "/b.zip",
                StatePath = "/c.json",
            };
            Assert.IsTrue(result.IsFullSuccess);
        }

        // ──────────────────────────────────────────────────────────────
        // CaptureProgressEvent 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void CaptureProgressEvent_IsSuccess_TrueWhenNoError()
        {
            var evt = new CaptureProgressEvent("screenshot", 0.25f);
            Assert.IsTrue(evt.IsSuccess);
        }

        [Test]
        public void CaptureProgressEvent_IsSuccess_FalseWhenErrorSet()
        {
            var evt = new CaptureProgressEvent("screenshot", 0.25f, "에러 발생");
            Assert.IsFalse(evt.IsSuccess);
        }

        // ──────────────────────────────────────────────────────────────
        // HotkeyManager 바인딩 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator BindHotkeyManager_ThenTrigger_StartsCapture()
        {
            bool completed = false;
            _orchestrator.OnCaptureCompleted += _ => completed = true;

            // HotkeyManager 생성 및 바인딩
            var go = new GameObject("HotkeyManagerTest");
            var hotkey = go.AddComponent<HotkeyManager>();
            hotkey.SetProvider(new AlwaysTriggerOnceProvider());

            var settings = ScriptableObject.CreateInstance<BugBeaconSettings>();
            settings.captureHotkey = UnityEngine.KeyCode.F12;
            hotkey.SetSettings(settings);

            _orchestrator.BindHotkeyManager(hotkey);

            // 트리거 발생 (Update에서 호출)
            yield return null; // 1프레임 = 1번 트리거

            // 캡처 완료 대기 (최대 10프레임)
            float waited = 0;
            while (!completed && waited < 6f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(settings);

            Assert.IsTrue(completed, "핫키 트리거 후 캡처가 완료되어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 헬퍼
        // ──────────────────────────────────────────────────────────────

        private class AlwaysTriggerOnceProvider : IHotkeyProvider
        {
            private int _count;
            public bool IsTriggered(UnityEngine.KeyCode key)
            {
                if (_count == 0) { _count++; return true; }
                return false;
            }
        }
    }
}
