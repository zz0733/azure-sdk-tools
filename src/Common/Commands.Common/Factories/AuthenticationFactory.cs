﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Azure.Subscriptions;
using Microsoft.Azure.Subscriptions.Models;
using Microsoft.WindowsAzure.Subscriptions;
using Microsoft.WindowsAzure.Commands.Common.Models;
using Microsoft.WindowsAzure.Commands.Common.Properties;
using Microsoft.WindowsAzure.Commands.Utilities.Common;
using Microsoft.WindowsAzure.Commands.Utilities.Common.Authentication;

namespace Microsoft.WindowsAzure.Commands.Common.Factories
{
    public class AuthenticationFactory : IAuthenticationFactory
    {
        private const string CommonAdTenant = "Common";

        private readonly IDictionary<Guid, IAccessToken> subscriptionTokenCache = new Dictionary<Guid, IAccessToken>();
        private readonly AzureProfile profile;

        public AuthenticationFactory(AzureProfile azureProfile)
        {
            TokenProvider = new AdalTokenProvider();
            profile = azureProfile;
        }

        public ITokenProvider TokenProvider { get; set; }

        public IEnumerable<AzureSubscription> Authenticate(AzureEnvironment environment, AzureModule currentMode, bool noPrompt, out string userId)
        {
            string newUserId = null;
            var subscriptions = AuthenticateAndGetSubscriptions(environment, currentMode, ref newUserId, null, noPrompt);
            userId = newUserId;
            return subscriptions;
        }

        public IEnumerable<AzureSubscription> Authenticate(AzureEnvironment environment, AzureModule currentMode, bool noPrompt, string userId)
        {
            return AuthenticateAndGetSubscriptions(environment, currentMode, ref userId, null, noPrompt);
        }

        public IEnumerable<AzureSubscription> Authenticate(AzureEnvironment environment, AzureModule currentMode, bool noPrompt, string userId, SecureString password)
        {
            return AuthenticateAndGetSubscriptions(environment, currentMode, ref userId, password, noPrompt);
        }

        
        private IList<AzureSubscription> AuthenticateAndGetSubscriptions(AzureEnvironment environment, AzureModule currentMode, 
            ref string userId, SecureString password, bool noPrompt)
        {
            Func<AdalConfiguration, string, SecureString, IAccessToken> getTokenFunction = TokenProvider.GetNewToken;
            List<AzureSubscription> result;

            if (noPrompt)
            {
                getTokenFunction = TokenProvider.GetCachedToken;
            }

            // Get common token and list all tenants
            var commonTenantToken = getTokenFunction(GetAdalConfiguration(environment, CommonAdTenant), userId, password);
            userId = commonTenantToken.UserId;

            if (currentMode == AzureModule.AzureResourceManager)
            {
                result = GetResourceManagerSubscriptions(environment, password, commonTenantToken, getTokenFunction)
                    .Union(GetServiceManagementSubscriptions(environment, password, commonTenantToken, getTokenFunction))
                    .ToList();
            }
            else
            {
                result = GetServiceManagementSubscriptions(environment, password, commonTenantToken, getTokenFunction)
                    .ToList();
            }

            // Set user ID
            foreach (var subscription in result)
            {
                subscription.Properties[AzureSubscription.Property.UserAccount] = userId;
            }

            return result;
        }

        private IEnumerable<AzureSubscription> GetResourceManagerSubscriptions(AzureEnvironment environment, SecureString password,
            IAccessToken commonTenantToken, Func<AdalConfiguration, string, SecureString, IAccessToken> getTokenFunction)
        {
            TenantListResult tenants;
            using (var subscriptionClient = AzurePowerShell.ClientFactory.CreateClient<Azure.Subscriptions.SubscriptionClient>(
                new TokenCloudCredentials(commonTenantToken.AccessToken),
                new Uri(environment.GetEndpoint(AzureEnvironment.Endpoint.ResourceManagerEndpoint))))
            {
                tenants = subscriptionClient.Tenants.List();
            }

            // Go over each tenant and get all subscriptions for tenant
            foreach (var tenant in tenants.TenantIds)
            {
                // Generate tenant specific token to query list of subscriptions
                var tenantToken = getTokenFunction(GetAdalConfiguration(environment, tenant.TenantId), commonTenantToken.UserId,
                    password);

                using (var subscriptionClient = AzurePowerShell.ClientFactory.CreateClient<Azure.Subscriptions.SubscriptionClient>(
                        new TokenCloudCredentials(tenantToken.AccessToken),
                        new Uri(environment.GetEndpoint(AzureEnvironment.Endpoint.ResourceManagerEndpoint))))
                {
                    var subscriptionListResult = subscriptionClient.Subscriptions.List();
                    foreach (var subscription in subscriptionListResult.Subscriptions)
                    {
                        AzureSubscription psSubscription = new AzureSubscription
                        {
                            Id = new Guid(subscription.SubscriptionId),
                            Name = subscription.DisplayName,
                            Environment = environment.Name
                        };
                        if (commonTenantToken.LoginType == LoginType.LiveId)
                        {
                            subscriptionTokenCache[psSubscription.Id] = tenantToken;
                        }
                        else
                        {
                            subscriptionTokenCache[psSubscription.Id] = commonTenantToken;
                        }

                        yield return psSubscription;
                    }
                }
            }
        }

        private IEnumerable<AzureSubscription> GetServiceManagementSubscriptions(AzureEnvironment environment, SecureString password,
            IAccessToken commonTenantToken, Func<AdalConfiguration, string, SecureString, IAccessToken> getTokenFunction)
        {
            using (var subscriptionClient = AzurePowerShell.ClientFactory.CreateClient<WindowsAzure.Subscriptions.SubscriptionClient>(
                        new TokenCloudCredentials(commonTenantToken.AccessToken),
                        new Uri(environment.GetEndpoint(AzureEnvironment.Endpoint.ServiceEndpoint))))
            {
                var subscriptionListResult = subscriptionClient.Subscriptions.List();
                foreach (var subscription in subscriptionListResult.Subscriptions)
                {
                    AzureSubscription psSubscription = new AzureSubscription
                    {
                        Id = new Guid(subscription.SubscriptionId),
                        Name = subscription.SubscriptionName,
                        Environment = environment.Name
                    };
                    if (commonTenantToken.LoginType == LoginType.LiveId)
                    {
                        subscriptionTokenCache[psSubscription.Id] = getTokenFunction(
                            GetAdalConfiguration(environment, subscription.ActiveDirectoryTenantId),
                            commonTenantToken.UserId, password);
                    }
                    else
                    {
                        subscriptionTokenCache[psSubscription.Id] = commonTenantToken;
                    }

                    yield return psSubscription;
                }
            }
        }

        public SubscriptionCloudCredentials GetSubscriptionCloudCredentials(Guid subscriptionId)
        {
            var subscription = profile.Subscriptions[subscriptionId];
            if (subscription == null)
            {
                throw new ArgumentException("Specified subscription has not been loaded.");
            }

            var environment = profile.Environments[subscription.Environment];
            var userId = subscription.GetProperty(AzureSubscription.Property.UserAccount);
            var certificate = WindowsAzureCertificate.FromThumbprint(subscription.GetProperty(AzureSubscription.Property.Thumbprint));

            if (subscriptionTokenCache.ContainsKey(subscriptionId))
            {
                return new AccessTokenCredential(subscriptionId.ToString(), subscriptionTokenCache[subscriptionId]);
            }
            else if (userId != null)
            {
                // Try to authenticate using AzureResourceManager (to get as many subscriptions as possible)
                AuthenticateAndGetSubscriptions(environment, AzureModule.AzureResourceManager, ref userId, null, true);
                if (!subscriptionTokenCache.ContainsKey(subscriptionId))
                {
                    throw new ArgumentException(Resources.InvalidSubscriptionState);
                }
                return new AccessTokenCredential(subscriptionId.ToString(), subscriptionTokenCache[subscriptionId]);
            }
            else if (certificate != null)
            {
                return new CertificateCloudCredentials(subscriptionId.ToString(), certificate);
            }
            else
            {
                throw new ArgumentException(Resources.InvalidSubscriptionState);
            }
        }

        private AdalConfiguration GetAdalConfiguration(AzureEnvironment environment, string tenantId)
        {
            var adEndpoint = environment.Endpoints[AzureEnvironment.Endpoint.ActiveDirectoryEndpoint];
            var adResourceId = environment.Endpoints[AzureEnvironment.Endpoint.ActiveDirectoryServiceEndpointResourceId];

            return new AdalConfiguration
            {
                AdEndpoint = adEndpoint,
                ResourceClientUri = adResourceId,
                AdDomain = tenantId
            };
        }
    }
}
