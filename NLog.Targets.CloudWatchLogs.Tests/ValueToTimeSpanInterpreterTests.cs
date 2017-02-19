using System;
using NLog.Targets.CloudWatchLogs.Interval;
using Xunit;

namespace NLog.Targets.CloudWatchLogs.Tests
{
     public class ValueToTimeSpanInterpreterTests
    {
        [Fact]
        public void GetTimeSpan_Should_Return_3_Seconds()
        {
            // arrange
            var target = new Seconds();

            // act
            var actual = target.GetTimeSpan(3);

            // assert
            Assert.Equal(TimeSpan.FromSeconds(3), actual);
        }

        [Fact]
        public void GetTimeSpan_Should_Return_10_Minutes()
        {
            // arrange
            var target = new Minutes();

            // act
            var actual = target.GetTimeSpan(10);

            // assert
            Assert.Equal(TimeSpan.FromMinutes(10), actual);
        }

        [Fact]
        public void GetTimeSpan_Should_Return_1_Hour()
        {
            // arrange
            var target = new Hours();

            // act
            var actual = target.GetTimeSpan(1);

            // assert
            Assert.Equal(TimeSpan.FromHours(1), actual);
        }
    }
}
