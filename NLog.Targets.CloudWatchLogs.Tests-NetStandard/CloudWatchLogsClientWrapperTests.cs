using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    [TestClass]
    public class CloudWatchLogsClientWrapperTests
    {
        private const string _logGroup = "some-log-group", _logStream = "some-log-stream";

        private Mock<IAmazonCloudWatchLogs> CreateClientMock()
        {
            var clientMock = new Mock<IAmazonCloudWatchLogs>();
            clientMock
                .Setup(m => m.DescribeLogGroupsAsync(It.Is<DescribeLogGroupsRequest>(v => v.LogGroupNamePrefix == _logGroup), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new DescribeLogGroupsResponse { HttpStatusCode = HttpStatusCode.OK }));
            clientMock
                .Setup(m => m.CreateLogGroupAsync(It.Is<CreateLogGroupRequest>( v => v.LogGroupName == _logGroup), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult( new CreateLogGroupResponse { HttpStatusCode = HttpStatusCode.OK }));
            clientMock
                .Setup(m => m.DescribeLogStreamsAsync(It.Is<DescribeLogStreamsRequest>(v => v.LogGroupName == _logGroup && v.LogStreamNamePrefix == _logStream), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new DescribeLogStreamsResponse { HttpStatusCode = HttpStatusCode.OK }));
            clientMock
                .Setup(m => m.CreateLogStreamAsync(It.Is<CreateLogStreamRequest>(v => v.LogGroupName == _logGroup && v.LogStreamName == _logStream), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new CreateLogStreamResponse { HttpStatusCode = HttpStatusCode.OK }));
            return clientMock;
        }

        private IIntervalProvider CreateIntervalProvider() =>
            Mock.Of<IIntervalProvider>(m => m.GetInterval(It.IsAny<int>()) == TimeSpan.Zero);

        private IEnumerable<InputLogEvent> CreateEvents(int count = 3)
        {
            return Enumerable.Range(1, count)
                .OrderByDescending(i => i)
                .Select(i => new InputLogEvent
                {
                    Timestamp = DateTime.Now.AddSeconds(-i),
                    Message = i.ToString()
                });
        }


        [TestMethod]
        public async Task WriteAsync_Should_Retry_Failed_Request_3_Times()
        {
            // arrange
            int actualRetries = 0, expectedRetries = 3;
            var clientMock = CreateClientMock();
            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    actualRetries++;
                    return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = actualRetries <= (expectedRetries - 1) ? HttpStatusCode.BadGateway : HttpStatusCode.OK });
                });

            var target = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, CreateIntervalProvider());

            // act
            await target.WriteAsync(CreateEvents());
            
            // assert
            Assert.AreEqual(expectedRetries, actualRetries);
        }

        [TestMethod]
        [ExpectedException(typeof(AWSFailedRequestException))]
        public async Task WriteAsync_Should_Throw_AWSFailedRequestException()
        {
            // arrange
            var clientMock = CreateClientMock();

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.BadGateway });
                });

            var target = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, CreateIntervalProvider());

            // act
            await target.WriteAsync(CreateEvents());
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidSequenceTokenException))]
        public async Task WriteAsync_Should_Throw_InvalidSequenceTokenException()
        {
            // arrange
            var clientMock = CreateClientMock();

            clientMock
                .Setup(m => m.PutLogEventsAsync(It.IsAny<PutLogEventsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<PutLogEventsRequest, CancellationToken>((r, c) =>
                {
                    throw new InvalidSequenceTokenException("invalid token");
                });

            var target = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, CreateIntervalProvider());

            // act
            await target.WriteAsync(CreateEvents());
        }

        [TestMethod]
        public async Task WriteAsync_Should_Queue_Log_Requests_And_Preserve_Their_Order()
        {
            // arrange
            var rand = new Random();
            var clientMock = CreateClientMock();
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

            var target = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, CreateIntervalProvider());

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
            int actualRetries = 0, expectedRetries = 3;
            string logGroup = "some-other-log-group";
            var clientMock = CreateClientMock();
            clientMock
                .Setup(m => m.DescribeLogGroupsAsync(It.Is<DescribeLogGroupsRequest>(v => v.LogGroupNamePrefix == logGroup), It.IsAny<CancellationToken>()))
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

            var target = new CloudWatchLogsClientWrapper(clientMock.Object, logGroup, _logStream, CreateIntervalProvider());

            // act
            await target.WriteAsync(CreateEvents());

            // assert
            Assert.AreEqual(expectedRetries, actualRetries);
        }

        [TestMethod]
        public async Task Init_Should_Retry_Requests_Resulting_In_Amazon_Exceptions()
        {
            // arrange
            int actualRetries = 0, expectedRetries = 3;
            string logGroup = "some-other-log-group";
            var clientMock = CreateClientMock();
            clientMock
                .Setup(m => m.DescribeLogGroupsAsync(It.Is<DescribeLogGroupsRequest>(v => v.LogGroupNamePrefix == logGroup), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new DescribeLogGroupsResponse { HttpStatusCode = HttpStatusCode.OK }));
            clientMock
                .Setup(m => m.CreateLogGroupAsync(It.Is<CreateLogGroupRequest>(v => v.LogGroupName == logGroup), It.IsAny<CancellationToken>()))
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

            var target = new CloudWatchLogsClientWrapper(clientMock.Object, logGroup, _logStream, CreateIntervalProvider());

            // act
            await target.WriteAsync(CreateEvents());

            // assert
            Assert.AreEqual(expectedRetries, actualRetries);
        }

        [TestMethod]
        public async Task WriteAsync_Should_Order_Log_Events_Chronologically_Before_Sending()
        {
            // arange
            var clientMock = CreateClientMock();
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
                   return Task.FromResult(new PutLogEventsResponse { HttpStatusCode = HttpStatusCode.OK });
               });

            var target = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, CreateIntervalProvider());

            // act
            await target.WriteAsync(events);

            // assert
            Assert.IsTrue(expected.SequenceEqual(actual), "Actual events sequence should be ordered chronologically.");
        }

        [TestMethod]
        public async Task WriteAsync_Should_Handle_Concurent_Requests_From_Multiple_Target_Insances()
        {
            // arange
            var clientMock = CreateClientMock();
            var tokens = new List<int>();

            clientMock
                .Setup(m => m.DescribeLogStreamsAsync(It.IsAny<DescribeLogStreamsRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new DescribeLogStreamsResponse
                {
                    HttpStatusCode = HttpStatusCode.OK,
                    LogStreams = new List<LogStream> { new LogStream { LogStreamName = _logStream, UploadSequenceToken = "1" } }
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
            var target1 = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, intervalProvider);
            var target2 = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, intervalProvider);
            var target3 = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, intervalProvider);

            // act
            await Task.WhenAll(new []
            {
                target1.WriteAsync(CreateEvents()),
                target2.WriteAsync(CreateEvents()),
                target3.WriteAsync(CreateEvents())
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
            var clientMock = CreateClientMock();
            int token = 1;
            string successfullToken = null;

            clientMock
                .Setup(m => m.DescribeLogStreamsAsync(It.IsAny<DescribeLogStreamsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<DescribeLogStreamsRequest, CancellationToken>((r, c) => Task.FromResult(
                    new DescribeLogStreamsResponse
                    {
                        HttpStatusCode = HttpStatusCode.OK,
                        LogStreams = new List<LogStream> { new LogStream { LogStreamName = r.LogStreamNamePrefix, UploadSequenceToken = token++.ToString() } }
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

            var target = new CloudWatchLogsClientWrapper(clientMock.Object, _logGroup, _logStream, CreateIntervalProvider());

            // act
            try
            {
                // first time should fail.
                await target.WriteAsync(CreateEvents());
                Assert.Fail("First WriteAsync call should have failed.");
            }
            catch (InvalidSequenceTokenException)
            {
            }

            // second time should be successful
            await target.WriteAsync(CreateEvents());

            // assert
            Assert.AreEqual("2", successfullToken);
        }
    }
}
