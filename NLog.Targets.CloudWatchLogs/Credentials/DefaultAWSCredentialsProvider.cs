using Amazon.Runtime;

namespace NLog.Targets.CloudWatchLogs.Credentials
{
    public class DefaultAWSCredentialsProvider : IAWSCredentialsProvider
    {
        public AWSCredentials GetCredentials() => FallbackCredentialsFactory.GetCredentials();
    }
}
