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

        protected virtual LogDatum CreateDatum(LogEventInfo logEvent)
        {
            return new LogDatum()
            {
                Message = Layout.Render(logEvent),
                GroupName = LogGroupName,
                StreamName = LogStreamName,
                Timestamp = logEvent.TimeStamp
            };
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