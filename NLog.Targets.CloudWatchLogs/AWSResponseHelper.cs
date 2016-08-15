using System;
using Amazon.Runtime;

namespace NLog.Targets.CloudWatchLogs
{
    /// <summary>
    /// Helper class for AmazonWebServiceResponse status verification.
    /// </summary>
    public static class AWSResponseHelper
    {
        /// <summary>
        /// Generic method to verify AmazonWebServiceResponse.
        /// </summary>
        /// <typeparam name="T">Reponse type derived from AmazonWebServiceResponse.</typeparam>
        /// <param name="response">The reponse object.</param>
        /// <returns>The response object.</returns>
        public static T Verify<T>(this T response) where T : AmazonWebServiceResponse
        {
            return Verify(response, String.Empty);
        }

        /// <summary>
        /// Generic method to verify AmazonWebServiceResponse with the name of the request and HttpStatusCode range to check.
        /// </summary>
        /// <typeparam name="T">Reponse type derived from AmazonWebServiceResponse.</typeparam>
        /// <param name="response">The reponse object.</param>
        /// <param name="requestName">The name of the request.</param>
        /// <returns>The response object.</returns>
        public static T Verify<T>(this T response, string requestName) where T : AmazonWebServiceResponse
        {
            if (!response.IsSuccessful())
                throw new AWSFailedRequestException(response, requestName);

            return response;
        }

        public static bool IsSuccessful<T>(this T response) where T : AmazonWebServiceResponse
        {
            return (int)response.HttpStatusCode >= 200 && (int)response.HttpStatusCode < 300;
        }
    }
}
