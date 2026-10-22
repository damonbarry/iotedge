// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Agent.Core.Test
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Agent.Core.ConfigSources;
    using Microsoft.Azure.Devices.Edge.Agent.Core.Metrics;
    using Microsoft.Azure.Devices.Edge.Agent.Core.PlanRunner;
    using Microsoft.Azure.Devices.Edge.Agent.Core.Serde;
    using Microsoft.Azure.Devices.Edge.Storage;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Edged;
    using Microsoft.Azure.Devices.Edge.Util.Test.Common;
    using Moq;
    using Xunit;

    [Unit]
    public class AgentTests
    {
        const int CoolOffTimeInSeconds = 10;
        const int MaxRunCount = 20;

        public static IEnumerable<object[]> GetExceptionsToTest() => new List<object[]>
        {
            new object[]
            {
                new ConfigEmptyException("Empty config"),
                DeploymentStatusCode.ConfigEmptyError
            },
            new object[]
            {
                new InvalidSchemaVersionException("Bad schema"),
                DeploymentStatusCode.InvalidSchemaVersion
            },
            new object[]
            {
                new ConfigFormatException("Bad config"),
                DeploymentStatusCode.ConfigFormatError
            }
        };

        [Fact]
        public void AgentConstructorInvalidArgs()
        {
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironmentProvider = new Mock<IEnvironmentProvider>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var mockModuleLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            Assert.Throws<ArgumentNullException>(() => new Agent(null, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, null, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, null, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, null, mockReporter.Object, mockModuleLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, null, mockModuleLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, null, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, null, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, configStore, null, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, null, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, null, availabilityMetric, Option.None<OfflineCheckInfo>()));
            Assert.Throws<ArgumentNullException>(() => new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, null, Option.None<OfflineCheckInfo>()));
        }

        [Fact]
        public async void AgentCreateSuccessWhenDecryptFails()
        {
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironmentProvider = new Mock<IEnvironmentProvider>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var mockModuleLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = new Mock<IEntityStore<string, string>>();
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = new Mock<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();
            configStore.Setup(cs => cs.Get(It.IsAny<string>()))
                .ReturnsAsync(Option.Some("encrypted"));
            encryptionDecryptionProvider.Setup(ep => ep.DecryptAsync(It.IsAny<string>()))
                .ThrowsAsync(new WorkloadCommunicationException("failed", 404));

            Agent agent = await Agent.Create(mockConfigSource.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleLifecycleManager.Object, mockEnvironmentProvider.Object, configStore.Object, serde, encryptionDecryptionProvider.Object, availabilityMetric, Option.None<OfflineCheckInfo>());

            Assert.NotNull(agent);
            encryptionDecryptionProvider.Verify(ep => ep.DecryptAsync(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async void ReconcileAsyncOnEmptyPlan()
        {
            var token = default(CancellationToken);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockEnvironmentProvider = new Mock<IEnvironmentProvider>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var runtimeInfo = Mock.Of<IRuntimeInfo>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            var deploymentConfig = new DeploymentConfig(
                "1.0",
                runtimeInfo,
                new SystemModules(null, null),
                new Dictionary<string, IModule>
                {
                    { "mod1", new TestModule("mod1", "1.0", "docker", ModuleStatus.Running, new TestConfig("boo"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null) }
                },
                null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            ModuleSet desiredModuleSet = deploymentConfig.GetModuleSet();
            ModuleSet currentModuleSet = desiredModuleSet;

            mockEnvironmentProvider.Setup(m => m.Create(It.IsAny<DeploymentConfig>())).Returns(mockEnvironment.Object);
            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(currentModuleSet);
            mockEnvironment.Setup(env => env.GetRuntimeInfoAsync()).ReturnsAsync(runtimeInfo);
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(It.Is<ModuleSet>(ms => ms.Equals(desiredModuleSet)), currentModuleSet))
                .ReturnsAsync(ImmutableDictionary<string, IModuleIdentity>.Empty);
            mockPlanner.Setup(pl => pl.PlanAsync(It.Is<ModuleSet>(ms => ms.Equals(desiredModuleSet)), currentModuleSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty))
                .Returns(Task.FromResult(Plan.Empty));
            mockReporter.Setup(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), DeploymentStatus.Success))
                .Returns(Task.CompletedTask);

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());

            await agent.ReconcileAsync(token);

            mockEnvironment.Verify(env => env.GetModulesAsync(token), Times.Once);
            mockPlanner.Verify(pl => pl.PlanAsync(It.Is<ModuleSet>(ms => ms.Equals(desiredModuleSet)), currentModuleSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty), Times.Once);
            mockReporter.Verify(r => r.ReportAsync(token, currentModuleSet, runtimeInfo, deploymentConfigInfo.Version, DeploymentStatus.Success), Times.Once);
            mockPlanRunner.Verify(r => r.ExecuteAsync(1, Plan.Empty, token), Times.Never);
        }

        [Theory]
        [InlineData(false, "200 Connection Established", true)]
        [InlineData(true, "200 Connection Established", true)]
        [InlineData(false, "407 Proxy Authentication Required", false)]
        [InlineData(false, "502 Bad Gateway", false)]
        [InlineData(false, "302 Found", false)]
        public async Task ReconcileAsyncChecksTargetThroughProxy(bool authenticate, string status, bool available)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var proxy = new WebProxy(new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}"));
            if (authenticate)
            {
                proxy.Credentials = new NetworkCredential("test-user", "test-password");
            }

            var offlineCheckInfo = new OfflineCheckInfo("iot-hub.invalid", 443, Option.Some<IWebProxy>(proxy));
            var reporter = new Mock<IReporter>();
            Agent agent = CreateAgentForOfflineCheck(Option.Some(offlineCheckInfo), reporter);
            Task proxyTask = RespondToProxyConnectAsync(listener, authenticate, status, cancellation.Token);
            try
            {
                await agent.ReconcileAsync(cancellation.Token).WaitAsync(cancellation.Token);

                reporter.Verify(r => r.ReportAsync(It.IsAny<CancellationToken>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.IsAny<DeploymentStatus>()), Times.Exactly(available ? 1 : 0));
                await proxyTask;
            }
            finally
            {
                cancellation.Cancel();
                try
                {
                    await proxyTask;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
            }
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public async Task ReconcileAsyncChecksDirectRoute(bool available, bool bypassProxy)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            if (!available)
            {
                listener.Stop();
            }

            Option<IWebProxy> proxy = bypassProxy
                ? Option.Some<IWebProxy>(new WebProxy("http://proxy.invalid:3128") { BypassList = new[] { @"127\.0\.0\.1" } })
                : Option.None<IWebProxy>();
            var reporter = new Mock<IReporter>();
            Agent agent = CreateAgentForOfflineCheck(Option.Some(new OfflineCheckInfo("127.0.0.1", port, proxy)), reporter);

            await agent.ReconcileAsync(cancellation.Token).WaitAsync(cancellation.Token);

            reporter.Verify(r => r.ReportAsync(It.IsAny<CancellationToken>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.IsAny<DeploymentStatus>()), Times.Exactly(available ? 1 : 0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReconcileAsyncRecoversAfterStalledProxy(bool cancelCaller)
        {
            using var cancellation = new CancellationTokenSource();
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var proxy = new WebProxy($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            var reporter = new Mock<IReporter>();
            Agent agent = CreateAgentForOfflineCheck(Option.Some(new OfflineCheckInfo("iot-hub.invalid", 443, Option.Some<IWebProxy>(proxy))), reporter);

            Task reconcile = agent.ReconcileAsync(cancellation.Token);
            using var acceptTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using TcpClient stalledConnection = await listener.AcceptTcpClientAsync(acceptTimeout.Token);
            if (cancelCaller)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconcile.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            else
            {
                await reconcile.WaitAsync(TimeSpan.FromSeconds(15));
            }

            reporter.Verify(r => r.ReportAsync(It.IsAny<CancellationToken>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.IsAny<DeploymentStatus>()), Times.Never);

            using var recoveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Task.WhenAll(
                agent.ReconcileAsync(recoveryTimeout.Token),
                RespondToProxyConnectAsync(listener, false, "200 Connection Established", recoveryTimeout.Token)).WaitAsync(recoveryTimeout.Token);

            reporter.Verify(r => r.ReportAsync(It.IsAny<CancellationToken>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.IsAny<DeploymentStatus>()), Times.Once);
        }

        [Fact]
        public async void ReconcileAsyncAbortsWhenConfigSourceThrows()
        {
            // Arrange
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var token = default(CancellationToken);
            var currentSet = ModuleSet.Empty;
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync()).Throws<InvalidOperationException>();
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(currentSet);
            mockReporter.Setup(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.Is<DeploymentStatus>(s => s.Code == DeploymentStatusCode.Failed)))
                .Returns(Task.CompletedTask);

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());

            // Act
            // Assert
            await agent.ReconcileAsync(token);
            mockPlanner.Verify(p => p.PlanAsync(It.IsAny<ModuleSet>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<ImmutableDictionary<string, IModuleIdentity>>()), Times.Never);
            mockReporter.VerifyAll();
            mockPlanRunner.Verify(r => r.ExecuteAsync(1, It.IsAny<Plan>(), token), Times.Never);
        }

        [Theory]
        [MemberData(nameof(GetExceptionsToTest))]
        public async void ReconcileAsyncAbortsWhenConfigSourceReturnsKnownExceptions(
            Exception testException,
            DeploymentStatusCode statusCode)
        {
            // Arrange
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var token = default(CancellationToken);
            var currentSet = ModuleSet.Empty;
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            var deploymentConfigInfo = new DeploymentConfigInfo(10, testException);
            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(currentSet);
            mockReporter.Setup(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.Is<DeploymentStatus>(s => s.Code == statusCode)))
                .Returns(Task.CompletedTask);

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());

            // Act
            // Assert
            await agent.ReconcileAsync(token);
            mockPlanner.Verify(p => p.PlanAsync(It.IsAny<ModuleSet>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<ImmutableDictionary<string, IModuleIdentity>>()), Times.Never);
            mockReporter.VerifyAll();
            mockPlanRunner.Verify(r => r.ExecuteAsync(1, It.IsAny<Plan>(), token), Times.Never);
        }

        [Fact]
        public async void ReconcileAsyncAbortsWhenEnvironmentSourceThrows()
        {
            // Arrange
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var token = default(CancellationToken);
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            var deploymentConfig = new DeploymentConfig("1.0", Mock.Of<IRuntimeInfo>(), new SystemModules(null, null), new Dictionary<string, IModule>(), null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token)).Throws<InvalidOperationException>();
            mockReporter.Setup(r => r.ReportAsync(token, null, It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.Is<DeploymentStatus>(s => s.Code == DeploymentStatusCode.Failed)))
                .Returns(Task.CompletedTask);

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());

            // Act
            // Assert
            await agent.ReconcileAsync(token);
            mockPlanner.Verify(p => p.PlanAsync(It.IsAny<ModuleSet>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<ImmutableDictionary<string, IModuleIdentity>>()), Times.Never);
            mockReporter.VerifyAll();
            mockPlanRunner.Verify(r => r.ExecuteAsync(1, It.IsAny<Plan>(), token), Times.Never);
        }

        [Fact]
        public async void ReconcileAsyncAbortsWhenModuleIdentityLifecycleManagerThrows()
        {
            // Arrange
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var token = default(CancellationToken);
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            var deploymentConfig = new DeploymentConfig(
                "1.0",
                Mock.Of<IRuntimeInfo>(),
                new SystemModules(null, null),
                new Dictionary<string, IModule>
                {
                    { "mod1", new TestModule("mod1", "1.0", "docker", ModuleStatus.Running, new TestConfig("boo"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null) }
                },
                null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            ModuleSet desiredModuleSet = deploymentConfig.GetModuleSet();
            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(ModuleSet.Empty);
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(It.Is<ModuleSet>(ms => ms.Equals(desiredModuleSet)), ModuleSet.Empty))
                .Throws<InvalidOperationException>();
            mockReporter.Setup(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.Is<DeploymentStatus>(s => s.Code == DeploymentStatusCode.Failed)))
                .Returns(Task.CompletedTask);

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());

            // Act
            // Assert
            await agent.ReconcileAsync(token);
            mockPlanner.Verify(p => p.PlanAsync(It.IsAny<ModuleSet>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<ImmutableDictionary<string, IModuleIdentity>>()), Times.Never);
            mockReporter.VerifyAll();
            mockPlanRunner.Verify(r => r.ExecuteAsync(1, It.IsAny<Plan>(), token), Times.Never);
        }

        [Fact]
        public async void ReconcileAsyncReportsFailedWhenEncryptProviderThrows()
        {
            var token = default(CancellationToken);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockEnvironmentProvider = new Mock<IEnvironmentProvider>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var runtimeInfo = Mock.Of<IRuntimeInfo>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var encryptionDecryptionProvider = new Mock<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            var deploymentConfig = new DeploymentConfig(
                "1.0",
                runtimeInfo,
                new SystemModules(null, null),
                new Dictionary<string, IModule>
                {
                    { "mod1", new TestModule("mod1", "1.0", "docker", ModuleStatus.Running, new TestConfig("boo"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null) }
                },
                null);
            var desiredModule = new TestModule("desired", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var recordKeeper = Option.Some(new TestPlanRecorder());
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            ModuleSet desiredModuleSet = deploymentConfig.GetModuleSet();
            ModuleSet currentModuleSet = desiredModuleSet;

            var commandList = new List<ICommand>
            {
                new TestCommand(TestCommandType.TestCreate, desiredModule, recordKeeper)
            };
            var testPlan = new Plan(commandList);

            mockEnvironmentProvider.Setup(m => m.Create(It.IsAny<DeploymentConfig>())).Returns(mockEnvironment.Object);
            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(currentModuleSet);
            mockEnvironment.Setup(env => env.GetRuntimeInfoAsync()).ReturnsAsync(runtimeInfo);
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(It.Is<ModuleSet>(ms => ms.Equals(desiredModuleSet)), currentModuleSet))
                .ReturnsAsync(ImmutableDictionary<string, IModuleIdentity>.Empty);
            mockPlanner.Setup(pl => pl.PlanAsync(It.Is<ModuleSet>(ms => ms.Equals(desiredModuleSet)), currentModuleSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty))
                .ReturnsAsync(testPlan);
            encryptionDecryptionProvider.Setup(ep => ep.EncryptAsync(It.IsAny<string>()))
                .ThrowsAsync(new WorkloadCommunicationException("failed", 404));

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider.Object, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider.Object, availabilityMetric, Option.None<OfflineCheckInfo>());

            await agent.ReconcileAsync(token);

            // Assert
            mockPlanner.Verify(p => p.PlanAsync(It.IsAny<ModuleSet>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<ImmutableDictionary<string, IModuleIdentity>>()), Times.Once);
            mockReporter.Verify(r => r.ReportAsync(It.IsAny<CancellationToken>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), 0, new DeploymentStatus(DeploymentStatusCode.Failed, "failed")));
            mockPlanRunner.Verify(r => r.ExecuteAsync(0, It.IsAny<Plan>(), token), Times.Once);
            encryptionDecryptionProvider.Verify(ep => ep.EncryptAsync(It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async void ReconcileAsyncOnSetPlan()
        {
            var desiredModule = new TestModule("desired", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var currentModule = new TestModule("current", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var recordKeeper = Option.Some(new TestPlanRecorder());
            var moduleExecutionList = new List<TestRecordType>
            {
                new TestRecordType(TestCommandType.TestCreate, desiredModule),
                new TestRecordType(TestCommandType.TestRemove, currentModule)
            };
            var commandList = new List<ICommand>
            {
                new TestCommand(TestCommandType.TestCreate, desiredModule, recordKeeper),
                new TestCommand(TestCommandType.TestRemove, currentModule, recordKeeper)
            };
            var testPlan = new Plan(commandList);
            var token = default(CancellationToken);
            var runtimeInfo = Mock.Of<IRuntimeInfo>();
            var deploymentConfig = new DeploymentConfig("1.0", runtimeInfo, new SystemModules(null, null), new Dictionary<string, IModule> { ["desired"] = desiredModule }, null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            ModuleSet desiredSet = deploymentConfig.GetModuleSet();
            ModuleSet currentSet = ModuleSet.Create(currentModule);

            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var planRunner = new OrderedRetryPlanRunner(MaxRunCount, CoolOffTimeInSeconds, new SystemTime());
            var mockReporter = new Mock<IReporter>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(currentSet);
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(desiredSet, currentSet))
                .ReturnsAsync(ImmutableDictionary<string, IModuleIdentity>.Empty);
            mockPlanner.Setup(pl => pl.PlanAsync(It.IsAny<ModuleSet>(), currentSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty))
                .Returns(Task.FromResult(testPlan));
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(It.IsAny<ModuleSet>(), currentSet))
                .Returns(Task.FromResult((IImmutableDictionary<string, IModuleIdentity>)ImmutableDictionary<string, IModuleIdentity>.Empty));
            mockReporter.Setup(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), DeploymentStatus.Success))
                .Returns(Task.CompletedTask);

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, planRunner, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());

            await agent.ReconcileAsync(token);

            mockEnvironment.Verify(env => env.GetModulesAsync(token), Times.Exactly(1));
            mockPlanner.Verify(pl => pl.PlanAsync(It.IsAny<ModuleSet>(), currentSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty), Times.Once);
            mockReporter.VerifyAll();
            recordKeeper.ForEach(r => Assert.Equal(moduleExecutionList, r.ExecutionList));
        }

        [Fact]
        public async void ReconcileAsyncWithNoDeploymentChange()
        {
            var desiredModule = new TestModule("CustomModule", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var currentModule = new TestModule("CustomModule", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);

            var testPlan = new Plan(new List<ICommand>());
            var token = default(CancellationToken);
            var runtimeInfo = Mock.Of<IRuntimeInfo>();
            var deploymentConfig = new DeploymentConfig("1.0", runtimeInfo, new SystemModules(null, null), new Dictionary<string, IModule> { ["CustomModule"] = desiredModule }, null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            ModuleSet desiredSet = deploymentConfig.GetModuleSet();
            ModuleSet currentSet = ModuleSet.Create(currentModule);

            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var planRunner = new OrderedRetryPlanRunner(MaxRunCount, CoolOffTimeInSeconds, new SystemTime());
            var mockReporter = new Mock<IReporter>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(currentSet);
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(desiredSet, currentSet))
                .ReturnsAsync(ImmutableDictionary<string, IModuleIdentity>.Empty);
            mockPlanner.Setup(pl => pl.PlanAsync(It.IsAny<ModuleSet>(), currentSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty))
                .Returns(Task.FromResult(testPlan));
            mockReporter.Setup(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), DeploymentStatus.Success))
                .Returns(Task.CompletedTask);

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, planRunner, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());

            await agent.ReconcileAsync(token);

            mockEnvironment.Verify(env => env.GetModulesAsync(token), Times.Exactly(1));
            mockPlanner.Verify(pl => pl.PlanAsync(It.IsAny<ModuleSet>(), currentSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty), Times.Once);
            mockReporter.Verify(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), DeploymentStatus.Success), Times.Once);
        }

        [Fact]
        public async Task DesiredIsNotNullBecauseCurrentThrew()
        {
            // Arrange
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var runtimeInfo = new Mock<IRuntimeInfo>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var token = default(CancellationToken);
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();
            var deploymentConfigInfo = new DeploymentConfigInfo(1, new DeploymentConfig("1.0", runtimeInfo.Object, new SystemModules(null, null), ImmutableDictionary<string, IModule>.Empty, null));
            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(e => e.GetModulesAsync(token))
                .Throws<InvalidOperationException>();

            // Act
            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());
            await agent.ReconcileAsync(token);

            // Assert
            mockReporter.Verify(r => r.ReportAsync(token, null, It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.Is<DeploymentStatus>(s => s.Code == DeploymentStatusCode.Failed)));
            mockPlanRunner.Verify(r => r.ExecuteAsync(1, It.IsAny<Plan>(), token), Times.Never);
        }

        [Fact]
        public async Task CurrentIsNotNullBecauseDesiredThrew()
        {
            // Arrange
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var token = default(CancellationToken);
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .Throws<InvalidOperationException>();
            mockEnvironment.Setup(e => e.GetModulesAsync(token))
                .ReturnsAsync(ModuleSet.Empty);

            // Act
            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());
            await agent.ReconcileAsync(token);

            // Assert
            mockReporter.Verify(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.Is<DeploymentStatus>(s => s.Code == DeploymentStatusCode.Failed)));
            mockPlanRunner.Verify(r => r.ExecuteAsync(1, It.IsAny<Plan>(), token), Times.Never);
        }

        [Fact]
        public async void ReconcileAsyncExecuteAsyncIncompleteDefaulsUnknown()
        {
            var desiredModule = new TestModule("desired", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var currentModule = new TestModule("current", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var commandList = new List<ICommand>
            {
                new Mock<ICommand>().Object,
                new Mock<ICommand>().Object,
            };
            var testPlan = new Plan(commandList);
            var token = default(CancellationToken);
            var runtimeInfo = Mock.Of<IRuntimeInfo>();
            var deploymentConfig = new DeploymentConfig("1.0", runtimeInfo, new SystemModules(null, null), new Dictionary<string, IModule> { ["desired"] = desiredModule }, null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            ModuleSet desiredSet = deploymentConfig.GetModuleSet();
            ModuleSet currentSet = ModuleSet.Create(currentModule);

            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var deploymentMetrics = Mock.Of<IDeploymentMetrics>();

            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(currentSet);
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(desiredSet, currentSet))
                .ReturnsAsync(ImmutableDictionary<string, IModuleIdentity>.Empty);
            mockPlanner.Setup(pl => pl.PlanAsync(It.IsAny<ModuleSet>(), currentSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty))
                .Returns(Task.FromResult(testPlan));
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(It.IsAny<ModuleSet>(), currentSet))
                .Returns(Task.FromResult((IImmutableDictionary<string, IModuleIdentity>)ImmutableDictionary<string, IModuleIdentity>.Empty));
            mockReporter.Setup(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), DeploymentStatus.Unknown))
                .Returns(Task.CompletedTask);
            mockPlanRunner.SetupSequence(m => m.ExecuteAsync(It.IsAny<long>(), It.IsAny<Plan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, deploymentMetrics, Option.None<OfflineCheckInfo>());

            await agent.ReconcileAsync(token);

            mockEnvironment.Verify(env => env.GetModulesAsync(token), Times.Once());
            mockPlanner.Verify(pl => pl.PlanAsync(It.IsAny<ModuleSet>(), currentSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty), Times.Once());
            mockPlanRunner.VerifyAll();
            mockReporter.VerifyAll();
        }

        [Fact]
        public async void ReconcileAsyncExecuteAsyncIncompleteReportsLastState()
        {
            var desiredModule = new TestModule("desired", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var currentModule = new TestModule("current", "v1", "test", ModuleStatus.Running, new TestConfig("image"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var commandList = new List<ICommand>
            {
                new Mock<ICommand>().Object,
                new Mock<ICommand>().Object,
            };
            var testPlan = new Plan(commandList);
            var token = default(CancellationToken);
            var runtimeInfo = Mock.Of<IRuntimeInfo>();
            var deploymentConfig = new DeploymentConfig("1.0", runtimeInfo, new SystemModules(null, null), new Dictionary<string, IModule> { ["desired"] = desiredModule }, null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            ModuleSet desiredSet = deploymentConfig.GetModuleSet();
            ModuleSet currentSet = ModuleSet.Create(currentModule);
            var statuses = new List<DeploymentStatusCode>();

            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var deploymentMetrics = Mock.Of<IDeploymentMetrics>();

            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(env => env.GetModulesAsync(token))
                .ReturnsAsync(currentSet);
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(desiredSet, currentSet))
                .ReturnsAsync(ImmutableDictionary<string, IModuleIdentity>.Empty);
            mockPlanner.Setup(pl => pl.PlanAsync(It.IsAny<ModuleSet>(), currentSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty))
                .Returns(Task.FromResult(testPlan));
            mockModuleIdentityLifecycleManager.Setup(m => m.GetModuleIdentitiesAsync(It.IsAny<ModuleSet>(), currentSet))
                .Returns(Task.FromResult((IImmutableDictionary<string, IModuleIdentity>)ImmutableDictionary<string, IModuleIdentity>.Empty));
            mockReporter.Setup(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.IsAny<DeploymentStatus>()))
                .Callback((CancellationToken _t, ModuleSet _m, IRuntimeInfo _r, long _v, DeploymentStatus d) => statuses.Add(d.Code))
                .Returns(Task.CompletedTask);
            // First call represents a command failure, 2nd call represents an execution backoff.
            mockPlanRunner.SetupSequence(m => m.ExecuteAsync(It.IsAny<long>(), It.IsAny<Plan>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("generic exception"))
                .ReturnsAsync(false);
            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, deploymentMetrics, Option.None<OfflineCheckInfo>());

            await agent.ReconcileAsync(token);
            await agent.ReconcileAsync(token);

            mockEnvironment.Verify(env => env.GetModulesAsync(token), Times.Exactly(2));
            mockPlanner.Verify(pl => pl.PlanAsync(It.IsAny<ModuleSet>(), currentSet, runtimeInfo, ImmutableDictionary<string, IModuleIdentity>.Empty), Times.Exactly(2));
            mockPlanRunner.VerifyAll();
            mockReporter.Verify(r => r.ReportAsync(token, It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.IsAny<DeploymentStatus>()), Times.Exactly(2));
            Assert.Equal(DeploymentStatusCode.Failed, statuses[0]);
            Assert.Equal(DeploymentStatusCode.Failed, statuses[1]);
        }

        [Fact]
        public async Task ReportShutdownAsyncConfigTest()
        {
            // Arrange
            var mockConfigSource = new Mock<IConfigSource>();
            var mockEnvironment = new Mock<IEnvironment>();
            var mockPlanner = new Mock<IPlanner>();
            var mockPlanRunner = new Mock<IPlanRunner>();
            var mockReporter = new Mock<IReporter>();
            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();
            var deploymentConfig = new DeploymentConfig(
                "1.0",
                Mock.Of<IRuntimeInfo>(),
                new SystemModules(null, null),
                new Dictionary<string, IModule>
                {
                    { "mod1", new TestModule("mod1", "1.0", "docker", ModuleStatus.Running, new TestConfig("boo"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null) }
                },
                null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            var token = default(CancellationToken);

            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);
            mockEnvironment.Setup(e => e.GetModulesAsync(token))
                .ReturnsAsync(ModuleSet.Empty);

            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());
            await agent.ReportShutdownAsync(token);

            // Assert
            mockReporter.Verify(r => r.ReportShutdown(It.IsAny<DeploymentStatus>(), token));
        }

        [Fact]
        public async Task HandleShutdownTest()
        {
            // Arrange
            var mockConfigSource = new Mock<IConfigSource>();

            IModule mod1 = new TestModule("mod1", "1.0", "docker", ModuleStatus.Running, new TestConfig("boo"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            IModule mod2 = new TestModule("mod2", "1.0", "docker", ModuleStatus.Running, new TestConfig("boo"), RestartPolicy.OnUnhealthy, ImagePullPolicy.OnCreate, Constants.DefaultStartupOrder, new ConfigurationInfo("1"), null);
            var modules = new Dictionary<string, IModule>
            {
                [mod1.Name] = mod1,
                [mod2.Name] = mod2
            };

            var mockEnvironment = new Mock<IEnvironment>();
            mockEnvironment.Setup(m => m.GetModulesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModuleSet(modules));

            var mockPlanner = new Mock<IPlanner>();
            mockPlanner.Setup(p => p.CreateShutdownPlanAsync(It.IsAny<ModuleSet>()))
                .ReturnsAsync(new Plan(new ICommand[0]));

            var mockPlanRunner = new Mock<IPlanRunner>();
            mockPlanRunner.Setup(m => m.ExecuteAsync(It.IsAny<long>(), It.IsAny<Plan>(), It.IsAny<CancellationToken>()))
                .Returns(
                    async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        return true;
                    });

            var mockReporter = new Mock<IReporter>();
            mockReporter.Setup(
                    m => m.ReportAsync(
                        It.IsAny<CancellationToken>(),
                        It.IsAny<ModuleSet>(),
                        It.IsAny<IRuntimeInfo>(),
                        It.IsAny<long>(),
                        It.IsAny<DeploymentStatus>()))
                .Returns(Task.Delay(TimeSpan.FromSeconds(5)));

            var mockModuleIdentityLifecycleManager = new Mock<IModuleIdentityLifecycleManager>();
            var configStore = Mock.Of<IEntityStore<string, string>>();
            var mockEnvironmentProvider = Mock.Of<IEnvironmentProvider>(m => m.Create(It.IsAny<DeploymentConfig>()) == mockEnvironment.Object);
            var serde = Mock.Of<ISerde<DeploymentConfigInfo>>();
            var encryptionDecryptionProvider = Mock.Of<IEncryptionProvider>();
            var availabilityMetric = Mock.Of<IDeploymentMetrics>();

            var deploymentConfig = new DeploymentConfig("1.0", Mock.Of<IRuntimeInfo>(), new SystemModules(null, null), modules, null);
            var deploymentConfigInfo = new DeploymentConfigInfo(0, deploymentConfig);
            var token = default(CancellationToken);

            mockConfigSource.Setup(cs => cs.GetDeploymentConfigInfoAsync())
                .ReturnsAsync(deploymentConfigInfo);

            // Act
            var agent = new Agent(mockConfigSource.Object, mockEnvironmentProvider, mockPlanner.Object, mockPlanRunner.Object, mockReporter.Object, mockModuleIdentityLifecycleManager.Object, configStore, DeploymentConfigInfo.Empty, serde, encryptionDecryptionProvider, availabilityMetric, Option.None<OfflineCheckInfo>());

            var shutdownTask = agent.HandleShutdown(token);
            var waitTask = Task.Delay(TimeSpan.FromSeconds(6));
            var completedTask = await Task.WhenAny(shutdownTask, waitTask);

            // Assert
            Assert.Equal(completedTask, shutdownTask);
            mockReporter.Verify(r => r.ReportShutdown(It.IsAny<DeploymentStatus>(), token), Times.Once);
            mockPlanRunner.Verify(r => r.ExecuteAsync(It.IsAny<long>(), It.IsAny<Plan>(), It.IsAny<CancellationToken>()), Times.Once);
            mockPlanner.Verify(r => r.CreateShutdownPlanAsync(It.IsAny<ModuleSet>()), Times.Once);
        }

        static Agent CreateAgentForOfflineCheck(Option<OfflineCheckInfo> offlineCheckInfo, Mock<IReporter> reporter)
        {
            var configSource = new Mock<IConfigSource>();
            configSource.Setup(c => c.GetDeploymentConfigInfoAsync()).ReturnsAsync(DeploymentConfigInfo.Empty);
            var environment = new Mock<IEnvironment>();
            environment.Setup(e => e.GetModulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ModuleSet.Empty);
            var environmentProvider = Mock.Of<IEnvironmentProvider>(p => p.Create(It.IsAny<DeploymentConfig>()) == environment.Object);
            reporter.Setup(r => r.ReportAsync(It.IsAny<CancellationToken>(), It.IsAny<ModuleSet>(), It.IsAny<IRuntimeInfo>(), It.IsAny<long>(), It.IsAny<DeploymentStatus>())).Returns(Task.CompletedTask);

            return new Agent(
                configSource.Object,
                environmentProvider,
                Mock.Of<IPlanner>(),
                Mock.Of<IPlanRunner>(),
                reporter.Object,
                Mock.Of<IModuleIdentityLifecycleManager>(),
                Mock.Of<IEntityStore<string, string>>(),
                DeploymentConfigInfo.Empty,
                Mock.Of<ISerde<DeploymentConfigInfo>>(),
                Mock.Of<IEncryptionProvider>(),
                Mock.Of<IDeploymentMetrics>(),
                offlineCheckInfo);
        }

        static async Task RespondToProxyConnectAsync(TcpListener listener, bool authenticate, string status, CancellationToken token)
        {
            for (int attempt = 0; attempt < (authenticate ? 2 : 1); attempt++)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(token);
                using NetworkStream stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                Assert.Equal("CONNECT iot-hub.invalid:443 HTTP/1.1", await reader.ReadLineAsync(token));
                string authorization = null;
                string header;
                while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(token)))
                {
                    if (header.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                    {
                        authorization = header.Substring("Proxy-Authorization:".Length).Trim();
                    }
                }

                string response;
                if (authenticate && attempt == 0)
                {
                    Assert.Null(authorization);
                    response = "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }
                else
                {
                    if (authenticate)
                    {
                        Assert.Equal($"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes("test-user:test-password"))}", authorization);
                    }

                    response = $"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                }

                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), token);
            }
        }
    }
}
