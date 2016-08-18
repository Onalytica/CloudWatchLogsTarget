using System;

namespace NLog.Targets.CloudWatchLogs.Interval
{
    /// <summary>
    /// Interprets minutes value.
    /// </summary>
    public class Minutes : IValueToTimeSpanInterpreter
    {
        public TimeSpan GetTimeSpan(double value) => TimeSpan.FromMinutes(value);
    }
}
