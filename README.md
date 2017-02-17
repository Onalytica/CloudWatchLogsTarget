﻿To setup CloudWatchLogs target for NLog, your `nlog.config` has to look something like the following:

```
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"> 
  <targets>
    <target name="bw"
            xsi:type="BufferingWrapper"
            slidingTimeout="true"
            bufferSize="100"
            flushTimeout="60000">    
      <target name="cwl" 
              xsi:type="CloudWatchLogs" 
              AWSAccessKeyId="aws-access-key-id"
              AWSSecretKey="aws-secret-key"
              AWSRegion="aws-region"
              LogGroupName="log-group"
              LogStreamName="log-stream"
              layout="${longdate} [${threadid}] ${level} ${logger} [${ndc}] - ${message}"/>
    </target>
  </targets> 
  <rules> 
    <logger name="*" minLevel="Info" appendTo="bw"/> 
  </rules> 
</nlog>
```

If `log-group` or `log-stream` do not exist, they will be created automatically within the desired region `aws-region`.

The provided AWS user has to have the following permissions:

```
logs:CreateLogGroup
logs:CreateLogStream
logs:DescribeLogGroups
logs:DescribeLogStreams
logs:PutLogEvents
```