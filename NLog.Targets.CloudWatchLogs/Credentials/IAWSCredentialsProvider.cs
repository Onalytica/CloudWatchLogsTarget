using Amazon.Runtime;

namespace NLog.Targets.CloudWatchLogs.Credentials
{
    public interface IAWSCredentialsProvider
    {
        AWSCredentials GetCredentials();
    }
}
