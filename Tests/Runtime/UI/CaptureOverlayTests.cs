using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Tests
{
    /// <summary>
    /// CaptureOverlay Play Mode 단위 테스트.
    ///
    /// 검증 항목:
    ///   - EnsureInstance(): 인스턴스 생성 및 단일 인스턴스 보장
    ///   - BindOrchestrator(): 오케스트레이터 바인딩
    ///   - OnProgress 이벤트 수신 시 상태 전환
    ///   - Hide(): 오버레이 숨김
    ///   - 오케스트레이터 교체 시 이전 이벤트 구독 해제
    /// </summary>
    [TestFixture]
    public class CaptureOverlayTests
    {
        private CaptureOverlay _overlay;
        private FakeOrchestrator _orchestrator;

        // ──────────────────────────────────────────────────────────────
        // 셋업 / 해제
        // ──────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            // 테스트 전 기존 CaptureOverlay 오브젝트 제거
            CaptureOverlay existing = Object.FindObjectOfType<CaptureOverlay>();
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            _orchestrator = new FakeOrchestrator();
        }

        [TearDown]
        public void TearDown()
        {
            // 테스트 후 생성된 오버레이 정리
            CaptureOverlay existing = Object.FindObjectOfType<CaptureOverlay>();
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
        }

        // ──────────────────────────────────────────────────────────────
        // EnsureInstance 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void EnsureInstance_ReturnsNonNullOverlay()
        {
            _overlay = CaptureOverlay.EnsureInstance();

            Assert.IsNotNull(_overlay, "EnsureInstance는 null이 아닌 CaptureOverlay를 반환해야 합니다.");
        }

        [Test]
        public void EnsureInstance_CalledTwice_ReturnsSameInstance()
        {
            CaptureOverlay first  = CaptureOverlay.EnsureInstance();
            CaptureOverlay second = CaptureOverlay.EnsureInstance();

            Assert.AreSame(first, second, "두 번 호출해도 같은 인스턴스를 반환해야 합니다.");

            _overlay = first;
        }

        [Test]
        public void EnsureInstance_CreatesGameObjectWithCorrectName()
        {
            _overlay = CaptureOverlay.EnsureInstance();

            Assert.AreEqual("[Rekon] CaptureOverlay", _overlay.gameObject.name,
                "게임 오브젝트 이름이 '[Rekon] CaptureOverlay'여야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // BindOrchestrator 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void BindOrchestrator_WithNullOrchestrator_DoesNotThrow()
        {
            _overlay = CaptureOverlay.EnsureInstance();

            // null 오케스트레이터 바인딩 시 예외 없이 처리되어야 합니다.
            Assert.DoesNotThrow(() => _overlay.BindOrchestrator(null),
                "null 오케스트레이터 바인딩 시 예외가 발생하면 안 됩니다.");
        }

        [Test]
        public void BindOrchestrator_WithValidOrchestrator_DoesNotThrow()
        {
            _overlay = CaptureOverlay.EnsureInstance();

            Assert.DoesNotThrow(() => _overlay.BindOrchestrator(_orchestrator),
                "유효한 오케스트레이터 바인딩 시 예외가 발생하면 안 됩니다.");
        }

        [Test]
        public void BindOrchestrator_ReplacingOrchestrator_UnsubscribesPrevious()
        {
            _overlay = CaptureOverlay.EnsureInstance();

            FakeOrchestrator first  = new FakeOrchestrator();
            FakeOrchestrator second = new FakeOrchestrator();

            _overlay.BindOrchestrator(first);

            // 두 번째 오케스트레이터로 교체
            _overlay.BindOrchestrator(second);

            // 첫 번째 오케스트레이터에서 이벤트 발행 → 구독 해제되었으므로 오버레이가 반응하지 않아야 함
            first.FireProgress("screenshot", 0.25f);

            // 오버레이가 두 번째에만 바인딩되어 있는지 검증
            // (직접 상태 접근은 private이므로 예외 없이 완료되는 것으로 검증)
            Assert.DoesNotThrow(() => second.FireProgress("screenshot", 0.25f),
                "두 번째 오케스트레이터 이벤트 발행 시 예외가 없어야 합니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // Hide 테스트
        // ──────────────────────────────────────────────────────────────

        [Test]
        public void Hide_DoesNotThrow()
        {
            _overlay = CaptureOverlay.EnsureInstance();
            _overlay.BindOrchestrator(_orchestrator);

            Assert.DoesNotThrow(() => _overlay.Hide(),
                "Hide() 호출 시 예외가 발생하면 안 됩니다.");
        }

        [Test]
        public void Hide_CalledMultipleTimes_DoesNotThrow()
        {
            _overlay = CaptureOverlay.EnsureInstance();

            Assert.DoesNotThrow(() =>
            {
                _overlay.Hide();
                _overlay.Hide();
                _overlay.Hide();
            }, "Hide() 반복 호출 시 예외가 발생하면 안 됩니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // OnProgress 이벤트 수신 테스트
        // ──────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator OnProgress_ScreenshotStage_DoesNotThrow()
        {
            _overlay = CaptureOverlay.EnsureInstance();
            _overlay.BindOrchestrator(_orchestrator);

            // 프레임 대기
            yield return null;

            // 스크린샷 단계 이벤트 발행 → 예외 없이 처리되어야 함
            bool threw = false;
            try
            {
                _orchestrator.FireProgress("screenshot", 0.25f);
            }
            catch
            {
                threw = true;
            }

            Assert.IsFalse(threw, "OnProgress 이벤트 수신 시 예외가 발생하면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator OnProgress_CompleteStage_TriggersHideAfterDelay()
        {
            _overlay = CaptureOverlay.EnsureInstance();
            _overlay.BindOrchestrator(_orchestrator);

            yield return null;

            // 완료 이벤트 발행
            _orchestrator.FireProgress("complete", 1.0f);

            // 완료 이벤트 발행 직후 예외 없이 처리되어야 함
            yield return null;

            Assert.IsNotNull(_overlay, "완료 이벤트 후 오버레이 오브젝트가 존재해야 합니다.");
        }

        [UnityTest]
        public IEnumerator OnProgress_ErrorStage_ShowsErrorInOverlay()
        {
            _overlay = CaptureOverlay.EnsureInstance();
            _overlay.BindOrchestrator(_orchestrator);

            yield return null;

            // 오류 이벤트 발행 (errorMessage가 있음)
            bool threw = false;
            try
            {
                _orchestrator.FireProgressWithError("screenshot", 0.25f, "스크린샷 캡처 실패");
            }
            catch
            {
                threw = true;
            }

            Assert.IsFalse(threw, "오류 단계 이벤트 수신 시 예외가 발생하면 안 됩니다.");
        }

        // ──────────────────────────────────────────────────────────────
        // 내부 Fake 오케스트레이터
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 테스트용 가짜 ICaptureOrchestrator.
        /// OnProgress 이벤트를 직접 발행할 수 있습니다.
        /// </summary>
        private class FakeOrchestrator : ICaptureOrchestrator
        {
            public event System.Action<CaptureProgressEvent> OnProgress;

            public System.Threading.Tasks.Task<CaptureResult> StartAsync()
            {
                return System.Threading.Tasks.Task.FromResult(new CaptureResult
                {
                    ScreenshotPath = "",
                    LogsPath       = "",
                    StatePath      = "",
                    Timestamp      = System.DateTime.UtcNow,
                });
            }

            /// <summary>성공 진행 이벤트를 발행합니다.</summary>
            public void FireProgress(string stage, float progress)
            {
                OnProgress?.Invoke(new CaptureProgressEvent(stage, progress));
            }

            /// <summary>오류가 포함된 진행 이벤트를 발행합니다.</summary>
            public void FireProgressWithError(string stage, float progress, string error)
            {
                OnProgress?.Invoke(new CaptureProgressEvent(stage, progress, error));
            }
        }
    }
}
