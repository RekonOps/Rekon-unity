using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace RekonOps.Rekon
{
    /// <summary>
    /// Update() 기반 1초 간격 샘플링으로 성능 타임라인을 수집하는 MonoBehaviour.
    ///
    /// 플랜별 수집 범위 (성능 타임라인 = team_pro 전용, 2026-05-20 정책):
    ///   - free / team : timescale, network, scene 만 수집 (단, backend create-report 가
    ///                   team_pro 만 저장하므로 실제 timeline 미적재)
    ///   - team_pro    : + FPS(5초 슬라이딩 평균) + 힙 메모리 + GPU 메모리 + 텍스처 메모리
    ///                   + FrameTimingManager + 씬 전환 이벤트 + TimeScale 변화 + PerformanceSnapshot
    ///                   → '리플레이' 경험 (성능 + 로그 싱크 결합)의 성능 축
    ///
    /// 링버퍼:
    ///   고정 크기 배열(_sampleBuffer)과 _sampleHead 포인터를 사용해
    ///   LogRingBuffer와 동일한 패턴으로 최근 60개 샘플만 유지합니다.
    ///
    /// FPS 슬라이딩 평균:
    ///   매 프레임 Queue에 instantaneous FPS를 추가하고,
    ///   5초 분량(최대 300개 @60fps) 초과분은 Dequeue하여 슬라이딩 윈도우를 유지합니다.
    /// </summary>
    public class PerformanceTimelineCollector : MonoBehaviour
    {
        private bool _isCollecting;
        private string _plan = "free";
        private float _collectionStartTime;
        private float _nextSampleTime;

        // 링버퍼
        private PerformanceSample[] _sampleBuffer;
        private int _sampleHead;
        private int _sampleCount;
        private int _maxSamples = 60;

        // FPS 5초 슬라이딩 평균
        private Queue<float> _fpsHistory = new Queue<float>();
        private const int FPS_WINDOW = 5; // 초

        // 이벤트 목록
        private List<PerformanceEvent> _events = new List<PerformanceEvent>();

        // 변화 감지용 이전 값
        private string _prevScene;
        private float _prevTimescale;

        /// <summary>
        /// 수집을 시작합니다. 이미 수집 중이면 재시작합니다.
        /// </summary>
        /// <param name="plan">플랜 식별자 ("free" | "team" | "team_pro")</param>
        public void StartCollecting(string plan)
        {
            _plan = plan ?? "free";
            _isCollecting = true;
            _collectionStartTime = Time.realtimeSinceStartup;
            _nextSampleTime = _collectionStartTime + 1.0f;
            _sampleBuffer = new PerformanceSample[_maxSamples];
            _sampleHead = 0;
            _sampleCount = 0;
            _fpsHistory.Clear();
            _events.Clear();
            _prevScene = SceneManager.GetActiveScene().name;
            _prevTimescale = Time.timeScale;

            // 씬 전환 이벤트 구독 (team_pro 전용이지만 구독 자체는 항상 등록 후 StopCollecting에서 해제)
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// 수집을 중지하고 PerformanceTimeline을 반환합니다.
        /// </summary>
        public PerformanceTimeline StopCollecting()
        {
            _isCollecting = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;

            var timeline = new PerformanceTimeline();

            // 링버퍼에서 오래된 순서대로 추출
            int start = (_sampleCount >= _maxSamples) ? _sampleHead : 0;
            int count = Math.Min(_sampleCount, _maxSamples);
            for (int i = 0; i < count; i++)
            {
                int idx = (start + i) % _maxSamples;
                timeline.samples.Add(_sampleBuffer[idx]);
            }

            timeline.events = new List<PerformanceEvent>(_events);

            // Team Pro만 스냅샷 수집. 그 외 플랜은 명시적으로 null 설정하여 허위 표시 방지
            timeline.snapshot = (_plan == "team_pro") ? CollectSnapshot() : null;

            return timeline;
        }

        void Update()
        {
            if (!_isCollecting) return;

            // FPS 히스토리 누적 (매 프레임, team 이상만 필요하지만 미리 수집)
            float currentFps = 1f / Time.unscaledDeltaTime;
            _fpsHistory.Enqueue(currentFps);

            // 5초 × 60fps = 최대 300개 유지
            while (_fpsHistory.Count > FPS_WINDOW * 60)
                _fpsHistory.Dequeue();

            // TimeScale 변화 감지 (team_pro 전용)
            if (_plan == "team_pro" && Math.Abs(Time.timeScale - _prevTimescale) > 0.01f)
            {
                float t = Time.realtimeSinceStartup - _collectionStartTime;
                _events.Add(new PerformanceEvent
                {
                    t = t,
                    type = "timescale_change",
                    value = Time.timeScale.ToString("F2")
                });
                _prevTimescale = Time.timeScale;
            }

            // 1초 간격 샘플링
            float now = Time.realtimeSinceStartup;
            if (now < _nextSampleTime) return;
            _nextSampleTime = now + 1.0f;

            CollectSample(now - _collectionStartTime);
        }

        private void CollectSample(float elapsed)
        {
            var sample = new PerformanceSample();
            sample.t = elapsed;
            sample.timescale = Time.timeScale;
            sample.network = Application.internetReachability != NetworkReachability.NotReachable;
            sample.scene = SceneManager.GetActiveScene().name;

            // 성능 타임라인은 team_pro 전용 (리플레이 경험 = 성능 + 로그 싱크)
            if (_plan == "team_pro")
            {
                // FPS 5초 슬라이딩 평균 계산
                float sum = 0f;
                int count = 0;
                foreach (var f in _fpsHistory)
                {
                    sum += f;
                    count++;
                }
                sample.fps = count > 0 ? sum / count : 0f;

                // 힙 메모리 (MB)
                sample.heap_used = (int)(System.GC.GetTotalMemory(false) / (1024 * 1024));
                sample.heap_total = (int)(Profiler.GetMonoHeapSizeLong() / (1024 * 1024));
            }

            // Team Pro: GPU + 텍스처 + 프레임 타이밍
            if (_plan == "team_pro")
            {
                sample.gpu_mem = (int)(Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024));
                sample.tex_mem = (int)(Texture.totalTextureMemory / (1024 * 1024));

                // FrameTimingManager: 플랫폼 미지원 시 0으로 폴백
                try
                {
                    FrameTimingManager.CaptureFrameTimings();
                    var timings = new FrameTiming[1];
                    uint capturedCount = FrameTimingManager.GetLatestTimings(1, timings);
                    if (capturedCount > 0)
                    {
                        sample.cpu_ms = (float)timings[0].cpuFrameTime;
                        sample.gpu_ms = (float)timings[0].gpuFrameTime;
                    }
                }
                catch
                {
                    // 플랫폼 미지원 시 0으로 폴백 (sample.cpu_ms, sample.gpu_ms 기본값 0)
                }
            }

            // 링버퍼에 저장 (LogRingBuffer와 동일한 패턴)
            _sampleBuffer[_sampleHead] = sample;
            _sampleHead = (_sampleHead + 1) % _maxSamples;
            _sampleCount++;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_isCollecting) return;
            if (_plan != "team_pro") return;

            float t = Time.realtimeSinceStartup - _collectionStartTime;
            _events.Add(new PerformanceEvent
            {
                t = t,
                type = "scene_load",
                value = scene.name
            });
        }

        private PerformanceSnapshot CollectSnapshot()
        {
            return new PerformanceSnapshot
            {
                render_pipeline = QualitySettings.renderPipeline != null
                    ? QualitySettings.renderPipeline.name
                    : "Built-in",
                vsync = QualitySettings.vSyncCount,
                target_fps = Application.targetFrameRate,
                fixed_dt = Time.fixedDeltaTime,
                battery = SystemInfo.batteryLevel >= 0 ? SystemInfo.batteryLevel : 0f,
                battery_status = SystemInfo.batteryStatus.ToString()
            };
        }

        void OnDestroy()
        {
            if (_isCollecting)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _isCollecting = false;
            }
        }
    }
}
