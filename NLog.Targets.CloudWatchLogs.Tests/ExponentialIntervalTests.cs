using System;
using NLog.Targets.CloudWatchLogs.Interval;
using Xunit;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    /// <summary>
    /// Summary description for ExponentialIntervalTests
    /// </summary>
    public class ExponentialIntervalTests
    {
        [Fact]
        public void GetInterval_Should_Return_8_Seconds()
        {
            // arrange
            var target = new ExponentialInterval<Seconds>(2);

            // act
            var actual = target.GetInterval(3);

            // assert
            Assert.Equal(TimeSpan.FromSeconds(8), actual);
        }

        [Fact]
        public void GetInterval_Should_Return_25_Minutes()
        {
            // arrange
            var target = new ExponentialInterval<Minutes>(5);

            // act
            var actual = target.GetInterval(2);

            // assert
            Assert.Equal(TimeSpan.FromMinutes(25), actual);
        }

        [Fact]
        public void GetInterval_Should_Return_1_Hour()
        {
            // arrange
            var target = new ExponentialInterval<Hours>(1);

            // act
            var actual = target.GetInterval(10);

            // assert
            Assert.Equal(TimeSpan.FromHours(1), actual);
        }
    }
}
