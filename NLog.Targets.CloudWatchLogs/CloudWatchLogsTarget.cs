using System;
using NLog.Config;
using NLog.Common;
using Amazon;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using System.Collections.Generic;
using System.Linq;
using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs
{
    [Target("CloudWatchLogs")]
    public sealed class CloudWatchLogsTarget : TargetWithLayout
    {
        private Lazy<CloudWatchLogsClientWrapper> _client;

        //public CloudWatchLogsTarget()
        //{
        //    _client = new Lazy<CloudWatchLogsClientWrapper>(() => 
        //        new CloudWatchLogsClientWrapper(
        //            new AmazonCloudWatchLogsClient(AWSAccessKeyId, AWSSecretKey, RegionEndpoint.GetBySystemName(AWSRegion)),
        //            LogGroupName,
        //            LogStreamName,
        //            new ExponentialInterval<Seconds>(2)));
        //}

        public CloudWatchLogsTarget(bool localCredentials = false)
        {
            if (localCredentials)
                _client = new Lazy<CloudWatchLogsClientWrapper>(() =>
                    new CloudWatchLogsClientWrapper(
                        new AmazonCloudWatchLogsClient(AWSAccessKeyId, AWSSecretKey,
                            RegionEndpoint.GetBySystemName(AWSRegion)),
                        LogGroupName,
                        LogStreamName,
                        new ExponentialInterval<Seconds>(2)));
            else
            {
                if (string.IsNullOrEmpty(AWSRegion))
                    _client = new Lazy<CloudWatchLogsClientWrapper>(() =>
                        new CloudWatchLogsClientWrapper(
                            new AmazonCloudWatchLogsClient(),
                            LogGroupName,
                            LogStreamName,
                            new ExponentialInterval<Seconds>(2)));
                else
                    _client = new Lazy<CloudWatchLogsClientWrapper>(() =>
                        new CloudWatchLogsClientWrapper(
                            new AmazonCloudWatchLogsClient(RegionEndpoint.GetBySystemName(AWSRegion)),
                            LogGroupName,
                            LogStreamName,
                            new ExponentialInterval<Seconds>(2)));
            }
        }

        [RequiredParameter]
        public string AWSAccessKeyId { get; set; }

        [RequiredParameter]
        public string AWSSecretKey { get; set; }

        [RequiredParameter]
        public string AWSRegion { get; set; }

        [RequiredParameter]
        public string LogGroupName { get; set; }

        [RequiredParameter]
        public string LogStreamName { get; set; }

        protected override void Write(LogEventInfo logEvent)
        {
            _client.Value
                .WriteAsync(new[] { new InputLogEvent { Message = Layout.Render(logEvent), Timestamp = logEvent.TimeStamp } })
                .Wait();
        }

        protected override void Write(AsyncLogEventInfo logEvent)
        {
            _client.Value
                .WriteAsync(new[] { new InputLogEvent { Message = Layout.Render(logEvent.LogEvent), Timestamp = logEvent.LogEvent.TimeStamp } })
                .Wait();
        }

        protected override void Write(AsyncLogEventInfo[] logEvents)
        {
            _client.Value
                .WriteAsync(logEvents.Select(e => new InputLogEvent { Message = Layout.Render(e.LogEvent), Timestamp = e.LogEvent.TimeStamp }))
                .Wait();
        }
    }
}