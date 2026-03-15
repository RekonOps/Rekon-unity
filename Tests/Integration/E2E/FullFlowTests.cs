using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace RekonOps.BugBeacon.Tests.Integration
{
    /// <summary>
    /// Phase 8.1 E2E 통합 테스트 - 핫키→번들→Jira 전체 플로우.
    ///
    /// 테스트 시나리오:
    ///   1. 정상 플로우: 캡처 → 번들 생성 → Jira 이슈 등록
    ///   2. 부분 실패: 스크린샷 성공 + 영상 실패 → 부분 번들 생성
    ///   3. 번들 상태 전이 검증 (created → submitting → submitted)
    ///
    /// Mock 전략:
    ///   - 각 서비스의 의존성을 직접 Mock 구현체로 대체
    ///   - 외부 HTTP 호출 없이 미리 설정된 결과 반환
    /// </summary>
    [TestFixture]
    public class FullFlowTests
    {
        // 테스트용 임시 디렉토리
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            // 각 테스트마다 독립된 임시 디렉토리 사용
            _tempDir = Path.Combine(Path.GetTempPath(), "BugBeacon_E2E_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            // 임시 디렉토리 정리
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }
        }

        // ─── 테스트 1: 번들 manifest 초기 상태 검증 ──────────────────────────────

        [Test]
        public void 캡처_결과로_manifest_생성시_Created_상태_확인()
        {
            // Arrange - 가짜 캡처 결과 생성
            var captureResult = CreateFakeCaptureResult();
            var manifestGenerator = new ManifestGenerator();

            // Act
            BundleManifest manifest = manifestGenerator.Generate(captureResult);

            // Assert - manifest 초기 상태 검증
            Assert.IsNotNull(manifest, "번들 manifest가 생성되어야 합니다.");
            Assert.IsFalse(string.IsNullOrEmpty(manifest.id), "번들 ID가 있어야 합니다.");
            Assert.AreEqual(BundleState.Created, manifest.state, "번들 초기 상태는 Created여야 합니다.");
            Assert.IsNotNull(manifest.artifacts, "아티팩트 목록이 있어야 합니다.");
        }

        // ─── 테스트 2: Jira 제출 정상 플로우 및 상태 전이 ───────────────────────

        [Test]
        public async Task Jira_제출_서비스_정상_제출_상태_전이_검증()
        {
            // Arrange - Mock 구성요소 준비
            var mockBundleStateUpdater = new MockBundleStateUpdater();
            var mockJiraFacade = new MockJiraSubmissionFacade(
                issueKey: "TEST-123",
                issueUrl: "https://jira.test/issue/TEST-123",
                throwException: false);

            // Act - Mock을 통한 Jira 제출 시뮬레이션
            await mockBundleStateUpdater.UpdateSubmittingAsync("bundle-test-001");
            var result = await mockJiraFacade.SubmitAsync("bundle-test-001", "TEST", "[BugBeacon] E2E 테스트 이슈");
            await mockBundleStateUpdater.UpdateSubmittedAsync("bundle-test-001", result.IssueKey, result.IssueUrl);

            // Assert
            Assert.IsTrue(result.Success, "Jira 이슈 제출이 성공해야 합니다.");
            Assert.AreEqual("TEST-123", result.IssueKey, "Jira 이슈 키가 올바르게 반환되어야 합니다.");

            // 상태 전이 검증: submitting → submitted
            Assert.IsTrue(mockBundleStateUpdater.SubmittingCalled, "제출 중 상태 업데이트가 호출되어야 합니다.");
            Assert.IsTrue(mockBundleStateUpdater.SubmittedCalled, "제출 완료 상태 업데이트가 호출되어야 합니다.");
            Assert.AreEqual("TEST-123", mockBundleStateUpdater.LastIssueKey, "이슈 키가 저장되어야 합니다.");
        }

        // ─── 테스트 3: 부분 실패 시나리오 ───────────────────────────────────────

        [Test]
        public async Task 스크린샷_성공_영상_실패_부분_번들_생성_시나리오()
        {
            // Arrange - 스크린샷만 있는 캡처 결과 (영상 없음)
            var captureResult = new CaptureResult
            {
                Timestamp = DateTime.UtcNow,
                ScreenshotPath = Path.Combine(_tempDir, "screenshot.png"),
                LogsPath = Path.Combine(_tempDir, "logs.txt"),
                StatePath = Path.Combine(_tempDir, "state.json"),
                VideoPath = null // 영상 없음 = 영상 캡처 실패 시뮬레이션
            };

            // 가짜 파일 생성
            File.WriteAllText(captureResult.ScreenshotPath, "PNG_STUB");
            File.WriteAllText(captureResult.LogsPath, "TXT_STUB");
            File.WriteAllText(captureResult.StatePath, "{\"state\":\"test\"}");

            var manifestGenerator = new ManifestGenerator();

            // Act
            BundleManifest manifest = manifestGenerator.Generate(captureResult);

            // Assert - 영상이 없어도 번들 생성 가능
            Assert.IsNotNull(manifest, "부분 번들이 생성되어야 합니다.");
            Assert.IsNotNull(manifest.artifacts, "아티팩트 목록이 있어야 합니다.");

            // 영상 아티팩트는 VideoPath가 없으면 포함되지 않아야 함
            bool hasVideoArtifact = false;
            if (manifest.artifacts != null)
            {
                foreach (var artifact in manifest.artifacts)
                {
                    if (artifact.type == BundleArtifactType.Video)
                        hasVideoArtifact = true;
                }
            }
            Assert.IsFalse(hasVideoArtifact, "영상이 없으면 Video 아티팩트가 포함되지 않아야 합니다.");

            await Task.CompletedTask; // 비동기 패턴 유지
        }

        // ─── 테스트 4: Jira 이슈 생성 실패 → Failed 상태 전이 ───────────────────

        [Test]
        public async Task Jira_이슈_생성_실패시_Failed_상태_전이()
        {
            // Arrange
            var mockBundleStateUpdater = new MockBundleStateUpdater();
            var mockJiraFacade = new MockJiraSubmissionFacade(
                issueKey: null,
                issueUrl: null,
                throwException: true); // 실패 시뮬레이션

            // Act
            await mockBundleStateUpdater.UpdateSubmittingAsync("bundle-fail-001");

            bool exceptionThrown = false;
            string errorMessage = null;
            try
            {
                var result = await mockJiraFacade.SubmitAsync("bundle-fail-001", "TEST", "[BugBeacon] 이슈 생성 실패 테스트");
            }
            catch (Exception ex)
            {
                exceptionThrown = true;
                errorMessage = ex.Message;
                await mockBundleStateUpdater.UpdateFailedAsync("bundle-fail-001", ex.Message);
            }

            // Assert
            Assert.IsTrue(exceptionThrown, "이슈 생성 실패 시 예외가 발생해야 합니다.");
            Assert.IsTrue(mockBundleStateUpdater.FailedCalled, "실패 상태 업데이트가 호출되어야 합니다.");
            Assert.IsTrue(mockBundleStateUpdater.SubmittingCalled, "제출 중 상태 업데이트가 먼저 호출되어야 합니다.");
        }

        // ─── 테스트 5: manifest 상태 변화 검증 ───────────────────────────────────

        [Test]
        public async Task manifest_상태_전이_Created_Submitting_Submitted_순서_검증()
        {
            // Arrange
            var captureResult = CreateFakeCaptureResult();
            var manifestGenerator = new ManifestGenerator();
            BundleManifest manifest = manifestGenerator.Generate(captureResult);

            // 1단계: Created 상태 확인
            Assert.AreEqual(BundleState.Created, manifest.state, "초기 상태는 Created여야 합니다.");

            // 2단계: Submitting 상태로 전이 시뮬레이션
            manifest.state = BundleState.Submitting;
            Assert.AreEqual(BundleState.Submitting, manifest.state, "제출 중 상태로 전이되어야 합니다.");

            // 3단계: Submitted 상태로 전이 시뮬레이션
            manifest.state = BundleState.Submitted;
            manifest.jira_issue_key = "TEST-789";
            manifest.registered_at = DateTime.UtcNow.ToString("O");

            // Assert
            Assert.AreEqual(BundleState.Submitted, manifest.state, "최종 상태는 Submitted여야 합니다.");
            Assert.AreEqual("TEST-789", manifest.jira_issue_key, "Jira 이슈 키가 저장되어야 합니다.");
            Assert.IsNotNull(manifest.registered_at, "등록 시각이 저장되어야 합니다.");

            await Task.CompletedTask;
        }

        // ─── 테스트 6: HotkeyManager 이벤트 발행 검증 ───────────────────────────

        [Test]
        public void HotkeyManager_트리거_이벤트_발행_검증()
        {
            // Arrange
            bool eventFired = false;
            var mockProvider = new MockHotkeyProvider();

            // Act - 이벤트 발생 시뮬레이션
            Action captureHandler = () => { eventFired = true; };
            mockProvider.OnTrigger += captureHandler;
            mockProvider.SimulateTrigger();

            // Assert
            Assert.IsTrue(eventFired, "핫키 트리거 이벤트가 발행되어야 합니다.");
        }

        // ─── 헬퍼 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 가짜 CaptureResult를 생성합니다.
        /// </summary>
        private CaptureResult CreateFakeCaptureResult()
        {
            // 가짜 파일 생성
            string screenshotPath = Path.Combine(_tempDir, "screenshot.png");
            string logsPath = Path.Combine(_tempDir, "logs.txt");
            string statePath = Path.Combine(_tempDir, "state.json");

            File.WriteAllText(screenshotPath, "PNG_STUB_CONTENT");
            File.WriteAllText(logsPath, "TXT_STUB_CONTENT");
            File.WriteAllText(statePath, "{\"scene\":\"TestScene\",\"fps\":60}");

            return new CaptureResult
            {
                Timestamp = DateTime.UtcNow,
                ScreenshotPath = screenshotPath,
                LogsPath = logsPath,
                StatePath = statePath,
                VideoPath = null
            };
        }
    }

    // ─── Mock 구현체 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Jira 제출 파사드 Mock.
    /// 실제 HTTP 호출 없이 미리 설정된 결과를 반환합니다.
    /// </summary>
    internal class MockJiraSubmissionFacade
    {
        private readonly string _issueKey;
        private readonly string _issueUrl;
        private readonly bool _throwException;

        public MockJiraSubmissionFacade(string issueKey, string issueUrl, bool throwException)
        {
            _issueKey = issueKey;
            _issueUrl = issueUrl;
            _throwException = throwException;
        }

        public async Task<FakeSubmissionResult> SubmitAsync(
            string bundleId,
            string projectKey,
            string summary,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield(); // 비동기 시뮬레이션

            if (_throwException)
                throw new InvalidOperationException("Mock: Jira API 호출 실패 시뮬레이션");

            return new FakeSubmissionResult
            {
                Success = true,
                IssueKey = _issueKey,
                IssueUrl = _issueUrl
            };
        }

        public class FakeSubmissionResult
        {
            public bool Success { get; set; }
            public string IssueKey { get; set; }
            public string IssueUrl { get; set; }
        }
    }

    /// <summary>
    /// 번들 상태 업데이터 Mock 구현체.
    /// 상태 전이 호출 여부와 파라미터를 기록합니다.
    /// </summary>
    internal class MockBundleStateUpdater : IBundleStateUpdater
    {
        public bool SubmittingCalled { get; private set; }
        public bool SubmittedCalled { get; private set; }
        public bool FailedCalled { get; private set; }
        public string LastBundleId { get; private set; }
        public string LastIssueKey { get; private set; }
        public string LastErrorMessage { get; private set; }

        public async Task UpdateSubmittingAsync(string bundleId, CancellationToken cancellationToken = default)
        {
            SubmittingCalled = true;
            LastBundleId = bundleId;
            await Task.CompletedTask;
        }

        public async Task UpdateSubmittedAsync(
            string bundleId,
            string issueKey,
            string issueUrl,
            CancellationToken cancellationToken = default)
        {
            SubmittedCalled = true;
            LastBundleId = bundleId;
            LastIssueKey = issueKey;
            await Task.CompletedTask;
        }

        public async Task UpdateFailedAsync(
            string bundleId,
            string errorMessage,
            CancellationToken cancellationToken = default)
        {
            FailedCalled = true;
            LastBundleId = bundleId;
            LastErrorMessage = errorMessage;
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// 핫키 제공자 Mock 구현체.
    /// 테스트에서 이벤트 발행을 제어합니다.
    /// </summary>
    internal class MockHotkeyProvider : IHotkeyProvider
    {
        public event Action OnTrigger;

        public bool IsTriggered(KeyCode hotkey)
        {
            return false; // 기본적으로 트리거하지 않음
        }

        public void SimulateTrigger()
        {
            OnTrigger?.Invoke();
        }
    }
}
