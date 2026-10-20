// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Hub.Mqtt.Test
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using DotNetty.Buffers;
    using DotNetty.Transport.Channels;
    using Microsoft.Azure.Devices.Edge.Hub.Core;
    using Microsoft.Azure.Devices.Edge.Hub.Core.Identity;
    using Microsoft.Azure.Devices.Edge.Util.Test.Common;
    using Microsoft.Azure.Devices.ProtocolGateway.Identity;
    using Microsoft.Azure.Devices.ProtocolGateway.Messaging;
    using Microsoft.Extensions.Configuration;
    using Moq;
    using Xunit;

    [Integration]
    public class MqttProtocolHeadTest
    {
        [Theory]
        [InlineData(SslProtocols.Tls12, false)]
        [InlineData(SslProtocols.Tls12, true)]
        [InlineData(SslProtocols.Tls13, false)]
        [InlineData(SslProtocols.Tls13, true)]
        public async Task ConnectOverTlsReceivesConnAckAsync(SslProtocols protocol, bool fragmentConnect)
        {
            using var certificate = CertificateHelper.GenerateSelfSignedCert("localhost");
            var settings = new MqttSettingsProvider(
                new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string>
                    {
                        ["ThreadCount"] = "1",
                        ["EventLoopCount"] = "1"
                    }).Build());
            var authenticator = new Mock<IAuthenticator>();
            authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<IClientCredentials>())).ReturnsAsync(true);
            var connectionProvider = new Mock<IMqttConnectionProvider>();
            connectionProvider.Setup(p => p.Connect(It.IsAny<IDeviceIdentity>()))
                .ReturnsAsync((IDeviceIdentity identity) => new SingleClientMessagingBridge(identity, Mock.Of<IMessagingServiceClient>()));
            using var head = new MqttProtocolHead(
                settings,
                certificate,
                connectionProvider.Object,
                authenticator.Object,
                new MqttUsernameParser(),
                new ClientCredentialsFactory(new IdentityProvider("localhost")),
                new SessionStatePersistenceProvider(Mock.Of<IEdgeHub>()),
                Mock.Of<IWebSocketListenerRegistry>(),
                UnpooledByteBufferAllocator.Default,
                Mock.Of<IMetadataStore>(),
                false,
                protocol);
            IChannel server = null;

            try
            {
                server = await head.SetupServerBootstrap().BindAsync(IPAddress.Loopback, 0);
                var endpoint = (IPEndPoint)server.LocalAddress;
                await Task.WhenAll(Enumerable.Range(0, 8).Select(i => ConnectAsync(endpoint, protocol, fragmentConnect, $"module{i}")));
                authenticator.Verify(a => a.AuthenticateAsync(It.IsAny<IClientCredentials>()), Times.Exactly(8));
                connectionProvider.Verify(p => p.Connect(It.IsAny<IDeviceIdentity>()), Times.Exactly(8));
            }
            finally
            {
                await (server?.CloseAsync() ?? Task.CompletedTask);
                await head.CloseAsync(CancellationToken.None);
            }
        }

        static async Task ConnectAsync(IPEndPoint endpoint, SslProtocols protocol, bool fragmentConnect, string moduleId)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port, timeout.Token);
            using var stream = new SslStream(client.GetStream(), false, (sender, certificate, chain, errors) => true);
            await stream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = "localhost",
                    EnabledSslProtocols = protocol
                },
                timeout.Token);

            var packet = new List<byte> { 0, 4, (byte)'M', (byte)'Q', (byte)'T', (byte)'T', 4, 0xc2, 0, 60 };
            AddString(packet, $"device/{moduleId}");
            AddString(packet, $"localhost/device/{moduleId}/?api-version=2018-06-30");
            AddString(packet, "token");
            packet.InsertRange(0, new byte[] { 0x10, checked((byte)packet.Count) });
            byte[] connect = packet.ToArray();
            if (fragmentConnect)
            {
                await stream.WriteAsync(connect.AsMemory(0, 1), timeout.Token);
                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
                await stream.WriteAsync(connect.AsMemory(1), timeout.Token);
            }
            else
            {
                await stream.WriteAsync(connect, timeout.Token);
            }

            byte[] connAck = new byte[4];
            await stream.ReadExactlyAsync(connAck, timeout.Token);
            Assert.Equal(new byte[] { 0x20, 2, 0, 0 }, connAck);
            await stream.WriteAsync(new byte[] { 0xe0, 0 }, timeout.Token);
        }

        static void AddString(List<byte> packet, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            packet.Add(checked((byte)(bytes.Length >> 8)));
            packet.Add(checked((byte)(bytes.Length & 0xff)));
            packet.AddRange(bytes);
        }
    }
}
