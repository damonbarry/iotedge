// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.CloudProxy
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Concurrency;
    using Microsoft.Azure.Devices.Shared;

    class ModuleClientWrapper : IClient
    {
        static readonly ActivitySource activitySource = new ActivitySource("Microsoft.Azure.Devices.Edge.Hub", "db20200908.1");
        readonly ModuleClient underlyingModuleClient;
        readonly AtomicBoolean isActive;
        readonly string traceEndpoint;

        public ModuleClientWrapper(ModuleClient moduleClient, string traceEndpoint)
        {
            this.underlyingModuleClient = moduleClient;
            this.isActive = new AtomicBoolean(true);
            this.traceEndpoint = traceEndpoint;
        }

        public bool IsActive => this.isActive;

        public Task AbandonAsync(string messageId) => this.underlyingModuleClient.AbandonAsync(messageId);

        public Task CloseAsync()
        {
            if (this.isActive.GetAndSet(false))
            {
                this.underlyingModuleClient?.Dispose();
            }

            return Task.CompletedTask;
        }

        public Task RejectAsync(string messageId) => throw new InvalidOperationException("Reject is not supported for modules.");

        public Task<Message> ReceiveAsync(TimeSpan receiveMessageTimeout) => throw new InvalidOperationException("C2D messages are not supported for modules.");

        public Task CompleteAsync(string messageId) => this.underlyingModuleClient.CompleteAsync(messageId);

        public void Dispose()
        {
            this.isActive.Set(false);
            this.underlyingModuleClient?.Dispose();
        }

        public Task<Twin> GetTwinAsync() => this.underlyingModuleClient.GetTwinAsync();

        public async Task OpenAsync()
        {
            try
            {
                await this.underlyingModuleClient.OpenAsync().TimeoutAfter(TimeSpan.FromMinutes(2));
            }
            catch (Exception)
            {
                this.isActive.Set(false);
                this.underlyingModuleClient?.Dispose();
                throw;
            }
        }

        public Task SendEventAsync(Message message)
        {
            using (Activity activity = activitySource.StartActivity(
                "EdgeHubD2CMessageDeliveredUpstream", ActivityKind.Producer, message.TraceParent))
            {
                activity.AddTag("deviceId", Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID") ?? string.Empty);
                activity.AddTag("moduleId", Environment.GetEnvironmentVariable("IOTEDGE_MODULEID") ?? string.Empty);
                message.TraceParent = activity.Id;
                message.TraceState = $"timestamp={(int)DateTimeOffset.Now.ToUnixTimeSeconds()}";
                return this.underlyingModuleClient.SendEventAsync(message);
            }
        }

        public Task SendEventBatchAsync(IEnumerable<Message> messages)
        {
            Message[] inputMessages = messages.ToArray();
            if (inputMessages.Length == 1)
            {
                return this.SendEventAsync(inputMessages[0]);
            }

            var links = new List<ActivityLink>();
            foreach (Message message in inputMessages)
            {
                if (!string.IsNullOrEmpty(message.TraceParent))
                {
                    using (Activity activity = activitySource.StartActivity(
                        "EdgeHubD2CMessageSendBatch", ActivityKind.Consumer, message.TraceParent))
                    {
                        links.Add(new ActivityLink(activity.Context));
                        message.TraceParent = activity.Id;
                        message.TraceState = $"timestamp={(int)DateTimeOffset.Now.ToUnixTimeSeconds()}";
                    }
                }
            }

            using (Activity activity = activitySource.StartActivity(
                "EdgeHubD2CMessageDeliveredUpstream", ActivityKind.Producer, default(ActivityContext), null, links))
            {
                activity.AddTag("deviceId", Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID") ?? string.Empty);
                activity.AddTag("moduleId", Environment.GetEnvironmentVariable("IOTEDGE_MODULEID") ?? string.Empty);
                return this.underlyingModuleClient.SendEventBatchAsync(messages);
            }
        }

        public void SetConnectionStatusChangedHandler(ConnectionStatusChangesHandler handler) => this.underlyingModuleClient.SetConnectionStatusChangesHandler(handler);

        public Task SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateCallback onDesiredPropertyUpdates, object userContext)
            => this.underlyingModuleClient.SetDesiredPropertyUpdateCallbackAsync(onDesiredPropertyUpdates, userContext);

        public Task SetMethodDefaultHandlerAsync(MethodCallback methodHandler, object userContext)
            => this.underlyingModuleClient.SetMethodDefaultHandlerAsync(methodHandler, userContext);

        public void SetOperationTimeoutInMilliseconds(uint operationTimeoutMilliseconds) => this.underlyingModuleClient.OperationTimeoutInMilliseconds = operationTimeoutMilliseconds;

        public void SetProductInfo(string productInfo) => this.underlyingModuleClient.ProductInfo = productInfo;

        public Task UpdateReportedPropertiesAsync(TwinCollection reportedProperties) => this.underlyingModuleClient.UpdateReportedPropertiesAsync(reportedProperties);
    }
}
