using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs
{
    public class CloudWatchLogsClientWrapperSettings
    {
        public CloudWatchLogsClientWrapperSettings(
            IIntervalProvider sleepDurationProvider,
            int retries
        )
        {
            SleepDurationProvider = sleepDurationProvider;
            Retries = retries;
        }

        public CloudWatchLogsClientWrapperSettings(
            IIntervalProvider sleepDurationProvider
        )
            : this(sleepDurationProvider, 5)
        {
        }

        public CloudWatchLogsClientWrapperSettings(): this(new ExponentialInterval<Seconds>(2))
        {
        }

        public int Retries { get; private set; }
        public IIntervalProvider SleepDurationProvider { get; private set; }
    }
}
