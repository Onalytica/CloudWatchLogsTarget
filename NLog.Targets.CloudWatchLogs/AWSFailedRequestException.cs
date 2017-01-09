using System;
using Amazon.Runtime;

namespace NLog.Targets.CloudWatchLogs
{
    /// <summary>
    /// Represents failed AWS API request exception.
    /// </summary>
    public class AWSFailedRequestException : Exception
    {
        public AWSFailedRequestException(AmazonWebServiceResponse response, string requestName)
            : base($"Failed AWS {requestName} API request - {response.HttpStatusCode}")
        {
        }
    }
}
