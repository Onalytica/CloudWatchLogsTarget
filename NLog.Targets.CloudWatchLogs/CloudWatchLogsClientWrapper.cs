using System.Linq;
using System.Collections.Generic;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Polly;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NLog.Targets.CloudWatchLogs.Model;

namespace NLog.Targets.CloudWatchLogs
{
    /// <summary>
    /// Wraps the Amazon CloudWatch client adding request chaining, retry logic and multiple target instances support.
    /// </summary>
    public sealed class CloudWatchLogsClientWrapper
    {
        private readonly IAmazonCloudWatchLogs _client;
        private readonly CloudWatchLogsClientWrapperSettings _settings;
        private Task _currentTask = Task.FromResult(true);
        private static ConcurrentDictionary<string, string> _tokens = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Constructs CloudWatchLogsClientWrapper object.
        /// </summary>
        /// <param name="client">Instance of IAmazonCloudWatchLogs.</param>
        /// <param name="settings">The wrapper settings object.</param>
        public CloudWatchLogsClientWrapper(
            IAmazonCloudWatchLogs client,
            CloudWatchLogsClientWrapperSettings settings
        )
        {
            _client = client;
            _settings = settings;
        }

        /// <summary>
        /// Initializes the CloudWatch Logs group and stream and returns next sequence token.
        /// </summary>
        /// <returns>Next sequence token.</returns>
        private string Init(string key)
        {
            int colon = key.IndexOf(':');
            var logGroupName = key.Substring(0, colon);
            var logStreamName = key.Substring(colon + 1);

            return Policy
                .Handle<AWSFailedRequestException>()
                .Or<AmazonCloudWatchLogsException>()
                .WaitAndRetryAsync(_settings.Retries, retryCount => _settings.SleepDurationProvider.GetInterval(retryCount))
                .ExecuteAsync(async () =>
                {
                    string nextToken = null;

                    // We check if the log group exists and create if it doesn't.
                    var logGroupsResponse = await _client.DescribeLogGroupsAsync(
                        new DescribeLogGroupsRequest { LogGroupNamePrefix = logGroupName }
                    ).ConfigureAwait(false);

                    if (!logGroupsResponse.Verify(nameof(_client.DescribeLogGroupsAsync))
                            .LogGroups.Any(lg => lg.LogGroupName == logGroupName))
                    {
                        var resp = await _client.CreateLogGroupAsync(
                            new CreateLogGroupRequest { LogGroupName = logGroupName }
                        ).ConfigureAwait(false);
                        resp.Verify(nameof(_client.CreateLogGroupAsync));
                    }

                    // We check if the log stream exsists within the log group and create if it doesn't 
                    // or save the UploadSequenceToken if it does.
                    var logStreamsResponse = await _client.DescribeLogStreamsAsync(
                        new DescribeLogStreamsRequest { LogGroupName = logGroupName, LogStreamNamePrefix = logStreamName }
                    ).ConfigureAwait(false);

                    var stream = logStreamsResponse.Verify(nameof(_client.DescribeLogStreamsAsync))
                            .LogStreams.FirstOrDefault(ls => ls.LogStreamName == logStreamName);
                    if (stream == null)
                    {
                        var resp = await _client.CreateLogStreamAsync(
                            new CreateLogStreamRequest { LogStreamName = logStreamName, LogGroupName = logGroupName }
                        ).ConfigureAwait(false);
                        resp.Verify(nameof(_client.CreateLogStreamAsync));
                    }
                    else
                        nextToken = stream.UploadSequenceToken;

                    return nextToken;
                })
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Chains write requests and applies retry logic in case of failure.
        /// </summary>
        /// <param name="logData">Log events.</param>
        /// <returns>The write task.</returns>
        public Task WriteAsync(IEnumerable<LogDatum> logData)
        {
            var groupedData = logData.GroupBy(d => new { d.GroupName, d.StreamName }).ToList();

            foreach (var group in groupedData)
            {
                _currentTask = _currentTask
                    .ContinueWith(async prevt =>
                    {
                        var tokenKey = $"{group.Key.GroupName}:{group.Key.StreamName}";

                        try
                        {
                            await Policy
                                .Handle<AWSFailedRequestException>()
                                .Or<AmazonCloudWatchLogsException>()
                                .WaitAndRetryAsync(_settings.Retries, retryCount => _settings.SleepDurationProvider.GetInterval(retryCount))
                                .ExecuteAsync(async () =>
                                {
                                    var request = new PutLogEventsRequest
                                    {
                                        LogGroupName = group.Key.GroupName,
                                        LogStreamName = group.Key.StreamName,
                                        SequenceToken = _tokens.GetOrAdd(tokenKey, Init),
                                        LogEvents = group.Select(d => d.ToInputLogEvent()).OrderBy(e => e.Timestamp).ToList()
                                    };

                                    var response = (await _client.PutLogEventsAsync(request)                                        
                                        .ConfigureAwait(false))
                                        .Verify(nameof(_client.PutLogEventsAsync));

                                    _tokens.AddOrUpdate(tokenKey, response.NextSequenceToken, (k, ov) => response.NextSequenceToken);
                                }).ConfigureAwait(false);
                        }
                        catch (InvalidSequenceTokenException)
                        {
                            _tokens.TryRemove(tokenKey, out var _);
                            throw;
                        }
                    })
                    .Unwrap();
            }

            return _currentTask;
        }
    }
}
