using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NLog.Targets.CloudWatchLogs.Interval
{
    /// <summary>
    /// Interface providing value to TimeSpan interpatation.
    /// </summary>
    public interface IValueToTimeSpanInterpreter
    {
        TimeSpan GetTimeSpan(double value);
    }
}
