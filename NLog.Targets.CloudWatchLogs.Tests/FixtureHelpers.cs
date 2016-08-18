using System;
using Ploeh.AutoFixture;
using Ploeh.AutoFixture.AutoMoq;
using Moq;
using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    public static class FixtureHelpers
    {
        public static IFixture Init()
        {
            var fixture = new Fixture().Customize(new AutoMoqCustomization());

            // Sets zero sleep duration to make tests of retry logic run faster.
            fixture.Inject(Mock.Of<IIntervalProvider>(m => m.GetInterval(It.IsAny<int>()) == TimeSpan.Zero));

            return fixture;
        }
    }
}
