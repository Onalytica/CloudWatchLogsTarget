using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NLog.Targets.CloudWatchLogs.Interval;

namespace NLog.Targets.CloudWatchLogs.Tests
{
    /// <summary>
    /// Summary description for ExponentialIntervalTests
    /// </summary>
    [TestClass]
    public class ExponentialIntervalTests
    {
        [TestMethod]
        public void GetInterval_Should_Return_8_Seconds()
        {
            // arrange
            var target = new ExponentialInterval<Seconds>(2);

            // act
            var actual = target.GetInterval(3);

            // assert
            Assert.AreEqual(TimeSpan.FromSeconds(8), actual);
        }

        [TestMethod]
        public void GetInterval_Should_Return_25_Minutes()
        {
            // arrange
            var target = new ExponentialInterval<Minutes>(5);

            // act
            var actual = target.GetInterval(2);

            // assert
            Assert.AreEqual(TimeSpan.FromMinutes(25), actual);
        }

        [TestMethod]
        public void GetInterval_Should_Return_1_Hour()
        {
            // arrange
            var target = new ExponentialInterval<Hours>(1);

            // act
            var actual = target.GetInterval(10);

            // assert
            Assert.AreEqual(TimeSpan.FromHours(1), actual);
        }
    }
}
