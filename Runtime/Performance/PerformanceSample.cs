using System;

namespace RekonOps.Rekon
{
    [Serializable]
    public class PerformanceSample
    {
        public float t;           // 영상 시작 기준 경과 초
        public float fps;         // 5초 슬라이딩 평균 FPS
        public int heap_used;     // MB, GC.GetTotalMemory
        public int heap_total;    // MB, Profiler.GetMonoHeapSizeLong
        public int gpu_mem;       // MB, Profiler.GetAllocatedMemoryForGraphicsDriver
        public int tex_mem;       // MB, Texture.totalTextureMemory
        public float cpu_ms;      // FrameTimingManager
        public float gpu_ms;      // FrameTimingManager
        public float timescale;   // Time.timeScale
        public bool network;      // Application.internetReachability
        public string scene;      // 현재 씬 이름
    }
}
