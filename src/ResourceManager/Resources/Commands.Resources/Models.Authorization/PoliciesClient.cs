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

using Microsoft.Azure.Management.Authorization;
using Microsoft.Azure.Management.Authorization.Models;
using Microsoft.WindowsAzure.Commands.Utilities.Common;
using Microsoft.WindowsAzure.Commands.Utilities.Common.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.Commands.Resources.Models.Authorization
{
    public class PoliciesClient
    {
        /// <summary>
        /// This queue is used by the tests to assign fixed role assignment
        /// names every time the test runs.
        /// </summary>
        public static Queue<Guid> RoleAssignmentNames { get; set; }

        public IAuthorizationManagementClient PolicyClient { get; set; }

        public GraphClient GraphClient { get; set; }

        static PoliciesClient()
        {
            RoleAssignmentNames = new Queue<Guid>();
        }

        /// <summary>
        /// Creates PoliciesClient using WindowsAzureSubscription instance.
        /// </summary>
        /// <param name="subscription">The WindowsAzureSubscription instance</param>
        public PoliciesClient(WindowsAzureSubscription subscription)
        {
            AccessTokenCredential creds = subscription.CreateTokenCredentials();
            GraphClient = new GraphClient(subscription, creds);
            PolicyClient = subscription.CreateClientFromResourceManagerEndpoint<AuthorizationManagementClient>();
        }

        public PSRoleDefinition GetRoleDefinition(string roleId)
        {
            return PolicyClient.RoleDefinitions.GetById(roleId).RoleDefinition.ToPSRoleDefinition();
        }

        /// <summary>
        /// Filters the existing role Definitions.
        /// </summary>
        /// <param name="name">The role name</param>
        /// <returns>The matched role Definitions</returns>
        public List<PSRoleDefinition> FilterRoleDefinitions(string name)
        {
            List<PSRoleDefinition> result = new List<PSRoleDefinition>();

            if (string.IsNullOrEmpty(name))
            {
                result.AddRange(PolicyClient.RoleDefinitions.List().RoleDefinitions.Select(r => r.ToPSRoleDefinition()));
            }
            else
            {
                result.Add(PolicyClient.RoleDefinitions.List().RoleDefinitions
                    .FirstOrDefault(r => r.Properties.RoleName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .ToPSRoleDefinition());
            }

            return result;
        }

        /// <summary>
        /// Creates new role assignment.
        /// </summary>
        /// <param name="parameters">The create parameters</param>
        /// <returns>The created role assignment object</returns>
        public PSRoleAssignment CreateRoleAssignment(FilterRoleAssignmentsOptions parameters)
        {
            Guid principalId = GraphClient.GetActiveDirectoryObject(parameters.PrincipalName).Id;
            Guid roleAssignmentId = RoleAssignmentNames.Count == 0 ? Guid.NewGuid() : RoleAssignmentNames.Dequeue();
            string roleDefinitionId = FilterRoleDefinitions(parameters.RoleDefinition).First().Id;

            RoleAssignmentCreateParameters createParameters = new RoleAssignmentCreateParameters
            {
                PrincipalId = principalId,
                RoleDefinitionId = roleDefinitionId
            };

            PolicyClient.RoleAssignments.Create(parameters.Scope, roleAssignmentId, createParameters);
            return PolicyClient.RoleAssignments.Get(parameters.Scope, roleAssignmentId).RoleAssignment.ToPSRoleAssignment(this, GraphClient);
        }

        /// <summary>
        /// Filters role assignments based on the passed options.
        /// </summary>
        /// <param name="options">The filtering options</param>
        /// <returns>The filtered role assignments</returns>
        public List<PSRoleAssignment> FilterRoleAssignments(FilterRoleAssignmentsOptions options)
        {
            List<PSRoleAssignment> result = new List<PSRoleAssignment>();
            ListAssignmentsFilterParameters parameters = new ListAssignmentsFilterParameters
            {
                AtScope = true
            };

            result.AddRange(PolicyClient.RoleAssignments.ListForScope(options.Scope, parameters).RoleAssignments.Select(r => r.ToPSRoleAssignment(this, GraphClient)));

            if (!string.IsNullOrEmpty(options.PrincipalName))
            {
                result = result.Where(r => r.Principal.Equals(options.PrincipalName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(options.RoleDefinition))
            {
                result = result.Where(r => r.RoleDefinitionName.Equals(options.RoleDefinition, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return result;
        }

        /// <summary>
        /// Deletes a role assignments based on the used options.
        /// </summary>
        /// <param name="options">The role assignment filtering options</param>
        /// <returns>The deleted role assignments</returns>
        public PSRoleAssignment RemoveRoleAssignment(FilterRoleAssignmentsOptions options)
        {
            PSRoleAssignment roleAssignment = FilterRoleAssignments(options).FirstOrDefault();

            if (roleAssignment != null)
            {
                PolicyClient.RoleAssignments.DeleteById(roleAssignment.Id);
            }

            return roleAssignment;
        }
    }
}
