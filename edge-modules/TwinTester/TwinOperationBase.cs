// Copyright (c) Microsoft. All rights reserved.
namespace TwinTester
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.ModuleUtil;
    using Microsoft.Extensions.Logging;

    abstract class TwinOperationBase
    {
        public abstract ILogger Logger { get; }

        public abstract Task UpdateAsync();

        public abstract Task ValidateAsync();

        protected bool ExceedFailureThreshold(TwinState twinState, DateTime twinUpdateTime)
        {
            DateTime comparisonPoint = twinUpdateTime > twinState.LastTimeOffline ? twinUpdateTime : twinState.LastTimeOffline;
            return DateTime.UtcNow - comparisonPoint > Settings.Current.TwinUpdateFailureThreshold;
        }

        protected async Task CallAnalyzerToReportStatusAsync(AnalyzerClient analyzerClient, string moduleId, string status)
        {
            try
            {
                await analyzerClient.AddTwinStatusAsync(new ResponseStatus { ModuleId = moduleId, StatusCode = status, EnqueuedDateTime = DateTime.UtcNow });
            }
            catch (Exception e)
            {
                this.Logger.LogError($"Failed call to report status to analyzer: {e}");
            }
        }
    }
}
