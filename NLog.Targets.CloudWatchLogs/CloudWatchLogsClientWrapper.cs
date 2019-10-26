using System.Linq;
using System.Collections.Generic;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Polly;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using NLog.Targets.CloudWatchLogs.Model;
using System;

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

        private struct KeyBits
        {
            public string LogGroupName { get; set; }
            public string LogStreamName { get; set; }

            public static KeyBits Parse(string key)
            {
                var bits = key.Split(':');
                return new KeyBits()
                {
                    LogGroupName = bits[0],
                    LogStreamName = bits[1]
                };
            }
        }

        /// <summary>
        /// Initializes the CloudWatch Logs group and stream and returns next sequence token.
        /// </summary>
        /// <returns>Next sequence token.</returns>
        private string Init(string key)
        {
            var keyBits = KeyBits.Parse(key);

            return Policy
                .Handle<AWSFailedRequestException>()
                .Or<AmazonCloudWatchLogsException>()
                .WaitAndRetryAsync(_settings.Retries, retryCount => _settings.SleepDurationProvider.GetInterval(retryCount))
                .ExecuteAsync(async () =>
                {
                    string nextToken = null;

                    // We check if the log group exists and create if it doesn't.
                    var logGroupsResponse = await _client.DescribeLogGroupsAsync(
                        new DescribeLogGroupsRequest { LogGroupNamePrefix = keyBits.LogGroupName }
                    ).ConfigureAwait(false);

                    if (!logGroupsResponse.Verify(nameof(_client.DescribeLogGroupsAsync))
                            .LogGroups.Any(lg => lg.LogGroupName == keyBits.LogGroupName))
                    {
                        var resp = await _client.CreateLogGroupAsync(
                            new CreateLogGroupRequest { LogGroupName = keyBits.LogGroupName }
                        ).ConfigureAwait(false);
                        resp.Verify(nameof(_client.CreateLogGroupAsync));
                    }

                    // We check if the log stream exsists within the log group and create if it doesn't 
                    // or save the UploadSequenceToken if it does.
                    var logStreamsResponse = await _client.DescribeLogStreamsAsync(
                        new DescribeLogStreamsRequest(keyBits.LogGroupName) { LogStreamNamePrefix = keyBits.LogStreamName }
                    ).ConfigureAwait(false);

                    var stream = logStreamsResponse.Verify(nameof(_client.DescribeLogStreamsAsync))
                            .LogStreams.FirstOrDefault(ls => ls.LogStreamName == keyBits.LogStreamName);
                    if (stream == null)
                    {
                        var resp = await _client.CreateLogStreamAsync(
                            new CreateLogStreamRequest(keyBits.LogGroupName, keyBits.LogStreamName)
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
            var groupedData = logData.GroupBy(d => d.TokenKey).ToList();

            foreach (var group in groupedData)
            {
                _currentTask = _currentTask
                    .ContinueWith(async prevt =>
                    {
                        try
                        {
                            await Policy
                                .Handle<AWSFailedRequestException>()
                                .Or<AmazonCloudWatchLogsException>()
                                .WaitAndRetryAsync(_settings.Retries, retryCount => _settings.SleepDurationProvider.GetInterval(retryCount))
                                .ExecuteAsync(async () =>
                                {
                                    var keyBits = KeyBits.Parse(group.Key);
                                    var request = new PutLogEventsRequest
                                    {
                                        LogGroupName = keyBits.LogGroupName,
                                        LogStreamName = keyBits.LogStreamName,
                                        SequenceToken = _tokens.GetOrAdd(group.Key, Init),
                                        LogEvents = group.Select(d => d.ToInputLogEvent()).OrderBy(e => e.Timestamp).ToList()
                                    };

                                    var response = (await _client.PutLogEventsAsync(request)                                        
                                        .ConfigureAwait(false))
                                        .Verify(nameof(_client.PutLogEventsAsync));

                                    _tokens.AddOrUpdate(group.Key, response.NextSequenceToken, (k, ov) => response.NextSequenceToken);
                                }).ConfigureAwait(false);
                        }
                        catch (InvalidSequenceTokenException)
                        {
                            _tokens.TryRemove(group.Key, out var _);
                            throw;
                        }
                    })
                    .Unwrap();
            }

            return _currentTask;
        }
    }
}
