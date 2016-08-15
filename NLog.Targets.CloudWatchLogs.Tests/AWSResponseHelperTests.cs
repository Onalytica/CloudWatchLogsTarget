using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Amazon.Runtime;
using Ploeh.AutoFixture;

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
            var fixture = FixtureHelpers.Init();
            var response = fixture
                .Build<AmazonWebServiceResponse>()
                .With(s => s.HttpStatusCode, System.Net.HttpStatusCode.BadGateway)
                .Create();

            // act
            response.Verify();
        }

        [TestMethod]
        public void Verified_Should_Return_Same_Response_Object()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            var response = fixture
                .Build<AmazonWebServiceResponse>()
                .With(s => s.HttpStatusCode, System.Net.HttpStatusCode.OK)
                .Create();

            // act
            var result = response.Verify("some name");

            // assert
            Assert.AreSame(response, result);
        }

        [TestMethod]
        public void IsSuccessful_Should_Return_False()
        {
            // arrange
            var fixture = FixtureHelpers.Init();
            var response = fixture
                .Build<AmazonWebServiceResponse>()
                .With(s => s.HttpStatusCode, System.Net.HttpStatusCode.Ambiguous)
                .Create();

            // act
            var result = response.IsSuccessful();

            // assert
            Assert.IsFalse(result);
        }
    }
}
