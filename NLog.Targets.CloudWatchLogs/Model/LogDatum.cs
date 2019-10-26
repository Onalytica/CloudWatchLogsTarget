using Amazon.CloudWatchLogs.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NLog.Targets.CloudWatchLogs.Model
{
    public class LogDatum
    {
        public LogDatum(string message)
        {
            Message = message;
        }

        public LogDatum()
        {
        }

        public string Message { get; set; }
        public string StreamName { get; set; }
        public string GroupName { get; set; }
        public DateTime? Timestamp { get; set; }
        public string TokenKey => $"{GroupName}:{StreamName}";

        public InputLogEvent ToInputLogEvent()
        {
            return new InputLogEvent() { Message = Message, Timestamp = Timestamp ?? DateTime.Now };
        }
    }
}
