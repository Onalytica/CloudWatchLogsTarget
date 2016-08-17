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

namespace NLog.Targets.CloudWatchLogs.Tests
{
    [TestClass]
    public class CloudWatchLogsClientWrapperTests
    {
        private Mock<IAmazonCloudWatchLogs> SetupInitializers(IFixture fixture)
        {
            var clientMock = fixture.Freeze<Mock<IAmazonCloudWatchLogs>>();
            clientMock
                .Setup(m => m.DescribeLogGroupsAsync(It.IsAny<DescribeLogGroupsRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(fixture.Build<DescribeLogGroupsResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create()));
            clientMock
                .Setup(m => m.CreateLogGroupAsync(It.IsAny<CreateLogGroupRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(fixture.Build<CreateLogGroupResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create()));
            clientMock
                .Setup(m => m.DescribeLogStreamsAsync(It.IsAny<DescribeLogStreamsRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(fixture.Build<DescribeLogStreamsResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create()));
            clientMock
                .Setup(m => m.CreateLogStreamAsync(It.IsAny<CreateLogStreamRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(fixture.Build<CreateLogStreamResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create()));
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
        public async Task WriteAsync_Should_Throw_Exception_After_All_Unsuccessfull_Retries()
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
            Assert.IsTrue(expectedSequence.SequenceEqual(actualSequence));
            Assert.IsTrue(expectedSequence.Where(i => i % 5 == 0).SequenceEqual(retried));
        }

        [TestMethod]
        public async Task Init_Should_Retry_Failed_Requests()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            int actualRetries = 0, expectedRetries = 3;
            var clientMock = fixture.Freeze<Mock<IAmazonCloudWatchLogs>>();
            clientMock
                .Setup(m => m.DescribeLogGroupsAsync(It.IsAny<DescribeLogGroupsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<DescribeLogGroupsRequest, CancellationToken>((r,c) =>
                {
                    actualRetries++;
                    return Task.FromResult(new DescribeLogGroupsResponse { HttpStatusCode = actualRetries <= (expectedRetries - 1) ? HttpStatusCode.BadGateway : HttpStatusCode.OK });
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
            var clientMock = fixture.Freeze<Mock<IAmazonCloudWatchLogs>>();
            clientMock
                .Setup(m => m.DescribeLogGroupsAsync(It.IsAny<DescribeLogGroupsRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(fixture.Build<DescribeLogGroupsResponse>().With(r => r.HttpStatusCode, HttpStatusCode.OK).Create()));
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
    }
}
