using System;
using System.Collections.Generic;

namespace RekonOps.Rekon
{
    [Serializable]
    public class PerformanceTimeline
    {
        public List<PerformanceSample> samples = new List<PerformanceSample>();
        public List<PerformanceEvent> events = new List<PerformanceEvent>();
        public PerformanceSnapshot snapshot = new PerformanceSnapshot();
    }
}
