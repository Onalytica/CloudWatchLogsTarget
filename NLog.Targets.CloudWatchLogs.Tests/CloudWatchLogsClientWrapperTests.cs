using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ploeh.AutoFixture;
using Moq;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Collections.Concurrent;
using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    [TestClass]
    public class CloudWatchLogsClientWrapperTests
    {
        private Mock<IAmazonCloudWatchLogs> SetupInitializers(IFixture fixture)
        {
            var clientMock = fixture.Freeze<Mock<IAmazonCloudWatchLogs>>();
            clientMock
                .Setup(m => m.DescribeLogGroups(It.IsAny<DescribeLogGroupsRequest>()))
                .Returns(fixture.Build<DescribeLogGroupsResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create());
            clientMock
                .Setup(m => m.CreateLogGroup(It.IsAny<CreateLogGroupRequest>()))
                .Returns(fixture.Build<CreateLogGroupResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create());
            clientMock
                .Setup(m => m.DescribeLogStreams(It.IsAny<DescribeLogStreamsRequest>()))
                .Returns(fixture.Build<DescribeLogStreamsResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create());
            clientMock
                .Setup(m => m.CreateLogStream(It.IsAny<CreateLogStreamRequest>()))
                .Returns(fixture.Build<CreateLogStreamResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create());
            return clientMock;
        }

        [TestMethod]
        public async Task WriteAsync_Should_Retry_Failed_Request_3_Times()
        {
            // arrange
            int actualRetries = 0, expectedRetries = 3;
            var fixture = FixtureHelpers.Init();
            var clientMock = SetupInitializers(fixture);

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    actualRetries++;
                    return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = actualRetries <= (expectedRetries - 1) ? HttpStatusCode.BadGateway : HttpStatusCode.OK });
                });

            var target = fixture.Create<CloudWatchLogsClientWrapper>();

            // act
            await target.WriteAsync(fixture.CreateMany<InputLogEvent>());

            // assert
            Assert.AreEqual(expectedRetries, actualRetries);
        }

        [TestMethod]
        [ExpectedException(typeof(AWSFailedRequestException))]
        public async Task WriteAsync_Should_Throw_AWSFailedRequestException()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            var clientMock = SetupInitializers(fixture);

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.BadGateway });
                });

            var target = fixture.Create<CloudWatchLogsClientWrapper>();

            // act
            await target.WriteAsync(fixture.CreateMany<InputLogEvent>());
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidSequenceTokenException))]
        public async Task WriteAsync_Should_Throw_InvalidSequenceTokenException()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            var clientMock = SetupInitializers(fixture);

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    throw new InvalidSequenceTokenException("invalid token");
                });

            var target = fixture.Create<CloudWatchLogsClientWrapper>();

            // act
            await target.WriteAsync(fixture.CreateMany<InputLogEvent>());
        }

        [TestMethod]
        public async Task WriteAsync_Should_Queue_Log_Requests_And_Preserve_Their_Order()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            var clientMock = SetupInitializers(fixture);
            var expectedSequence = Enumerable.Range(0, 20);
            List<int> actualSequence = new List<int>(), retried = new List<int>();

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    return Task
                        .Delay(fixture.Create<int>() % 500) // Simulates delay on the API side
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

            var target = fixture.Create<CloudWatchLogsClientWrapper>();

            // act
            await Task.WhenAll(expectedSequence
                .Select(i => target.WriteAsync(new[] { new InputLogEvent { Message = i.ToString() } }))
                .ToArray());

            // assert
            Assert.IsTrue(expectedSequence.SequenceEqual(actualSequence), "Message sequence should be preserved.");
            Assert.IsTrue(expectedSequence.Where(i => i % 5 == 0).SequenceEqual(retried), "Expected and actual retries should match.");
        }

        [TestMethod]
        public async Task Init_Should_Retry_Failed_Requests()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            int actualRetries = 0, expectedRetries = 3;
            var clientMock = SetupInitializers(fixture);
            clientMock
                .Setup(m => m.DescribeLogGroups(It.IsAny<DescribeLogGroupsRequest>()))
                .Returns<DescribeLogGroupsRequest>(r =>
                {
                    actualRetries++;
                    return new DescribeLogGroupsResponse { HttpStatusCode = actualRetries <= (expectedRetries - 1) ? HttpStatusCode.BadGateway : HttpStatusCode.OK };
                });
            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns(Task.FromResult(fixture.Build<PutLogEventsResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create()));

            var target = fixture.Create<CloudWatchLogsClientWrapper>();

            // act
            await target.WriteAsync(fixture.CreateMany<InputLogEvent>());

            // assert
            Assert.AreEqual(expectedRetries, actualRetries);
        }

        [TestMethod]
        public async Task Init_Should_Retry_Requests_Resulting_In_Amazon_Exceptions()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            int actualRetries = 0, expectedRetries = 3;
            var clientMock = SetupInitializers(fixture);
            clientMock
                .Setup(m => m.DescribeLogGroups(It.IsAny<DescribeLogGroupsRequest>()))
                .Returns(fixture.Build<DescribeLogGroupsResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create());
            clientMock
                .Setup(m => m.CreateLogGroup(It.IsAny<CreateLogGroupRequest>()))
                .Returns<CreateLogGroupRequest>(r =>
                {
                    actualRetries++;
                    if (actualRetries <= (expectedRetries - 1))
                        throw new OperationAbortedException("something happened.");

                    return new CreateLogGroupResponse { HttpStatusCode = HttpStatusCode.OK };
                });
            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns(Task.FromResult(fixture.Build<PutLogEventsResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create()));

            var target = fixture.Create<CloudWatchLogsClientWrapper>();

            // act
            await target.WriteAsync(fixture.CreateMany<InputLogEvent>());

            // assert
            Assert.AreEqual(expectedRetries, actualRetries);
        }

        [TestMethod]
        public async Task WriteAsync_Should_Order_Log_Events_Chronologically_Before_Sending()
        {
            // arange
            var fixture = FixtureHelpers.Init();
            var clientMock = SetupInitializers(fixture);
            var events = new[]
            {
                new InputLogEvent { Timestamp = DateTime.UtcNow },
                new InputLogEvent { Timestamp = DateTime.UtcNow.AddSeconds(-1) }
            };
            List<InputLogEvent> actual = null, expected = events.OrderBy(e => e.Timestamp).ToList();

            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns<PutLogEventsRequest, CancellationToken>((r,c) =>
               {
                   actual = r.LogEvents;
                   return Task.FromResult(fixture.Build<PutLogEventsResponse>().With(rsp => rsp.HttpStatusCode, HttpStatusCode.OK).Create());
               });

            var target = fixture.Create<CloudWatchLogsClientWrapper>();

            // act
            await target.WriteAsync(events);

            // assert
            Assert.IsTrue(expected.SequenceEqual(actual), "Actual events sequence should be ordered chronologically.");
        }

        [TestMethod]
        public async Task WriteAsync_Should_Handle_Concurent_Requests_From_Multiple_Target_Insances()
        {
            // arange
            var group = "same-group";
            var stream = "same-stream";
            var fixture = FixtureHelpers.Init();
            var clientMock = SetupInitializers(fixture);
            var tokens = new List<int>();

            clientMock
                .Setup(m => m.DescribeLogStreams(It.IsAny<DescribeLogStreamsRequest>()))
                .Returns(fixture.Build<DescribeLogStreamsResponse>()
                    .With(r => r.HttpStatusCode, HttpStatusCode.OK)
                    .With(r => r.LogStreams, new List<LogStream> { new LogStream { LogStreamName = stream, UploadSequenceToken = "1" } })
                    .Create());

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

            var intervalProvider = fixture.Create<IIntervalProvider>();
            var target1 = new CloudWatchLogsClientWrapper(clientMock.Object, group, stream, intervalProvider);
            var target2 = new CloudWatchLogsClientWrapper(clientMock.Object, group, stream, intervalProvider);
            var target3 = new CloudWatchLogsClientWrapper(clientMock.Object, group, stream, intervalProvider);

            // act
            await Task.WhenAll(new []
            {
                target1.WriteAsync(fixture.CreateMany<InputLogEvent>()),
                target2.WriteAsync(fixture.CreateMany<InputLogEvent>()),
                target3.WriteAsync(fixture.CreateMany<InputLogEvent>())
            });

            // assert
            var actual = tokens.OrderBy(i => i);
            var expected = Enumerable.Range(1, 3);
            Assert.IsTrue(expected.SequenceEqual(actual), $"Expected token sequence: {String.Join(",", expected)}, actual: {String.Join(",", actual)}");
        }

        [TestMethod]
        public async Task WriteAsync_Should_Reinit_Sequence_Token_If_All_Retries_Fail()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            var clientMock = SetupInitializers(fixture);
            int token = 1;
            string successfullToken = null;

            clientMock
                .Setup(m => m.DescribeLogStreams(It.IsAny<DescribeLogStreamsRequest>()))
                .Returns<DescribeLogStreamsRequest>(r => new DescribeLogStreamsResponse
                    {
                        HttpStatusCode = HttpStatusCode.OK,
                        LogStreams = new List<LogStream> { new LogStream { LogStreamName = r.LogStreamNamePrefix, UploadSequenceToken = token++.ToString() } }
                    }
                );

            clientMock
               .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
               .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
               {
                   if(r.SequenceToken == "1")
                        throw new InvalidSequenceTokenException("Invalid token.");

                   successfullToken = r.SequenceToken;
                   return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK });
               });

            var target = fixture.Create<CloudWatchLogsClientWrapper>();

            // act
            try
            {
                // first time should fail.
                await target.WriteAsync(fixture.CreateMany<InputLogEvent>());
                Assert.Fail("First WriteAsync call should have failed.");
            }
            catch (InvalidSequenceTokenException)
            {
            }

            // second time should be successful
            await target.WriteAsync(fixture.CreateMany<InputLogEvent>());

            // assert
            Assert.AreEqual("2", successfullToken);
        }
    }
}
