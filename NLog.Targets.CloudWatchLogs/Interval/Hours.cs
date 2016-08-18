using System;

namespace NLog.Targets.CloudWatchLogs.Interval
{
    /// <summary>
    /// Interprets hours value.
    /// </summary>
    public class Hours : IValueToTimeSpanInterpreter
    {
        public TimeSpan GetTimeSpan(double value) => TimeSpan.FromHours(value);
    }
}
