using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs
{
    public class CloudWatchLogsWrapperSettings
    {
        public CloudWatchLogsWrapperSettings(
            string logGroupName,
            string logStreamName,
            IIntervalProvider sleepDurationProvider,
            int retries
        )
        {
            LogGroupName = logGroupName;
            LogStreamName = logStreamName;
            SleepDurationProvider = sleepDurationProvider;
            Retries = retries;
        }

        public CloudWatchLogsWrapperSettings(
            string logGroupName,
            string logStreamName,
            IIntervalProvider sleepDurationProvider
        )
            : this(logGroupName, logStreamName, sleepDurationProvider, 5)
        {
        }

        public CloudWatchLogsWrapperSettings(
            string logGroupName,
            string logStreamName
        )
            : this(logGroupName, logStreamName, new ExponentialInterval<Seconds>(2))
        {
        }

        public string LogGroupName { get; private set; }
        public string LogStreamName { get; private set; }
        public int Retries { get; private set; }
        public IIntervalProvider SleepDurationProvider { get; private set; }
    }
}
