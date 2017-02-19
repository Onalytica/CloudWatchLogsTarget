using Amazon.Runtime;
using Xunit;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    public class AWSResponseHelperTests
    {
        [Fact]
        public void Verify_Should_Raise_Exception()
        {
            // arrange
            var response = new AmazonWebServiceResponse { HttpStatusCode = System.Net.HttpStatusCode.BadGateway };

            // act
            Assert.Throws<AWSFailedRequestException>(() => response.Verify());
        }

        [Fact]
        public void Verified_Should_Return_Same_Response_Object()
        {
            // arrange
            var response = new AmazonWebServiceResponse { HttpStatusCode = System.Net.HttpStatusCode.OK };

            // act
            var result = response.Verify("some name");

            // assert
            Assert.Same(response, result);
        }

        [Fact]
        public void IsSuccessful_Should_Return_False()
        {
            // arrange
            var response = new AmazonWebServiceResponse { HttpStatusCode = System.Net.HttpStatusCode.Ambiguous };

            // act
            var result = response.IsSuccessful();

            // assert
            Assert.False(result);
        }
    }
}
