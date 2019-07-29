using System;
using NLog.Config;
using NLog.Common;
using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using System.Linq;
using NLog.Layouts;
using NLog.Targets.CloudWatchLogs.Credentials;

namespace NLog.Targets.CloudWatchLogs
{
    [Target("CloudWatchLogs")]
    public sealed class CloudWatchLogsTarget : TargetWithLayout
    {
        private readonly Lazy<CloudWatchLogsClientWrapper> _client;

        public CloudWatchLogsTarget()
        {
            _client = new Lazy<CloudWatchLogsClientWrapper>(() =>
                new CloudWatchLogsClientWrapper(
                    new AmazonCloudWatchLogsClient(
                        AWSCredentialsProvider.GetCredentialsOrDefault(
	                        this._awsAccessKeyIdRendered,
	                        this._awsSecretKeyRendered),
                        RegionEndpoint.GetBySystemName(this._awsRegionRendered)
                    ),
                    new CloudWatchLogsWrapperSettings(
	                    this._logGroupNameRendered,
	                    this._logStreamNameRendered)
                )
            );
        }

        private string _awsAccessKeyIdRendered => AWSAccessKeyId?.Render(LogEventInfo.CreateNullEvent()) ?? string.Empty;
        private string _awsSecretKeyRendered => AWSSecretKey?.Render(LogEventInfo.CreateNullEvent()) ?? string.Empty;
		private string _awsRegionRendered => AWSRegion?.Render(LogEventInfo.CreateNullEvent()) ?? "us-east-1";

		private string _logGroupNameRendered => LogGroupName?.Render(LogEventInfo.CreateNullEvent()) ?? string.Empty;
		private string _logStreamNameRendered => LogStreamName?.Render(LogEventInfo.CreateNullEvent()) ?? string.Empty;

		public SimpleLayout AWSAccessKeyId { get; set; }

        public SimpleLayout AWSSecretKey { get; set; }

        [RequiredParameter]
        public SimpleLayout AWSRegion { get; set; }

        [RequiredParameter]
        public SimpleLayout LogGroupName { get; set; }

        [RequiredParameter]
        public SimpleLayout LogStreamName { get; set; }

        protected override void Write(LogEventInfo logEvent)
        {
            _client.Value
                .WriteAsync(new[] { new InputLogEvent {
		            Message = Layout.Render(logEvent),
		            Timestamp = logEvent.TimeStamp
	            } })
                .Wait();
        }

        protected override void Write(AsyncLogEventInfo logEvent)
        {
            _client.Value
                .WriteAsync(new[] { new InputLogEvent {
		            Message = Layout.Render(logEvent.LogEvent),
		            Timestamp = logEvent.LogEvent.TimeStamp
	            } })
                .Wait();
        }

        protected override void Write(AsyncLogEventInfo[] logEvents)
        {
            _client.Value
                .WriteAsync(logEvents.Select(e => new InputLogEvent {
		            Message = Layout.Render(e.LogEvent),
		            Timestamp = e.LogEvent.TimeStamp
	            }))
                .Wait();
        }
    }
}