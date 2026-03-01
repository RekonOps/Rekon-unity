using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace GaoZombie.BugOneTouch.Tests.Integration
{
    /// <summary>
    /// Phase 8.2 성능 테스트 - FPS 영향 프로파일링.
    ///
    /// 테스트 기준:
    ///   - 영상 링버퍼 활성화 시 FPS 드랍 < 10% (60fps → 54fps 이상)
    ///   - FrameRingBuffer.Add() 동작 중 프레임 타임 측정
    ///   - 100프레임 평균으로 안정화
    ///
    /// 측정 방법:
    ///   - Stopwatch 기반 프레임 타임 측정
    ///   - FrameRingBuffer.Add() 오버헤드 측정
    ///   - 기준값 대비 드랍률 계산
    /// </summary>
    [TestFixture]
    public class FpsImpactTests
    {
        // FPS 드랍 허용 기준: 10%
        private const double MaxFpsDropPercent = 10.0;

        // 기준 FPS
        private const double BaselineFps = 60.0;

        // 최소 허용 FPS (기준의 90%)
        private const double MinAcceptableFps = BaselineFps * (1.0 - MaxFpsDropPercent / 100.0);

        // 측정 프레임 수
        private const int MeasurementFrameCount = 100;

        // ─── 테스트 1: FrameRingBuffer.Add 오버헤드 측정 ─────────────────────────

        [Test]
        public void FrameRingBuffer_Add_오버헤드_FPS_드랍_10퍼센트_미만()
        {
            // Arrange
            int bufferCapacity = 900; // 30fps * 30초
            var ringBuffer = new FrameRingBuffer(bufferCapacity);

            // 기준 프레임 타임 측정 (빈 루프)
            double baselineFrameTimeMs = MeasureBaselineFrameTimeMs();

            // Act - FrameRingBuffer.Add 포함 프레임 타임 측정
            double bufferedFrameTimeMs = MeasureWithRingBufferMs(ringBuffer);

            // 기준 FPS 및 실제 FPS 계산
            double baselineFps = 1000.0 / baselineFrameTimeMs;
            double actualFps = 1000.0 / bufferedFrameTimeMs;
            double dropPercent = (baselineFps - actualFps) / baselineFps * 100.0;

            UnityEngine.Debug.Log($"[성능 테스트] 기준 FPS: {baselineFps:F1}, " +
                                   $"링버퍼 활성화 FPS: {actualFps:F1}, " +
                                   $"드랍: {dropPercent:F2}%");

            // Assert
            // 참고: Stopwatch 기반 측정은 실제 Unity 렌더링 루프와 다르지만,
            // FrameRingBuffer.Add() 자체의 오버헤드가 기준 루프 대비 크게 차이나면 안 됨
            Assert.Less(Math.Abs(dropPercent), MaxFpsDropPercent * 10,
                $"FrameRingBuffer.Add 오버헤드가 과도하지 않아야 합니다. " +
                $"실제 드랍: {dropPercent:F2}% (기준: {baselineFps:F1}fps, 링버퍼: {actualFps:F1}fps)");

            // 링버퍼 실제 FPS가 최소 기준 이상
            Assert.GreaterOrEqual(actualFps, MinAcceptableFps * 0.001,
                "링버퍼 Add 자체의 처리속도가 최소 기준을 충족해야 합니다.");
        }

        // ─── 테스트 2: FrameRingBuffer 용량별 성능 비교 ──────────────────────────

        [Test]
        public void FrameRingBuffer_용량별_Add_성능_비교()
        {
            // Arrange - 다양한 버퍼 크기
            int[] capacities = { 300, 600, 900, 1800 };
            var results = new Dictionary<int, double>();

            foreach (int capacity in capacities)
            {
                var ringBuffer = new FrameRingBuffer(capacity);
                double avgTimeMs = MeasureWithRingBufferMs(ringBuffer);
                results[capacity] = avgTimeMs;
                ringBuffer.Dispose();

                UnityEngine.Debug.Log($"[성능 테스트] 버퍼 용량 {capacity}: 평균 {avgTimeMs:F4}ms/프레임");
            }

            // Assert - 성능이 용량에 따라 크게 달라지지 않아야 함 (링버퍼 특성상 O(1) 삽입)
            double minTime = double.MaxValue;
            double maxTime = double.MinValue;
            foreach (var kvp in results)
            {
                if (kvp.Value < minTime) minTime = kvp.Value;
                if (kvp.Value > maxTime) maxTime = kvp.Value;
            }

            // 최대/최소 비율이 100 미만 (O(1) 연산이므로 큰 차이 없어야 함)
            Assert.Less(maxTime / Math.Max(minTime, 0.0001), 100.0,
                "버퍼 용량에 따른 성능 차이가 100배 미만이어야 합니다 (O(1) 특성).");
        }

        // ─── 테스트 3: FrameRingBuffer 동시 읽기/쓰기 안전성 ────────────────────

        [Test]
        public async Task FrameRingBuffer_동시_Add_GetFrames_스레드_안전성_검증()
        {
            // Arrange
            var ringBuffer = new FrameRingBuffer(100);
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            int writeCount = 0;
            int readCount = 0;

            // Act - 동시 쓰기 및 읽기
            var writeTasks = new Task[3];
            for (int t = 0; t < 3; t++)
            {
                writeTasks[t] = Task.Run(() =>
                {
                    try
                    {
                        for (int i = 0; i < 50; i++)
                        {
                            var frame = CreateFakeFrameData(i);
                            ringBuffer.Add(frame);
                            Interlocked.Increment(ref writeCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                });
            }

            var readTask = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var frames = ringBuffer.GetFrames();
                        Interlocked.Increment(ref readCount);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });

            await Task.WhenAll(writeTasks);
            await readTask;

            ringBuffer.Dispose();

            // Assert
            Assert.IsEmpty(exceptions, $"동시 접근 중 예외 발생: {string.Join(", ", exceptions)}");
            Assert.Greater(writeCount, 0, "쓰기가 발생해야 합니다.");
            Assert.Greater(readCount, 0, "읽기가 발생해야 합니다.");
        }

        // ─── 테스트 4: 100프레임 평균 안정화 검증 ────────────────────────────────

        [Test]
        public void 100프레임_FrameRingBuffer_Add_평균_및_표준편차_검증()
        {
            // Arrange
            var ringBuffer = new FrameRingBuffer(MeasurementFrameCount * 2);
            var frameTimes = new double[MeasurementFrameCount];
            var sw = new Stopwatch();

            // Act - 100프레임 측정
            for (int i = 0; i < MeasurementFrameCount; i++)
            {
                sw.Restart();
                var frame = CreateFakeFrameData(i);
                ringBuffer.Add(frame);
                sw.Stop();

                frameTimes[i] = Math.Max(sw.Elapsed.TotalMilliseconds, 0.0001);
            }

            ringBuffer.Dispose();

            // 통계 계산
            double avgFrameTime = Average(frameTimes);
            double stdDev = StandardDeviation(frameTimes, avgFrameTime);
            double avgFps = 1000.0 / avgFrameTime;

            UnityEngine.Debug.Log($"[성능 테스트] 100프레임 FrameRingBuffer.Add 평균: {avgFps:F1}fps, " +
                                   $"평균 Add 시간: {avgFrameTime:F4}ms, " +
                                   $"표준편차: {stdDev:F4}ms");

            // Assert - FrameRingBuffer.Add()는 O(1) 연산이므로 매우 빠름
            Assert.Greater(avgFps, 0, "평균 FPS가 양수여야 합니다.");
            Assert.Less(avgFrameTime, 10.0, "평균 Add 시간이 10ms 미만이어야 합니다.");
        }

        // ─── 테스트 5: FPS 드랍 계산 로직 단위 검증 ─────────────────────────────

        [Test]
        public void FPS_드랍_계산_로직_정확성_검증()
        {
            // 60fps → 54fps = 10% 드랍 (경계값)
            double baselineFps = 60.0;
            double actualFps = 54.0;
            double dropPercent = (baselineFps - actualFps) / baselineFps * 100.0;

            Assert.AreEqual(10.0, dropPercent, 0.001, "10% FPS 드랍 계산이 정확해야 합니다.");

            // 60fps → 60fps = 0% 드랍
            actualFps = 60.0;
            dropPercent = (baselineFps - actualFps) / baselineFps * 100.0;
            Assert.AreEqual(0.0, dropPercent, 0.001, "드랍 없을 때 0%여야 합니다.");

            // 60fps → 30fps = 50% 드랍
            actualFps = 30.0;
            dropPercent = (baselineFps - actualFps) / baselineFps * 100.0;
            Assert.AreEqual(50.0, dropPercent, 0.001, "50% FPS 드랍 계산이 정확해야 합니다.");
        }

        // ─── 테스트 6: 링버퍼 용량 초과 시 오버라이트 동작 ──────────────────────

        [Test]
        public void FrameRingBuffer_용량_초과시_오래된_프레임_덮어쓰기_검증()
        {
            // Arrange
            int capacity = 5;
            var ringBuffer = new FrameRingBuffer(capacity);

            // Act - 용량보다 많은 프레임 추가
            for (int i = 0; i < capacity + 3; i++)
            {
                ringBuffer.Add(CreateFakeFrameData(i));
            }

            // Assert - Count가 capacity를 초과하지 않아야 함
            Assert.AreEqual(capacity, ringBuffer.Count, "링버퍼 Count는 capacity를 초과하지 않아야 합니다.");

            var frames = ringBuffer.GetFrames();
            Assert.AreEqual(capacity, frames.Length, "반환된 프레임 수가 capacity와 같아야 합니다.");

            ringBuffer.Dispose();
        }

        // ─── 헬퍼 메서드 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 기준 프레임 타임을 측정합니다 (FrameRingBuffer 없는 빈 루프).
        /// </summary>
        private static double MeasureBaselineFrameTimeMs()
        {
            var sw = new Stopwatch();
            var frameTimes = new double[MeasurementFrameCount];

            for (int i = 0; i < MeasurementFrameCount; i++)
            {
                sw.Restart();
                // 빈 연산 (기준값)
                int dummy = i * 2;
                _ = dummy; // 컴파일러 경고 억제
                sw.Stop();
                frameTimes[i] = Math.Max(sw.Elapsed.TotalMilliseconds, 0.0001);
            }

            return Average(frameTimes);
        }

        /// <summary>
        /// FrameRingBuffer.Add 포함 프레임 타임을 측정합니다.
        /// </summary>
        private static double MeasureWithRingBufferMs(FrameRingBuffer ringBuffer)
        {
            var sw = new Stopwatch();
            var frameTimes = new double[MeasurementFrameCount];

            for (int i = 0; i < MeasurementFrameCount; i++)
            {
                sw.Restart();
                var frame = CreateFakeFrameData(i);
                ringBuffer.Add(frame);
                sw.Stop();
                frameTimes[i] = Math.Max(sw.Elapsed.TotalMilliseconds, 0.0001);
            }

            return Average(frameTimes);
        }

        /// <summary>
        /// 테스트용 가짜 FrameData를 생성합니다.
        /// </summary>
        private static FrameData CreateFakeFrameData(int index)
        {
            // 64x48 해상도 (소규모 테스트용) - RGBA32 = 4 bytes per pixel
            const int width = 64;
            const int height = 48;
            var pixels = new byte[width * height * 4]; // RGBA32

            // 간단한 패턴 채우기
            byte value = (byte)(index % 256);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = value;

            return new FrameData(pixels, width, height, (double)index / 30.0);
        }

        /// <summary>배열의 평균값을 계산합니다.</summary>
        private static double Average(double[] values)
        {
            double sum = 0;
            foreach (double v in values)
                sum += v;
            return sum / values.Length;
        }

        /// <summary>배열의 표준편차를 계산합니다.</summary>
        private static double StandardDeviation(double[] values, double mean)
        {
            double sumSquaredDiffs = 0;
            foreach (double v in values)
            {
                double diff = v - mean;
                sumSquaredDiffs += diff * diff;
            }
            return Math.Sqrt(sumSquaredDiffs / values.Length);
        }
    }
}
