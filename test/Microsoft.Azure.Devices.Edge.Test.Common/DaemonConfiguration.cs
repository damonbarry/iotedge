// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Azure.Devices.Edge.Test.Common
{
    using System;
    using System.IO;
    using Microsoft.Azure.Devices.Edge.Test.Common.Certs;

    public class DaemonConfiguration
    {
        readonly string configYamlFile;
        readonly YamlDocument config;

        public DaemonConfiguration(string configYamlFile)
        {
            this.configYamlFile = configYamlFile;
            string contents = File.ReadAllText(this.configYamlFile);
            this.config = new YamlDocument(contents);
        }

        public void AddHttpsProxy(Uri proxy)
        {
            this.config.ReplaceOrAdd("agent.env.https_proxy", proxy.ToString());
            // The config.yaml file is configured during test suite
            // initialization, before we know which protocol a given test
            // will use. Always use AmqpWs, and when each test deploys a
            // configuration, it can use whatever it wants.
            this.config.ReplaceOrAdd("agent.env.UpstreamProtocol", "AmqpWs");
        }

        public void SetDeviceConnectionString(string value)
        {
            this.config.ReplaceOrAdd("provisioning.device_connection_string", value);
        }

        public void SetDeviceHostname(string value)
        {
            this.config.ReplaceOrAdd("hostname", value);
        }

        public void SetCertificates(EdgeCertificates certs)
        {
            this.config.ReplaceOrAdd("certificates.device_ca_cert", certs.CertificatePath);
            this.config.ReplaceOrAdd("certificates.device_ca_pk", certs.KeyPath);
            this.config.ReplaceOrAdd("certificates.trusted_ca_certs", certs.TrustedCertificatesPath);
        }

        public void RemoveCertificates()
        {
            this.config.RemoveIfExists("certificates");
        }

        public void Update()
        {
            var attr = File.GetAttributes(this.configYamlFile);
            File.SetAttributes(this.configYamlFile, attr & ~FileAttributes.ReadOnly);

            File.WriteAllText(this.configYamlFile, this.config.ToString());

            if (attr != 0)
            {
                File.SetAttributes(this.configYamlFile, attr);
            }
        }
    }
}
