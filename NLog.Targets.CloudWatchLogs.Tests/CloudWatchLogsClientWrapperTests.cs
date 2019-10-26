﻿using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using NLog.Targets.CloudWatchLogs.Interval;
using Xunit;
using NLog.Targets.CloudWatchLogs.Model;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    internal static class IAmazonCloudWatchLogsMockHelpers
    {
        internal static Mock<IAmazonCloudWatchLogs> InitDescribeGroup(this Mock<IAmazonCloudWatchLogs> mock)
        {
            mock
            .Setup(m => m.DescribeLogGroupsAsync(It.IsAny<DescribeLogGroupsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new DescribeLogGroupsResponse { HttpStatusCode = HttpStatusCode.OK }));
            return mock;
        }

        internal static Mock<IAmazonCloudWatchLogs> InitCreateGroup(this Mock<IAmazonCloudWatchLogs> mock)
        {
            mock
            .Setup(m => m.CreateLogGroupAsync(It.IsAny<CreateLogGroupRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new CreateLogGroupResponse { HttpStatusCode = HttpStatusCode.OK }));
            return mock;
        }

        internal static Mock<IAmazonCloudWatchLogs> InitGroup(this Mock<IAmazonCloudWatchLogs> mock) => mock.InitDescribeGroup().InitCreateGroup();

        internal static Mock<IAmazonCloudWatchLogs> InitDescribeStream(this Mock<IAmazonCloudWatchLogs> mock)
        {
            mock
            .Setup(m => m.DescribeLogStreamsAsync(It.IsAny<DescribeLogStreamsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new DescribeLogStreamsResponse { HttpStatusCode = HttpStatusCode.OK }));
            return mock;
        }

        internal static Mock<IAmazonCloudWatchLogs> InitCreateStream(this Mock<IAmazonCloudWatchLogs> mock)
        {
            mock
            .Setup(m => m.CreateLogStreamAsync(It.IsAny<CreateLogStreamRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new CreateLogStreamResponse { HttpStatusCode = HttpStatusCode.OK }));
            return mock;
        }

        internal static Mock<IAmazonCloudWatchLogs> InitStream(this Mock<IAmazonCloudWatchLogs> mock) => mock.InitDescribeStream().InitCreateStream();

        internal static Mock<IAmazonCloudWatchLogs> Init(this Mock<IAmazonCloudWatchLogs> mock) => mock.InitGroup().InitStream();
    }

    public class CloudWatchLogsClientWrapperTests
    {
        private IIntervalProvider CreateIntervalProvider() =>
            Mock.Of<IIntervalProvider>(m => m.GetInterval(It.IsAny<int>()) == TimeSpan.Zero);

        private IEnumerable<LogDatum> CreateEvents(string groupName, string streamName, int count = 3)
        {
            return Enumerable.Range(1, count)
                .OrderByDescending(i => i)
                .Select(i => new LogDatum
                {
                    Timestamp = DateTime.Now.AddSeconds(-i),
                    Message = i.ToString(),
                    GroupName = groupName,
                    StreamName = streamName
                });
        }

        private IEnumerable<LogDatum> CreateEvents(int count = 3) => CreateEvents("unspecified", "unspecified", count);

        private static string Guid() => System.Guid.NewGuid().ToString();


        [Fact]
        public async Task WriteAsync_Should_Retry_Failed_Request_3_Times()
        {
            // arrange
            int actualRetries = 0, expectedRetries = 3;
            var clientMock = new Mock<IAmazonCloudWatchLogs>().Init();
            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    actualRetries++;
                    return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = actualRetries <= (expectedRetries - 1) ? HttpStatusCode.BadGateway : HttpStatusCode.OK });
                });

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object, 
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await target.WriteAsync(CreateEvents());
            
            // assert
            Assert.Equal(expectedRetries, actualRetries);
        }

        [Fact]
        public async Task WriteAsync_Should_Throw_AWSFailedRequestException()
        {
            // arrange
            var clientMock = new Mock<IAmazonCloudWatchLogs>().Init();

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.BadGateway });
                });

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await Assert.ThrowsAsync<AWSFailedRequestException>(() =>target.WriteAsync(CreateEvents()));
        }

        [Fact]
        public async Task WriteAsync_Should_Throw_InvalidSequenceTokenException()
        {
            // arrange
            var clientMock = new Mock<IAmazonCloudWatchLogs>().Init();

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    throw new InvalidSequenceTokenException("invalid token");
                });

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await Assert.ThrowsAsync<InvalidSequenceTokenException>(() => target.WriteAsync(CreateEvents()));
        }

        [Fact]
        public async Task WriteAsync_Should_Queue_Log_Requests_And_Preserve_Their_Order()
        {
            // arrange
            var rand = new Random();
            var clientMock = new Mock<IAmazonCloudWatchLogs>().Init();
            var expectedSequence = Enumerable.Range(0, 20);
            List<int> actualSequence = new List<int>(), retried = new List<int>();

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    return Task
                        .Delay(rand.Next() % 500) // Simulates delay on the API side
                        .ContinueWith(t =>
                        {
                            PutLogEventsResponse result;
                            var messageInt = int.Parse(r.LogEvents.First().Message);
                            var shouldRetry = (messageInt % 5 == 0) && !retried.Contains(messageInt); // retry every fifth element once.
                            if (shouldRetry)
                            {
                                retried.Add(messageInt);
                                result = new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.BadRequest };
                            }
                            else
                            {
                                actualSequence.Add(messageInt);
                                result = new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK };
                            }
                            return result;
                        });
                });

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await Task.WhenAll(expectedSequence
                .Select(i => target.WriteAsync(new[] { new LogDatum { Message = i.ToString() } }))
                .ToArray());

            // assert
            Assert.True(expectedSequence.SequenceEqual(actualSequence), "Message sequence should be preserved.");
            Assert.True(expectedSequence.Where(i => i % 5 == 0).SequenceEqual(retried), "Expected and actual retries should match.");
        }

        [Fact]
        public async Task Init_Should_Retry_Failed_Requests()
        {
            // arrange
            int actualRetries = 0, expectedRetries = 3;
            var clientMock = new Mock<IAmazonCloudWatchLogs>().InitCreateGroup().InitStream();
            clientMock
                .Setup(m => m.DescribeLogGroupsAsync(It.IsAny<DescribeLogGroupsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<DescribeLogGroupsRequest, CancellationToken>((r, c) =>
                {
                    actualRetries++;
                    bool success = actualRetries > (expectedRetries - 1);
                    return Task.FromResult(new DescribeLogGroupsResponse {
                        HttpStatusCode = success ? HttpStatusCode.OK : HttpStatusCode.BadGateway,
                        LogGroups = success ? new List<LogGroup> { new LogGroup { LogGroupName = r.LogGroupNamePrefix } } : null
                    });
                });
            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns(Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK }));

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await target.WriteAsync(CreateEvents(Guid(), Guid()));

            // assert
            Assert.Equal(expectedRetries, actualRetries);
        }

        [Fact]
        public async Task Init_Should_Retry_Requests_Resulting_In_Amazon_Exceptions()
        {
            // arrange
            int actualRetries = 0, expectedRetries = 3;
            var clientMock = new Mock<IAmazonCloudWatchLogs>().InitStream();
            clientMock
                .Setup(m => m.DescribeLogGroupsAsync(It.IsAny<DescribeLogGroupsRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new DescribeLogGroupsResponse { HttpStatusCode = HttpStatusCode.OK }));
            clientMock
                .Setup(m => m.CreateLogGroupAsync(It.IsAny<CreateLogGroupRequest>(), It.IsAny<CancellationToken>()))
                .Returns<CreateLogGroupRequest, CancellationToken>((r, c) =>
                {
                    actualRetries++;
                    if (actualRetries <= (expectedRetries - 1))
                        throw new OperationAbortedException("something happened.");

                    return Task.FromResult(new CreateLogGroupResponse { HttpStatusCode = HttpStatusCode.OK });
                });
            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns(Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK }));

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await target.WriteAsync(CreateEvents(Guid(), Guid()));

            // assert
            Assert.Equal(expectedRetries, actualRetries);
        }

        [Fact]
        public async Task WriteAsync_Should_Order_Log_Events_Chronologically_Before_Sending()
        {
            // arange
            var clientMock = new Mock<IAmazonCloudWatchLogs>().Init();
            var data = new[]
            {
                new LogDatum { Timestamp = DateTime.UtcNow },
                new LogDatum { Timestamp = DateTime.UtcNow.AddSeconds(-1) }
            };
            List<DateTime> actual = null, expected = data.Select(d => d.Timestamp.Value).OrderBy(t => t).ToList();

            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns<PutLogEventsRequest, CancellationToken>((r,c) =>
               {
                   actual = r.LogEvents.Select(e => e.Timestamp).ToList();
                   return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK });
               });

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await target.WriteAsync(data);

            // assert
            Assert.True(expected.SequenceEqual(actual), "Actual events sequence should be ordered chronologically.");
        }

        [Fact]
        public async Task WriteAsync_Should_Handle_Concurent_Requests_From_Multiple_Target_Insances()
        {
            // arange
            var clientMock = new Mock<IAmazonCloudWatchLogs>().InitGroup().InitCreateStream();
            var tokens = new List<int>();
            string groupName = Guid(), streamName = Guid();

            clientMock
                .Setup(m => m.DescribeLogStreamsAsync(It.IsAny<DescribeLogStreamsRequest>(), It.IsAny<CancellationToken>()))
                .Returns((DescribeLogStreamsRequest req, CancellationToken token) => Task.FromResult(new DescribeLogStreamsResponse
                {
                    HttpStatusCode = HttpStatusCode.OK,
                    LogStreams = new List<LogStream> { new LogStream { LogStreamName = req.LogStreamNamePrefix, UploadSequenceToken = "1" } }
                }));

            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
               {
                   int tokenInt = int.Parse(r.SequenceToken);
                   lock (tokens)
                   {
                       if (tokens.Any(t => t == tokenInt))
                           throw new InvalidSequenceTokenException("Token already used.");

                       tokens.Add(tokenInt);
                   }
                    
                   return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK, NextSequenceToken = (tokenInt+1).ToString() });
               });

            var intervalProvider = CreateIntervalProvider();
            var target1 = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(intervalProvider)
            );
            var target2 = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(intervalProvider)
            );
            var target3 = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(intervalProvider)
            );

            // act
            await Task.WhenAll(new []
            {
                target1.WriteAsync(CreateEvents(groupName, streamName)),
                target2.WriteAsync(CreateEvents(groupName, streamName)),
                target3.WriteAsync(CreateEvents(groupName, streamName))
            });

            // assert
            var actual = tokens.OrderBy(i => i);
            var expected = Enumerable.Range(1, 3);
            Assert.True(expected.SequenceEqual(actual), $"Expected token sequence: {String.Join(",", expected)}, actual: {String.Join(",", actual)}");
        }

        [Fact]
        public async Task WriteAsync_Should_Reinit_Sequence_Token_If_All_Retries_Fail()
        {
            // arrange
            var clientMock = new Mock<IAmazonCloudWatchLogs>().InitGroup().InitCreateStream();
            int token = 1;
            string successfullToken = null;
            string guid = Guid();

            clientMock
                .Setup(m => m.DescribeLogStreamsAsync(It.IsAny<DescribeLogStreamsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<DescribeLogStreamsRequest, CancellationToken>((r, c) => Task.FromResult(
                    new DescribeLogStreamsResponse
                    {
                        HttpStatusCode = HttpStatusCode.OK,
                        LogStreams = new List<LogStream> { new LogStream { 
                            LogStreamName = r.LogStreamNamePrefix, UploadSequenceToken = token++.ToString() 
                        } }
                    }
                ));

            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
               {
                   if(r.SequenceToken == "1")
                        throw new InvalidSequenceTokenException("Invalid token.");

                   successfullToken = r.SequenceToken;
                   return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK });
               });

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await Assert.ThrowsAsync<InvalidSequenceTokenException>(() => target.WriteAsync(CreateEvents(guid, guid))); // first time should fail.
            await target.WriteAsync(CreateEvents(guid, guid)); // second time should be successful.

            // assert
            Assert.Equal("2", successfullToken);
        }

        [Fact]
        public async Task WriteAsync_Should_Receive_Group_And_Stream_Names()
        {
            // arange
            var clientMock = new Mock<IAmazonCloudWatchLogs>().Init();
            var data = CreateEvents(Guid(), Guid(), 1);
            string actual = null, expected = data.Select(d => $"{d.GroupName}:{d.StreamName}").Single();

            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
               {
                   actual = $"{r.LogGroupName}:{r.LogStreamName}";
                   return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK });
               });

            var target = new CloudWatchLogsClientWrapper(
                clientMock.Object,
                new CloudWatchLogsClientWrapperSettings(CreateIntervalProvider())
            );

            // act
            await target.WriteAsync(data);

            // assert
            Assert.Equal(expected, actual);
        }
    }
}
