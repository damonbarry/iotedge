// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Agent.Core
{
    using System.Net;
    using Microsoft.Azure.Devices.Edge.Util;

    public class OfflineCheckInfo
    {
        public string Address { get; }
        public int Port { get; }
        public Option<IWebProxy> Proxy { get; }

        public OfflineCheckInfo(string address, int port)
            : this(address, port, Option.None<IWebProxy>())
        {
        }

        public OfflineCheckInfo(string address, int port, Option<IWebProxy> proxy)
        {
            this.Address = address;
            this.Port = port;
            this.Proxy = proxy;
        }
    }
}
