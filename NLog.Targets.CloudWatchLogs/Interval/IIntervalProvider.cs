using System;

namespace NLog.Targets.CloudWatchLogs.Interval
{
    /// <summary>
    /// Interface for providing sleep duration value for a given iteration.
    /// </summary>
    public interface IIntervalProvider
    {
        TimeSpan GetInterval(int iteration);
    }
}
