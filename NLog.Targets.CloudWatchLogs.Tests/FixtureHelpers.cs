using Ploeh.AutoFixture;
using Ploeh.AutoFixture.AutoMoq;
using System.Net;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.Runtime;
using Moq;
using System.Threading;
using System.Threading.Tasks;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    public static class FixtureHelpers
    {
        public static IFixture Init()
        {
            return new Fixture()
                .Customize(new AutoMoqCustomization());
        }
    }
}
