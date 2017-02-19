To setup CloudWatchLogs target for NLog, your `nlog.config` has to look something like the following:

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

The example above will use the configured AWS credentials or the instance role. Consult the ["Configuring AWS Credentials" guide](http://docs.aws.amazon.com/sdk-for-net/v3/developer-guide/net-dg-config-creds.html) for more info. If you want to explicitly set credentials for the CloudWatchLogs target to use, specify them in the config:

```
<target name="cwl" 
		xsi:type="CloudWatchLogs" 
		AWSAccessKeyId="aws-access-key-id"
		AWSSecretKey="aws-secret-key"
		AWSRegion="aws-region"
		LogGroupName="log-group"
		LogStreamName="log-stream"
		layout="${longdate} [${threadid}] ${level} ${logger} [${ndc}] - ${message}"/>
```

The provided AWS user has to have the following permissions:

```
logs:CreateLogGroup
logs:CreateLogStream
logs:DescribeLogGroups
logs:DescribeLogStreams
logs:PutLogEvents
```