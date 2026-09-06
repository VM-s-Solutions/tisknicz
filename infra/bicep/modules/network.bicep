// Private network path from the App Services to Postgres.
//
// WHY THIS EXISTS
// Production has no route to its database. main.bicep gates the
// "allow all Azure services" firewall rule to dev, and there is no other
// connectivity, so a prod deploy boots hosts that cannot reach Postgres. And
// because /health is dependency-free, the smoke gate would have called that
// green — which is why the DB-backed probe landed first.
//
// WHY A PRIVATE ENDPOINT AND NOT VNET INJECTION
// Flexible Server makes you choose ONE networking mode at creation:
// VNet-injected private access (network.delegatedSubnetResourceId) or public
// access + private endpoints. The choice is permanent — a server cannot be
// moved in or out of a VNet. Injection is also incompatible with everything
// else here: it cannot coexist with public access, and the CI migrate job
// reaches Postgres from a GitHub-hosted runner via a temporary firewall rule.
// The reason to never touch the block is the creation-time constraint itself,
// not any claim about silent no-ops: a server cannot be moved in or out of a
// VNet, so the property is simply not changeable after the fact.
//
// This module therefore NEVER touches the server's network block. It attaches
// a private endpoint alongside public access, which Microsoft documents as
// "works as designed", and leaves publicNetworkAccess Enabled so the migrate
// job keeps working. With ZERO permanent firewall rules the server is still
// unreachable from the internet: "If you don't configure any firewall rules,
// by default, traffic can't access the ... server."
//
// CROSS-REGION IS DELIBERATE
// The apps are in westeurope; Postgres is in northeurope because this
// subscription is offer-restricted for Flexible Server in westeurope. Private
// Link supports this — "the consumer's virtual network can be in region A. It
// can connect to services behind Private Link in region B." The VNet must be
// co-regional with the App Service (VNet integration requires it), not with
// the database.
//
// WHY vnetRouteAllEnabled IS LEFT OFF — and why the private path still works
// The two behaviours this depends on are both unconditional, verified against
// the App Service VNet-integration docs:
//   * DNS: "After your app integrates with your virtual network, it uses the
//     same DNS server that your virtual network is configured with. If no
//     custom DNS is specified, it uses Azure default DNS and any private zones
//     linked to the virtual network." No route-all needed for the zone below
//     to be consulted.
//   * Routing: "If all traffic routing isn't enabled, only private traffic
//     (RFC1918) ... is sent into the virtual network. Outbound traffic to the
//     internet is routed directly from the app." The private endpoint's
//     10.20.x address is RFC1918, so database traffic goes through the VNet
//     while Key Vault, Blob, Comgate, Packeta and Resend keep going direct.
// Turning route-all ON would need a NAT gateway for internet egress and is
// documented to break cross-region Storage access — so it stays off.
//
// One integration, five apps: all four API hosts and Functions share a single
// App Service Plan, and "multiple apps in the same App Service plan can use
// the same virtual network integration". A plan supports two integrations
// maximum; this uses one. /26 is the recommended size (/28 the hard floor).
//
// Refs:
//   https://learn.microsoft.com/en-us/azure/postgresql/network/concepts-networking-private-link
//   https://learn.microsoft.com/en-us/azure/private-link/private-endpoint-dns
//   https://learn.microsoft.com/en-us/azure/app-service/overview-vnet-integration

// INVARIANT, enforced by nothing here: this VNet must keep Azure-provided DNS.
// It sets no dhcpOptions deliberately. The integrated apps inherit the VNet's
// DNS, which is what makes the private zone below authoritative for them —
// adding custom DNS servers to this VNet silently breaks the entire private
// path and sends database traffic back out over the public FQDN.

@description('VNet name, e.g. vnet-makables-weu-prod.')
param vnetName string

@description('Region. MUST match the App Service region — regional VNet integration requires the app and the VNet to be co-regional. It does NOT need to match the Postgres region; Private Link is global-reach.')
param location string

@description('Resource ID of the Postgres Flexible Server to attach the private endpoint to.')
param postgresServerId string

@description('Address space. /16 leaves room for later subnets (a jump box, a NAT gateway) without renumbering.')
param addressPrefix string = '10.20.0.0/16'

@description('Subnet delegated to Microsoft.Web/serverFarms for App Service regional VNet integration. /26 is the size Microsoft recommends; /28 is the hard minimum and leaves no headroom for scale-out.')
param integrationSubnetPrefix string = '10.20.1.0/26'

@description('Subnet holding the private endpoint NIC. Must NOT be delegated — a delegated subnet cannot host a private endpoint.')
param privateEndpointSubnetPrefix string = '10.20.2.0/27'

// The zone name is not cosmetic. Private endpoint DNS configurations
// "only automatically generate if you use the recommended naming scheme",
// and this is that name for Microsoft.DBforPostgreSQL/flexibleServers.
// Get it wrong and the deploy still succeeds — the server FQDN just keeps
// resolving to the PUBLIC path, which is the silent failure this whole
// module exists to avoid.
var privateDnsZoneName = 'privatelink.postgres.database.azure.com'

// Subnets are declared inline rather than as child resources so the VNet is
// created with both in one PUT. Declaring them as children of an existing VNet
// is the shape that races on redeploy (each child PUT can drop siblings it
// does not know about).
resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [addressPrefix]
    }
    subnets: [
      {
        name: 'snet-appservice'
        properties: {
          addressPrefix: integrationSubnetPrefix
          delegations: [
            {
              name: 'appservice-delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'snet-privatelink'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          // Network policies default to Disabled for private endpoints, but
          // state it explicitly: with them Enabled an NSG or UDR on this
          // subnet can silently blackhole the endpoint.
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource privateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: privateDnsZoneName
  // Private DNS zones are global; 'global' is the only valid location.
  location: 'global'
}

// Without this link the zone exists but the apps never consult it, so the
// FQDN resolves to the public A record and traffic silently leaves the VNet.
resource dnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: privateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    // No auto-registration: this zone serves one private endpoint, and
    // registering every VNet NIC into it would be noise at best.
    registrationEnabled: false
  }
}

resource postgresPrivateEndpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: 'pe-${vnetName}-postgres'
  location: location
  properties: {
    subnet: {
      // Name-based, not subnets[1]: a positional index couples correctness
      // to the RP's array ordering, and if it ever inverted the API hosts
      // would try to integrate into the private-endpoint subnet. Referencing
      // vnet.id keeps the implicit dependsOn that a bare resourceId() loses.
      id: '${vnet.id}/subnets/snet-privatelink'
    }
    privateLinkServiceConnections: [
      {
        name: 'postgres'
        properties: {
          privateLinkServiceId: postgresServerId
          // Verified against the Private Link DNS table: the subresource for
          // Microsoft.DBforPostgreSQL/flexibleServers is 'postgresqlServer'.
          groupIds: ['postgresqlServer']
        }
      }
    ]
  }
}

// This is what writes the A record into the zone. Without it the endpoint has
// a private IP that nothing can resolve, and every app falls back to the
// public FQDN — a fully green deploy with no private path.
resource dnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: postgresPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'postgres'
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}

@description('Subnet the App Services integrate into (delegated to Microsoft.Web/serverFarms).')
output integrationSubnetId string = '${vnet.id}/subnets/snet-appservice'

// Deliberately NO output for the endpoint's private IP. customDnsConfigs is a
// writable request field this template never sets, so its runtime content is
// the resource provider's business; indexing [0] on it would be evaluated on
// every deploy and, if the array came back empty, would fail the nested
// deployment AFTER the VNet, zone, link and endpoint were created and BEFORE
// any app got its subnet — the worst possible point in a first prod deploy.
// Nothing consumed the value. Microsoft's own AVM private-endpoint module
// outputs the whole array and offers no such convenience output, for the same
// reason. Read the IP from the portal or `az network private-endpoint show`.
