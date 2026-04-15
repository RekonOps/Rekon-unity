using System;

namespace RekonOps.Rekon
{
    [Serializable]
    public class PerformanceEvent
    {
        public float t;       // 이벤트 발생 시점 (경과 초)
        public string type;   // "scene_load", "timescale_change" 등
        public string value;  // 이벤트 값 (씬 이름, TimeScale 값 등)
    }
}
