using System;

namespace NLog.Targets.CloudWatchLogs.Interval
{
    /// <summary>
    /// Provides expenentially growing sleep duration values.
    /// </summary>
    public class ExponentialInterval<T> : IIntervalProvider where T : IValueToTimeSpanInterpreter, new()
    {
        private int _value;
        private IValueToTimeSpanInterpreter _interpreter;

        public ExponentialInterval(int value)
        {
            _value = value;
            _interpreter = new T();
        }

        public TimeSpan GetInterval(int iteration) => _interpreter.GetTimeSpan(Math.Pow(_value, iteration));
    }
}
