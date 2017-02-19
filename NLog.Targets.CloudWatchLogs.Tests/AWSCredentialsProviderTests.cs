using Amazon.Runtime;
using NLog.Targets.CloudWatchLogs.Credentials;
using Moq;
using Xunit;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    public class AWSCredentialsProviderTests
    {
        [Fact]
        public void GetCredentials_Should_Return_Credentials_From_KeyId_And_SecretKey()
        {
            // arrange
            var fallbackProviderMock = new Mock<IAWSCredentialsProvider>();
            string accessKey = "some-access-key", secretKey = "some-secret-key";
            var target = new AWSCredentialsProvider(accessKey, secretKey, fallbackProviderMock.Object);

            fallbackProviderMock.Setup(m => m.GetCredentials()).Verifiable();

            // act
            var creds = target.GetCredentials();

            // assert
            Assert.Equal(accessKey, creds.GetCredentials().AccessKey);
            Assert.Equal(secretKey, creds.GetCredentials().SecretKey);
            fallbackProviderMock.Verify(m => m.GetCredentials(), Times.Never);
        }

        [Theory]
        [InlineData("some-access-key", "some-secret-key", false)]
        [InlineData("some-access-key", null, true)]
        [InlineData("some-access-key", "", true)]
        [InlineData("some-access-key", " ", true)]
        [InlineData(null, "some-secret-key", true)]
        [InlineData("", "some-secret-key", true)]
        [InlineData(" ", "some-secret-key", true)]
        [InlineData(null, null, true)]
        [InlineData("", "", true)]
        [InlineData(" ", " ", true)]
        public void GetCredentials_Should_Call_FallbackProvider_If_One_Of_Keys_Is_Not_Provided(string accessKey, string secretKey, bool fallback)
        {
            // arrange
            string fallbackAccessKey = "some-other-access-key", fallbackSecretKey = "some-other-secret-key";
            var fallbackProvider = Mock.Of<IAWSCredentialsProvider>(m =>
               m.GetCredentials() == new BasicAWSCredentials(fallbackAccessKey, fallbackSecretKey)
            );
            var target = new AWSCredentialsProvider(accessKey, secretKey, fallbackProvider);

            // act
            var creds = target.GetCredentials();

            // assert
            if (fallback)
            {
                Assert.Equal(fallbackAccessKey, creds.GetCredentials().AccessKey);
                Assert.Equal(fallbackSecretKey, creds.GetCredentials().SecretKey);
            }
            else
            {
                Assert.Equal(accessKey, creds.GetCredentials().AccessKey);
                Assert.Equal(secretKey, creds.GetCredentials().SecretKey);
            }
            Mock.Get(fallbackProvider).Verify(m => m.GetCredentials(), Times.Exactly(fallback ? 1 : 0));
        }
    }
}
