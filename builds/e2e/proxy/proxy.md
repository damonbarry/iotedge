This file documents how to add agent VMs to an existing proxy environment for end-to-end tests.

These steps assume that the environment already includes:
- A proxy server VM - full network connectivity, runs an HTTP proxy server (squid).
- A Key Vault that contains the private keys used to SSH into any existing VMs.
- The virtual network and network security group to which the new agent VMs will belong.
These steps will add to the environment:
- One or more proxy client VMs (aka "runners") - no internet-bound network connectivity except through the proxy server.

After installing the Azure CLI, enter the following commands to deploy and configure the VMs:

```sh
cd builds/e2e/proxy/

# ----------
# Parameters

# Name of Azure subscription
subscription_name='<>'

# Name of the resource group
resource_group_name='<>'

# Prefix used when creating Azure resources. If not given, defaults to 'e2e-<13 char hash>-'.
resource_prefix='<>'

# Add, e.g., 2 more runners, prx-runner3-vm and prx-runner4-vm
runner_start=3
runner_count=2

# -------
# Execute

# Log in to Azure subscription
az login
az account set -s "$subscription_name"

# Deploy the VMs
az deployment group create --resource-group $resource_group_name --name 'add-runners' --template-file ./proxy-deployment-template.json --parameters "$(
    jq -n \
        --arg resource_prefix $resource_prefix \
        --argjson runner_start $runner_start \
        --argjson runner_count $runner_count \
        '{
            "resource_prefix": { "value": $resource_prefix },
            "runner_start": { "value": $runner_start },
            "runner_count": { "value": $runner_count },
            "create_runner_public_ip": { "value": true }
        }'
)"
```

Once the deployment has completed, SSH into each runner VM to install and configure the Azure Pipelines agent. To SSH into the runner VMs, you must first download their private keys from Key Vault. Find the name of the key vault from your deployment, then list the secret URLs for the private keys:

```sh
az keyvault secret list --vault-name '<>' -o tsv --query "[].id|[?contains(@, 'runner')]"
```

With a secret URL and an IP address, you can SSH into a runner VM like this:

```sh
az keyvault secret show --id '<>' -o tsv --query value > ~/.ssh/id_rsa.runner
chmod 600 ~/.ssh/id_rsa.runner
ssh -i ~/.ssh/id_rsa.runner azureuser@<ip addr>
```

To install and configure Azure Pipelines agent, see [Self-hosted Linux Agents](https://docs.microsoft.com/en-us/azure/devops/pipelines/agents/v2-linux?view=azure-devops) and [Run a self-hosted agent behind a web proxy](https://docs.microsoft.com/en-us/azure/devops/pipelines/agents/proxy?view=azure-devops&tabs=unix).

> Note that the proxy URL required for most operations on the runner VMs is simply the hostname of the proxy server VM, e.g. `http://e2e-piaj2z37enpb4-proxy-vm:3128`. However, operations inside Docker containers on the runner VMs need either:
> - The _fully-qualified_ name of the proxy VM, e.g. `http://e2e-piaj2z37enpb4-proxy-vm.e0gkjhpfr5quzatbjwfoss05vh.xx.internal.cloudapp.net:3128`, or
> - The private IP address of the proxy VM, e.g. `http://10.0.0.4:3128`
>
> The end-to-end tests get the proxy URL from the agent (via the predefined variable `$(Agent.ProxyUrl)`). Therefore, when you configure the agent you must give it one of the two proxy URLs described above (using either the fully-qualified name or the IP address). For example, To pass the fully-qualifed name during agent installation on a runner VM:
> ```
> proxy_hostname='<>'
> proxy_fqdn="http://$proxy_hostname.$(grep -Po '^search \K.*' /etc/resolv.conf):3128"
> ./config.sh --proxyurl $proxy_fqdn
> ```