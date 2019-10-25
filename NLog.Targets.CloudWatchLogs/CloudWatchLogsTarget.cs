using System;
using NLog.Config;
using NLog.Common;
using Amazon;
using Amazon.Runtime;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using System.Linq;
using NLog.Targets.CloudWatchLogs.Credentials;
using NLog.Targets.CloudWatchLogs.Model;

namespace NLog.Targets.CloudWatchLogs
{
    [Target("CloudWatchLogs")]
    public class CloudWatchLogsTarget : TargetWithLayout
    {
        private readonly Lazy<CloudWatchLogsClientWrapper> _client;

        public CloudWatchLogsTarget()
        {
            _client = new Lazy<CloudWatchLogsClientWrapper>(() =>
                new CloudWatchLogsClientWrapper(
                    new AmazonCloudWatchLogsClient(
                        AWSCredentialsProvider.GetCredentialsOrDefault(AWSAccessKeyId, AWSSecretKey),
                        RegionEndpoint.GetBySystemName(AWSRegion)
                    ),
                    new CloudWatchLogsClientWrapperSettings()
                )
            );
        }

        public string AWSAccessKeyId { get; set; }

        public string AWSSecretKey { get; set; }

        [RequiredParameter]
        public string AWSRegion { get; set; }

        [RequiredParameter]
        public string LogGroupName { get; set; } = "unspecified";

        [RequiredParameter]
        public string LogStreamName { get; set; } = "unspecified";

        /// <summary>
        /// Function to generate LogGroupName on a per-message basis.
        /// </summary>
        public Func<string> LogGroupNameFunc { get; set; }

        /// <summary>
        /// Function to generate LogStreamName on a per-message basis.
        /// </summary>
        public Func<string> LogStreamNameFunc { get; set; }

        protected virtual LogDatum CreateDatum(LogEventInfo logEvent)
        {
            var result = new LogDatum()
            {
                Message = Layout.Render(logEvent),
                GroupName = LogGroupNameFunc?.Invoke() ?? LogGroupName,
                StreamName = LogStreamNameFunc?.Invoke() ?? LogStreamName,
                Timestamp = logEvent.TimeStamp
            };

            return result;
        }

        protected override void Write(LogEventInfo logEvent)
        {
            _client.Value
                .WriteAsync(new[] { CreateDatum(logEvent) })
                .Wait();
        }

        protected override void Write(AsyncLogEventInfo logEvent)
        {
            _client.Value
                .WriteAsync(new[] { CreateDatum(logEvent.LogEvent) })
                .Wait();
        }

        [Obsolete]
        protected override void Write(AsyncLogEventInfo[] logEvents)
        {
            _client.Value
                .WriteAsync(logEvents.Select(e => CreateDatum(e.LogEvent)))
                .Wait();
        }
    }
}