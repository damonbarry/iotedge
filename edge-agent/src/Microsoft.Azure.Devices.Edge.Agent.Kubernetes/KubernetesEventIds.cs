// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Agent.Kubernetes
{
    public struct KubernetesEventIds
    {
        public const int KubernetesPlanner = EventIdStart + 100;
        public const int KubernetesCommand = EventIdStart + 200;
        public const int KubernetesOperator = EventIdStart + 300;
        public const int KubernetesReporter = EventIdStart + 400;
        public const int KubernetesCrdWatcher = EventIdStart + 500;
        public const int KubernetesRuntimeInfoProvider = EventIdStart + 600;
        const int EventIdStart = 200000;
    }
}
