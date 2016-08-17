using System;
using System.Linq;
using System.Collections.Generic;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Polly;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace NLog.Targets.CloudWatchLogs
{
    /// <summary>
    /// Wraps the Amazon CloudWatch client adding request chaining and retry logic.
    /// </summary>
    public sealed class CloudWatchLogsClientWrapper
    {
        private IAmazonCloudWatchLogs _client;
        private string _logGroupName;
        private string _logStreamName;
        private string _sequenceToken;
        private int _retries = 5;
        private int _backoffBaseInSeconds = 2;
        private Task _currentTask;
        private static ConcurrentDictionary<string, string> _tokens = new ConcurrentDictionary<string, string>();

        public CloudWatchLogsClientWrapper(IAmazonCloudWatchLogs client, string logGroupName, string logStreamName)
        {
            _client = client;
            _logGroupName = logGroupName;
            _logStreamName = logStreamName;

            Init();
        }

        /// <summary>
        /// Gets token key for the current log group and stream.
        /// </summary>
        /// <returns>The token key string.</returns>
        private string GetTokenKey() => $"{_logGroupName}:{_logStreamName}";

        /// <summary>
        /// Initializes the CloudWatch Logs group and stream.
        /// </summary>
        private void Init()
        {
            _currentTask = Policy
                .Handle<AWSFailedRequestException>()
                .Or<AmazonCloudWatchLogsException>()
                .WaitAndRetryAsync(_retries, retryCount => TimeSpan.FromSeconds(Math.Pow(_backoffBaseInSeconds, retryCount)))
                .ExecuteAsync(async () =>
                {
                    string nextToken = null;

                    // We check if the log group exists and create if it doesn't.
                    var logGroupsResponse = await _client.DescribeLogGroupsAsync(new DescribeLogGroupsRequest { LogGroupNamePrefix = _logGroupName });
                    if (!logGroupsResponse.Verify(nameof(_client.DescribeLogGroupsAsync)).LogGroups.Any(lg => lg.LogGroupName == _logGroupName))
                        (await _client.CreateLogGroupAsync(new CreateLogGroupRequest { LogGroupName = _logGroupName }))
                            .Verify(nameof(_client.CreateLogGroupAsync));

                    // We check if the log stream exsists within the log group and create if it doesn't or save the UploadSequenceToken if it does.
                    var logStreamsResponse = await _client.DescribeLogStreamsAsync(new DescribeLogStreamsRequest { LogGroupName = _logGroupName, LogStreamNamePrefix = _logStreamName });
                    var stream = logStreamsResponse.Verify(nameof(_client.DescribeLogStreamsAsync)).LogStreams.FirstOrDefault(ls => ls.LogStreamName == _logStreamName);
                    if (stream == null)
                        (await _client.CreateLogStreamAsync(new CreateLogStreamRequest { LogStreamName = _logStreamName, LogGroupName = _logGroupName }))
                            .Verify(nameof(_client.CreateLogStreamAsync));
                    else
                        nextToken = stream.UploadSequenceToken;

                    _tokens.AddOrUpdate(GetTokenKey(), nextToken, (k,ov) => nextToken);
                }, false);
        }

        /// <summary>
        /// Chains write requests and applies retry logic in case of failure.
        /// </summary>
        /// <param name="logEvents">Log events.</param>
        /// <returns>The write task.</returns>
        public Task WriteAsync(IEnumerable<InputLogEvent> logEvents)
        {
            _currentTask = _currentTask
                .ContinueWith(prevt => Policy
                    .Handle<AWSFailedRequestException>()
                    .Or<AmazonCloudWatchLogsException>()
                    .WaitAndRetryAsync(_retries, retryCount => TimeSpan.FromSeconds(Math.Pow(_backoffBaseInSeconds, retryCount)))
                    .ExecuteAsync(async () =>
                    {
                        var response = (await _client.PutLogEventsAsync(
                            new PutLogEventsRequest
                            {
                                LogGroupName = _logGroupName,
                                LogStreamName = _logStreamName,
                                SequenceToken = _sequenceToken,
                                LogEvents = logEvents.OrderBy(e => e.Timestamp).ToList()
                            })
                            .ConfigureAwait(false))
                            .Verify(nameof(_client.PutLogEventsAsync));
                        _sequenceToken = response.NextSequenceToken;
                        return response;
                    }, false))
                .Unwrap();

            return _currentTask;
        }
    }
}
