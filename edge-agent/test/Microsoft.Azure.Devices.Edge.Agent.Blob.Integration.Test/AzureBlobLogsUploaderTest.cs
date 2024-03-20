// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Agent.Blob.Integration.Test;

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Azure.Devices.Edge.Agent.Core.Logs;
using Microsoft.Azure.Devices.Edge.Agent.IoTHub.Blob;
using Microsoft.Azure.Devices.Edge.Util.Test.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

public class Startup
{
    public void ConfigureHost(IHostBuilder hostBuilder)
    {
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        hostBuilder.ConfigureHostConfiguration(builder => builder.AddConfiguration(config));
    }
}

[Integration]
public class AzureBlobLogsUploaderTest
{
    const string BlobNameRegexPattern = @"(?<iothub>.*)/(?<deviceid>.*)/(?<id>.*)-(?<timestamp>\d{4}-\d{2}-\d{2}--\d{2}-\d{2}-\d{2}).(?<extension>.{3,7})";

    readonly IConfiguration config;

    public AzureBlobLogsUploaderTest(IConfiguration config)
    {
        this.config = config;
    }

    [Fact]
    public async void UploadTest()
    {
        // Arrange
        string? iotHub = this.config.GetValue<string>("TEST_IOT_HUB");
        string? deviceId = this.config.GetValue<string>("TEST_DEVICE_ID");
        string? sasUri = this.config.GetValue<string>("TEST_SAS_URI");
        string id = "UploadTest";
        var regex = new Regex(BlobNameRegexPattern);
        byte[] payload = Encoding.UTF8.GetBytes("Test payload string");

        // Act
        var azureBlobLogsUploader = new AzureBlobRequestsUploader(iotHub, deviceId);
        await azureBlobLogsUploader.UploadLogs(sasUri, id, payload, LogsContentEncoding.Gzip, LogsContentType.Json);

        // Assert
    }

    [Fact]
    public async Task GetUploaderCallbackTest()
    {
        // Arrange
        string? iotHub = this.config.GetValue<string>("TEST_IOT_HUB");
        string? deviceId = this.config.GetValue<string>("TEST_DEVICE_ID");
        string? sasUri = this.config.GetValue<string>("TEST_SAS_URI");
        string id = "GetUploaderCallbackTest";
        var regex = new Regex(BlobNameRegexPattern);
        byte[] payload1 = Encoding.UTF8.GetBytes("Test payload string");
        byte[] payload2 = Encoding.UTF8.GetBytes("Second interesting payload");

        // Act
        var azureBlobLogsUploader = new AzureBlobRequestsUploader(iotHub, deviceId);
        Func<ArraySegment<byte>, Task> callback = await azureBlobLogsUploader.GetLogsUploaderCallback(sasUri, id, LogsContentEncoding.Gzip, LogsContentType.Json);

        Assert.NotNull(callback);
        await callback.Invoke(new ArraySegment<byte>(payload1));
        await callback.Invoke(new ArraySegment<byte>(payload2));

        // Assert
    }
}
