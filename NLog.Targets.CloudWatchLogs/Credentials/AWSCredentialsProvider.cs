using System;
using Amazon.Runtime;

namespace NLog.Targets.CloudWatchLogs.Credentials
{
    public class AWSCredentialsProvider : IAWSCredentialsProvider
    {
        private readonly string _awsAccessKeyId;
        private readonly string _awsSecretKey;
        private readonly IAWSCredentialsProvider _fallbackProvider;

        public AWSCredentialsProvider(string awsAccessKeyId, string awsSecretKey, IAWSCredentialsProvider fallbackProvider)
        {
            _awsAccessKeyId = awsAccessKeyId;
            _awsSecretKey = awsSecretKey;
            _fallbackProvider = fallbackProvider;
        }

        public AWSCredentials GetCredentials()
        {
            if (!String.IsNullOrWhiteSpace(_awsAccessKeyId) && !String.IsNullOrWhiteSpace(_awsSecretKey))
                return new BasicAWSCredentials(_awsAccessKeyId, _awsSecretKey);
            else
                return _fallbackProvider.GetCredentials();
        }

        public static AWSCredentials GetCredentialsOrDefault(string awsAccessKeyId, string awsSecretKey) =>
            new AWSCredentialsProvider(awsAccessKeyId, awsSecretKey, new DefaultAWSCredentialsProvider()).GetCredentials();
    }
}
