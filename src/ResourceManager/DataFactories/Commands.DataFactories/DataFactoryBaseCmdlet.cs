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
using System.Management.Automation;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Commands.Utilities.Common;

namespace Microsoft.Azure.Commands.DataFactories
{
    public abstract class DataFactoryBaseCmdlet : CmdletWithSubscriptionBase
    {
        private DataFactoryClient dataFactoryClient;

        [Parameter(Position = 0, Mandatory = true, ValueFromPipelineByPropertyName = true,
            HelpMessage = "The resource group name.")]
        [ValidateNotNullOrEmpty]
        public string ResourceGroupName { get; set; }

        internal DataFactoryClient DataFactoryClient
        {
            get
            {
                if (this.dataFactoryClient == null)
                {
                    this.dataFactoryClient = new DataFactoryClient(CurrentSubscription);
                }
                return this.dataFactoryClient;
            }
            set
            {
                this.dataFactoryClient = value;
            }
        }

        protected override void WriteExceptionError(Exception exception)
        {
            // Override the default error message into a formatted message which contains Request Id
            CloudException cloudException = exception as CloudException;
            if (cloudException != null)
            {
                exception = cloudException.CreateFormattedException();
            }

            base.WriteExceptionError(exception);
        }
    }
}
