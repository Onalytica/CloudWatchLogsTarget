using NLog.Targets.CloudWatchLogs.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;


namespace NLog.Targets.CloudWatchLogs.Tests
{
    public class CloudWatchLogsTargetTests
    {
        private class CrackedTarget: CloudWatchLogsTarget
        {
            public new LogDatum CreateDatum(LogEventInfo logInfo) => base.CreateDatum(logInfo);            
        }

        private static readonly LogEventInfo _logEventInfo = new LogEventInfo();

        [Fact]
        public void Target_With_No_Funcs_Should_Have_Default_Names()
        {
            var target = new CrackedTarget();
            string expectedGroupName = "unspecified", expectedStreamName = "unspecified";

            var datum = target.CreateDatum(_logEventInfo);

            Assert.Equal(datum.GroupName, expectedGroupName);
            Assert.Equal(datum.StreamName, expectedStreamName);
        }

        [Fact]
        public void Group_Name_Func_Should_Override_Default()
        {
            _logEventInfo.Message = Guid.NewGuid().ToString();
            var target = new CrackedTarget()
            {
                LogGroupNameFactory = m => m
            };

            var datum = target.CreateDatum(_logEventInfo);

            Assert.Equal(datum.GroupName, datum.Message);
        }

        [Fact]
        public void Stream_Name_Func_Should_Override_Default()
        {
            _logEventInfo.Message = Guid.NewGuid().ToString();
            var target = new CrackedTarget()
            {
                LogStreamNameFactory = m => m
            };

            var datum = target.CreateDatum(_logEventInfo);

            Assert.Equal(datum.StreamName, datum.Message);
        }
    }
}
