using Microsoft.VisualStudio.TestTools.UnitTesting;
using Amazon.Runtime;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    [TestClass]
    public class AWSResponseHelperTests
    {
        [TestMethod]
        [ExpectedException(typeof(AWSFailedRequestException))]
        public void Verify_Should_Raise_Exception()
        {
            // arrange
            var response = new AmazonWebServiceResponse { HttpStatusCode = System.Net.HttpStatusCode.BadGateway };

            // act
            response.Verify();
        }

        [TestMethod]
        public void Verified_Should_Return_Same_Response_Object()
        {
            // arrange
            var response = new AmazonWebServiceResponse { HttpStatusCode = System.Net.HttpStatusCode.OK };

            // act
            var result = response.Verify("some name");

            // assert
            Assert.AreSame(response, result);
        }

        [TestMethod]
        public void IsSuccessful_Should_Return_False()
        {
            // arrange
            var response = new AmazonWebServiceResponse { HttpStatusCode = System.Net.HttpStatusCode.Ambiguous };

            // act
            var result = response.IsSuccessful();

            // assert
            Assert.IsFalse(result);
        }
    }
}
