using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    [TestClass]
    public class ValueToTimeSpanInterpreterTests
    {
        [TestMethod]
        public void GetTimeSpan_Should_Return_3_Seconds()
        {
            // arrange
            var target = new Seconds();

            // act
            var actual = target.GetTimeSpan(3);

            // assert
            Assert.AreEqual(TimeSpan.FromSeconds(3), actual);
        }

        [TestMethod]
        public void GetTimeSpan_Should_Return_10_Minutes()
        {
            // arrange
            var target = new Minutes();

            // act
            var actual = target.GetTimeSpan(10);

            // assert
            Assert.AreEqual(TimeSpan.FromMinutes(10), actual);
        }

        [TestMethod]
        public void GetTimeSpan_Should_Return_1_Hour()
        {
            // arrange
            var target = new Hours();

            // act
            var actual = target.GetTimeSpan(1);

            // assert
            Assert.AreEqual(TimeSpan.FromHours(1), actual);
        }
    }
}
