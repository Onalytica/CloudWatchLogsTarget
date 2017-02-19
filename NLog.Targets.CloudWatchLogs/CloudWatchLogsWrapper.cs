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
    /// Wraps the Amazon CloudWatch client adding request chaining, retry logic and multiple target instances support.
    /// </summary>
    public sealed class CloudWatchLogsClientWrapper
    {
        private readonly IAmazonCloudWatchLogs _client;
        private readonly CloudWatchLogsWrapperSettings _settings;
        private Task _currentTask = Task.FromResult(true);
        private static ConcurrentDictionary<string, string> _tokens = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Constructs CloudWatchLogsClientWrapper object.
        /// </summary>
        /// <param name="client">Instance of IAmazonCloudWatchLogs.</param>
        /// <param name="settings">The wrapper settings object.</param>
        public CloudWatchLogsClientWrapper(
            IAmazonCloudWatchLogs client,
            CloudWatchLogsWrapperSettings settings
        )
        {
            _client = client;
            _settings = settings;
        }

        /// <summary>
        /// Gets token key for the current log group and stream.
        /// </summary>
        /// <returns>The token key string.</returns>
        private string GetTokenKey() => $"{_settings.LogGroupName}:{_settings.LogStreamName}";

        /// <summary>
        /// Initializes the CloudWatch Logs group and stream and returns next sequence token.
        /// </summary>
        /// <returns>Next sequence token.</returns>
        private string Init(string key)
        {
            return Policy
                .Handle<AWSFailedRequestException>()
                .Or<AmazonCloudWatchLogsException>()
                .WaitAndRetryAsync(_settings.Retries, retryCount => _settings.SleepDurationProvider.GetInterval(retryCount))
                .ExecuteAsync(async () =>
                {
                    string nextToken = null;

                    // We check if the log group exists and create if it doesn't.
                    var logGroupsResponse = await _client.DescribeLogGroupsAsync(new DescribeLogGroupsRequest { LogGroupNamePrefix = _settings.LogGroupName });
                    if (!logGroupsResponse.Verify(nameof(_client.DescribeLogGroupsAsync)).LogGroups.Any(lg => lg.LogGroupName == _settings.LogGroupName))
                        (await _client.CreateLogGroupAsync(new CreateLogGroupRequest { LogGroupName = _settings.LogGroupName }))
                            .Verify(nameof(_client.CreateLogGroupAsync));

                    // We check if the log stream exsists within the log group and create if it doesn't or save the UploadSequenceToken if it does.
                    var logStreamsResponse = await _client.DescribeLogStreamsAsync(new DescribeLogStreamsRequest { LogGroupName = _settings.LogGroupName, LogStreamNamePrefix = _settings.LogStreamName });
                    var stream = logStreamsResponse.Verify(nameof(_client.DescribeLogStreamsAsync)).LogStreams.FirstOrDefault(ls => ls.LogStreamName == _settings.LogStreamName);
                    if (stream == null)
                        (await _client.CreateLogStreamAsync(new CreateLogStreamRequest { LogStreamName = _settings.LogStreamName, LogGroupName = _settings.LogGroupName }))
                            .Verify(nameof(_client.CreateLogStreamAsync));
                    else
                        nextToken = stream.UploadSequenceToken;

                    return nextToken;
                })
                .Result;
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
                            .WaitAndRetryAsync(_settings.Retries, retryCount => _settings.SleepDurationProvider.GetInterval(retryCount))
                            .ExecuteAsync(async () =>
                            {
                                var response = (await _client.PutLogEventsAsync(
                                    new PutLogEventsRequest
                                    {
                                        LogGroupName = _settings.LogGroupName,
                                        LogStreamName = _settings.LogStreamName,
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
