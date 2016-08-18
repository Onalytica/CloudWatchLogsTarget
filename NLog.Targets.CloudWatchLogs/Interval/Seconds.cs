using System;

namespace NLog.Targets.CloudWatchLogs.Interval
{
    /// <summary>
    /// Interprets seconds value.
    /// </summary>
    public class Seconds : IValueToTimeSpanInterpreter
    {
        public TimeSpan GetTimeSpan(double value) => TimeSpan.FromSeconds(value);
    }
}
