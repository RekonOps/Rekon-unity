using System;

namespace RekonOps.Rekon
{
    [Serializable]
    public class PerformanceSnapshot
    {
        public string render_pipeline;  // "URP", "HDRP", "Built-in"
        public int vsync;               // QualitySettings.vSyncCount
        public int target_fps;          // Application.targetFrameRate
        public float fixed_dt;          // Time.fixedDeltaTime
        public float battery;           // SystemInfo.batteryLevel (-1이면 0)
        public string battery_status;   // SystemInfo.batteryStatus.ToString()
    }
}
