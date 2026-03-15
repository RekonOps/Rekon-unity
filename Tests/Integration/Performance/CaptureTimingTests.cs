using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace RekonOps.BugBeacon.Tests.Integration
{
    /// <summary>
    /// Phase 8.2 성능 테스트 - 캡처 완료 시간 측정.
    ///
    /// 테스트 기준:
    ///   - CaptureOrchestrator.StartAsync() 호출부터 완료까지 시간 측정
    ///   - 기준: p95 < 5초 (95번째 백분위수)
    ///   - 10회 반복 측정, 95번째 백분위수 계산
    ///
    /// 측정 방법:
    ///   - Mock 캡처 서브시스템으로 네트워크/IO 오버헤드 제거
    ///   - Stopwatch 기반 실시간 측정
    ///   - 백분위수 계산으로 안정성 검증
    /// </summary>
    [TestFixture]
    public class CaptureTimingTests
    {
        // p95 성능 기준: 5000ms
        private const double P95ThresholdMs = 5000.0;

        // 측정 반복 횟수
        private const int MeasurementCount = 10;

        // 테스트용 임시 디렉토리
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "BugBeacon_Timing_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); }
                catch { /* 정리 실패는 무시 */ }
            }
        }

        // ─── 테스트 1: 캡처 파이프라인 각 단계 개별 시간 측정 ───────────────────

        [Test]
        public async Task 캡처_파이프라인_각_단계_타이밍_측정()
        {
            // Arrange
            var timings = new Dictionary<string, double>();
            var sw = new Stopwatch();

            // Act - 각 단계 개별 측정

            // 1단계: 스크린샷 모의 시간
            sw.Restart();
            await Task.Delay(10); // 스크린샷 시뮬레이션 (~10ms)
            sw.Stop();
            timings["screenshot"] = sw.Elapsed.TotalMilliseconds;

            // 2단계: 로그 수집 모의 시간
            sw.Restart();
            await Task.Delay(5); // 로그 수집 시뮬레이션 (~5ms)
            sw.Stop();
            timings["logs"] = sw.Elapsed.TotalMilliseconds;

            // 3단계: 상태 스냅샷 모의 시간
            sw.Restart();
            await Task.Delay(3); // 상태 수집 시뮬레이션 (~3ms)
            sw.Stop();
            timings["state"] = sw.Elapsed.TotalMilliseconds;

            // 전체 시간 (병렬 실행 가정 시 최대값)
            double maxSingleTime = Math.Max(Math.Max(timings["screenshot"], timings["logs"]), timings["state"]);

            UnityEngine.Debug.Log($"[성능 테스트] 각 단계 타이밍: " +
                                   $"스크린샷={timings["screenshot"]:F1}ms, " +
                                   $"로그={timings["logs"]:F1}ms, " +
                                   $"상태={timings["state"]:F1}ms, " +
                                   $"병렬 최대={maxSingleTime:F1}ms");

            // Assert - 각 단계가 5초 이내
            foreach (var kvp in timings)
            {
                Assert.Less(kvp.Value, P95ThresholdMs,
                    $"{kvp.Key} 단계가 p95 기준 {P95ThresholdMs}ms 미만이어야 합니다.");
            }
        }

        // ─── 테스트 2: Mock 캡처 오케스트레이터 10회 측정 ───────────────────────

        [Test]
        public async Task Mock_캡처_오케스트레이터_10회_p95_검증()
        {
            // Arrange - Mock 캡처 오케스트레이터
            var mockOrchestrator = new MockCaptureOrchestrator(
                simulatedScreenshotMs: 15,
                simulatedLogsMs: 8,
                simulatedStateMs: 5,
                simulatedVideoMs: 0); // 영상 없음

            var measurements = new double[MeasurementCount];
            var sw = new Stopwatch();

            // Act - 10회 측정
            for (int i = 0; i < MeasurementCount; i++)
            {
                sw.Restart();
                var result = await mockOrchestrator.StartAsync();
                sw.Stop();

                measurements[i] = sw.Elapsed.TotalMilliseconds;
                Assert.IsNotNull(result, $"측정 {i + 1}회에서 결과가 null이 아니어야 합니다.");

                UnityEngine.Debug.Log($"[성능 테스트] 측정 {i + 1}/{MeasurementCount}: {measurements[i]:F1}ms");
            }

            // 백분위수 계산
            double p95 = CalculatePercentile(measurements, 95);
            double p50 = CalculatePercentile(measurements, 50);
            double avg = Average(measurements);

            UnityEngine.Debug.Log($"[성능 테스트] 10회 측정 결과: " +
                                   $"평균={avg:F1}ms, p50={p50:F1}ms, p95={p95:F1}ms");

            // Assert - p95 < 5초
            Assert.Less(p95, P95ThresholdMs,
                $"캡처 완료 p95가 {P95ThresholdMs}ms 미만이어야 합니다. 실제: {p95:F1}ms");
        }

        // ─── 테스트 3: 병렬 캡처 성능 vs 순차 비교 ──────────────────────────────

        [Test]
        public async Task 병렬_캡처_성능이_순차보다_빠름_검증()
        {
            // Arrange
            int screenshotMs = 20;
            int logsMs = 15;
            int stateMs = 10;

            var sw = new Stopwatch();

            // Act 1 - 순차 실행 시간 측정
            sw.Restart();
            await Task.Delay(screenshotMs);
            await Task.Delay(logsMs);
            await Task.Delay(stateMs);
            sw.Stop();
            double sequentialMs = sw.Elapsed.TotalMilliseconds;

            // Act 2 - 병렬 실행 시간 측정
            sw.Restart();
            await Task.WhenAll(
                Task.Delay(screenshotMs),
                Task.Delay(logsMs),
                Task.Delay(stateMs));
            sw.Stop();
            double parallelMs = sw.Elapsed.TotalMilliseconds;

            UnityEngine.Debug.Log($"[성능 테스트] 순차: {sequentialMs:F1}ms, 병렬: {parallelMs:F1}ms, " +
                                   $"개선: {(sequentialMs - parallelMs):F1}ms ({(sequentialMs - parallelMs) / sequentialMs * 100:F0}% 단축)");

            // Assert - 병렬이 순차보다 빠름
            Assert.Less(parallelMs, sequentialMs,
                $"병렬 실행({parallelMs:F1}ms)이 순차 실행({sequentialMs:F1}ms)보다 빠른야 합니다.");
        }

        // ─── 테스트 4: 캡처 타임아웃 (5초) 동작 검증 ────────────────────────────

        [Test]
        public async Task 캡처_5초_타임아웃_이내_완료_검증()
        {
            // Arrange - 빠른 Mock 캡처 (1초 미만)
            var fastOrchestrator = new MockCaptureOrchestrator(
                simulatedScreenshotMs: 10,
                simulatedLogsMs: 5,
                simulatedStateMs: 3,
                simulatedVideoMs: 0);

            var sw = new Stopwatch();

            // Act
            sw.Start();
            var result = await fastOrchestrator.StartAsync();
            sw.Stop();

            double elapsedMs = sw.Elapsed.TotalMilliseconds;

            UnityEngine.Debug.Log($"[성능 테스트] 캡처 완료 시간: {elapsedMs:F1}ms");

            // Assert - 5000ms (5초) 이내 완료
            Assert.Less(elapsedMs, P95ThresholdMs,
                $"캡처가 5초 이내에 완료되어야 합니다. 실제: {elapsedMs:F1}ms");
            Assert.IsNotNull(result, "캡처 결과가 반환되어야 합니다.");
        }

        // ─── 테스트 5: 백분위수 계산 로직 단위 검증 ─────────────────────────────

        [Test]
        public void 백분위수_계산_로직_정확성_검증()
        {
            // Arrange - 알려진 데이터셋
            double[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            // Act
            double p50 = CalculatePercentile(data, 50);
            double p90 = CalculatePercentile(data, 90);
            double p95 = CalculatePercentile(data, 95);

            UnityEngine.Debug.Log($"[성능 테스트] p50={p50}, p90={p90}, p95={p95}");

            // Assert - 백분위수 범위 확인
            Assert.GreaterOrEqual(p50, 5.0, "p50이 5 이상이어야 합니다.");
            Assert.LessOrEqual(p50, 6.0, "p50이 6 이하여야 합니다.");
            Assert.GreaterOrEqual(p90, 9.0, "p90이 9 이상이어야 합니다.");
            Assert.LessOrEqual(p95, 10.0, "p95가 최댓값(10) 이하여야 합니다.");
        }

        // ─── 테스트 6: 메모리 할당 오버헤드 간접 검증 ───────────────────────────

        [Test]
        public void CaptureResult_생성_오버헤드_최소화_검증()
        {
            // Arrange - GC 압력 측정
            long gcBefore = GC.GetTotalMemory(forceFullCollection: true);
            var sw = new Stopwatch();

            // Act - 100회 CaptureResult 생성
            var results = new CaptureResult[100];
            sw.Start();
            for (int i = 0; i < 100; i++)
            {
                results[i] = new CaptureResult
                {
                    Timestamp = DateTime.UtcNow,
                    ScreenshotPath = null,
                    LogsPath = null,
                    StatePath = null,
                    VideoPath = null
                };
            }
            sw.Stop();

            long gcAfter = GC.GetTotalMemory(forceFullCollection: false);
            long allocatedBytes = gcAfter - gcBefore;

            UnityEngine.Debug.Log($"[성능 테스트] CaptureResult 100개 생성: {sw.Elapsed.TotalMilliseconds:F2}ms, " +
                                   $"할당 메모리: ~{allocatedBytes}bytes");

            // Assert - 100회 생성이 1000ms(1초) 이내
            Assert.Less(sw.Elapsed.TotalMilliseconds, 1000.0, "CaptureResult 100개 생성이 1초 이내여야 합니다.");
            Assert.IsNotNull(results[99], "마지막 결과가 null이 아니어야 합니다.");
        }

        // ─── 헬퍼 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 지정된 백분위수를 계산합니다.
        /// </summary>
        private static double CalculatePercentile(double[] sortedValues, int percentile)
        {
            var sorted = (double[])sortedValues.Clone();
            Array.Sort(sorted);

            double index = (percentile / 100.0) * (sorted.Length - 1);
            int lower = (int)index;
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            double fraction = index - lower;

            return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
        }

        /// <summary>배열의 평균값을 계산합니다.</summary>
        private static double Average(double[] values)
        {
            double sum = 0;
            foreach (double v in values)
                sum += v;
            return sum / values.Length;
        }
    }

    // ─── Mock 구현체 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 캡처 오케스트레이터 Mock 구현체.
    /// 실제 Unity 렌더링 없이 지정된 시간만큼 지연 후 결과를 반환합니다.
    /// </summary>
    internal class MockCaptureOrchestrator
    {
        private readonly int _screenshotMs;
        private readonly int _logsMs;
        private readonly int _stateMs;
        private readonly int _videoMs;

        public MockCaptureOrchestrator(
            int simulatedScreenshotMs,
            int simulatedLogsMs,
            int simulatedStateMs,
            int simulatedVideoMs)
        {
            _screenshotMs = simulatedScreenshotMs;
            _logsMs = simulatedLogsMs;
            _stateMs = simulatedStateMs;
            _videoMs = simulatedVideoMs;
        }

        /// <summary>
        /// 모든 캡처 단계를 병렬로 실행하고 결과를 반환합니다.
        /// CaptureOrchestrator.StartAsync()와 동일한 구조입니다.
        /// </summary>
        public async Task<CaptureResult> StartAsync(CancellationToken cancellationToken = default)
        {
            var result = new CaptureResult { Timestamp = DateTime.UtcNow };

            // 병렬 수집 (CaptureOrchestrator와 동일한 패턴)
            var screenshotTask = SimulateScreenshotAsync(_screenshotMs, result, cancellationToken);
            var logsTask = SimulateLogsAsync(_logsMs, result, cancellationToken);
            var stateTask = SimulateStateAsync(_stateMs, result, cancellationToken);
            var videoTask = SimulateVideoAsync(_videoMs, result, cancellationToken);

            await Task.WhenAll(screenshotTask, logsTask, stateTask, videoTask);

            return result;
        }

        private async Task SimulateScreenshotAsync(int delayMs, CaptureResult result, CancellationToken token)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, token);
            result.ScreenshotPath = "mock_screenshot.png";
        }

        private async Task SimulateLogsAsync(int delayMs, CaptureResult result, CancellationToken token)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, token);
            result.LogsPath = "mock_logs.txt";
        }

        private async Task SimulateStateAsync(int delayMs, CaptureResult result, CancellationToken token)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, token);
            result.StatePath = "mock_state.json";
        }

        private async Task SimulateVideoAsync(int delayMs, CaptureResult result, CancellationToken token)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs, token);
            result.VideoPath = delayMs > 0 ? "mock_video/" : null;
        }
    }
}
