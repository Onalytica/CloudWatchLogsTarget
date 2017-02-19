using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;

namespace NLog.Targets.CloudWatchLogs.TestApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var logger = LogManager.GetCurrentClassLogger();

            logger.Trace("This is a trace message");
            logger.Debug("This is a debug message");
            logger.Info("This is a info message");
            logger.Warn("This is a warning message");
            logger.Error("This is a error message");
        }
    }
}
