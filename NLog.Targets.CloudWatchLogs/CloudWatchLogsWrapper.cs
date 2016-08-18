using System;
using System.Linq;
using System.Collections.Generic;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Polly;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs
{
    /// <summary>
    /// Wraps the Amazon CloudWatch client adding request chaining, retry logic and multiple target instances support.
    /// </summary>
    public sealed class CloudWatchLogsClientWrapper
    {
        private IAmazonCloudWatchLogs _client;
        private string _logGroupName;
        private string _logStreamName;
        private int _retries = 5;
        private Task _currentTask = Task.FromResult(true);
        private IIntervalProvider _sleepDurationProvider;
        private static ConcurrentDictionary<string, string> _tokens = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Constructs CloudWatchLogsClientWrapper object.
        /// </summary>
        /// <param name="client">Instance of IAmazonCloudWatchLogs.</param>
        /// <param name="logGroupName">The log group name to be used.</param>
        /// <param name="logStreamName">The log stream name within the group to be used.</param>
        /// <param name="sleepDurationProvider">The sleep duration provider used for the retry policy.</param>
        public CloudWatchLogsClientWrapper(
            IAmazonCloudWatchLogs client, 
            string logGroupName, 
            string logStreamName, 
            IIntervalProvider sleepDurationProvider)
        {
            _client = client;
            _logGroupName = logGroupName;
            _logStreamName = logStreamName;
            _sleepDurationProvider = sleepDurationProvider;
        }

        /// <summary>
        /// Gets token key for the current log group and stream.
        /// </summary>
        /// <returns>The token key string.</returns>
        private string GetTokenKey() => $"{_logGroupName}:{_logStreamName}";

        /// <summary>
        /// Initializes the CloudWatch Logs group and stream and returns next sequence token.
        /// </summary>
        /// <returns>Next sequence token.</returns>
        private string Init(string key)
        {
            return Policy
                .Handle<AWSFailedRequestException>()
                .Or<AmazonCloudWatchLogsException>()
                .WaitAndRetry(_retries, retryCount => _sleepDurationProvider.GetInterval(retryCount))
                .Execute(() =>
                {
                    string nextToken = null;

                    // We check if the log group exists and create if it doesn't.
                    var logGroupsResponse = _client.DescribeLogGroups(new DescribeLogGroupsRequest { LogGroupNamePrefix = _logGroupName });
                    if (!logGroupsResponse.Verify(nameof(_client.DescribeLogGroups)).LogGroups.Any(lg => lg.LogGroupName == _logGroupName))
                        _client.CreateLogGroup(new CreateLogGroupRequest { LogGroupName = _logGroupName })
                            .Verify(nameof(_client.CreateLogGroup));

                    // We check if the log stream exsists within the log group and create if it doesn't or save the UploadSequenceToken if it does.
                    var logStreamsResponse = _client.DescribeLogStreams(new DescribeLogStreamsRequest { LogGroupName = _logGroupName, LogStreamNamePrefix = _logStreamName });
                    var stream = logStreamsResponse.Verify(nameof(_client.DescribeLogStreams)).LogStreams.FirstOrDefault(ls => ls.LogStreamName == _logStreamName);
                    if (stream == null)
                        _client.CreateLogStream(new CreateLogStreamRequest { LogStreamName = _logStreamName, LogGroupName = _logGroupName })
                            .Verify(nameof(_client.CreateLogStream));
                    else
                        nextToken = stream.UploadSequenceToken;

                    return nextToken;
                });
        }

        /// <summary>
        /// Chains write requests and applies retry logic in case of failure.
        /// </summary>
        /// <param name="logEvents">Log events.</param>
        /// <returns>The write task.</returns>
        public Task WriteAsync(IEnumerable<InputLogEvent> logEvents)
        {
            _currentTask = _currentTask
                .ContinueWith(async prevt =>
                {
                    try
                    {
                        return await Policy
                            .Handle<AWSFailedRequestException>()
                            .Or<AmazonCloudWatchLogsException>()
                            .WaitAndRetryAsync(_retries, retryCount => _sleepDurationProvider.GetInterval(retryCount))
                            .ExecuteAsync(async () =>
                            {
                                var response = (await _client.PutLogEventsAsync(
                                    new PutLogEventsRequest
                                    {
                                        LogGroupName = _logGroupName,
                                        LogStreamName = _logStreamName,
                                        SequenceToken = _tokens.GetOrAdd(GetTokenKey(), Init),
                                        LogEvents = logEvents.OrderBy(e => e.Timestamp).ToList()
                                    })
                                    .ConfigureAwait(false))
                                    .Verify(nameof(_client.PutLogEventsAsync));
                                _tokens.AddOrUpdate(GetTokenKey(), response.NextSequenceToken, (k, ov) => response.NextSequenceToken);
                                return response;
                            }, false);
                    }
                    catch (InvalidSequenceTokenException)
                    {
                        string token;
                        _tokens.TryRemove(GetTokenKey(), out token);
                        throw;
                    }
                })
                .Unwrap();

            return _currentTask;
        }
    }
}
