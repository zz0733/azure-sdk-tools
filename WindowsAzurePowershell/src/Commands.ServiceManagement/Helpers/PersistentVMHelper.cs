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


namespace Microsoft.WindowsAzure.Commands.ServiceManagement.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.IO;
    using System.Xml.Serialization;
    using AutoMapper;
    using Management.Compute.Models;
    using Model;
    using WindowsAzure.ServiceManagement;
    using Properties;
    using System.Management.Automation;
    using ConfigurationSet = Model.PersistentVMModel.ConfigurationSet;
    using NetworkConfigurationSet = Model.PersistentVMModel.NetworkConfigurationSet;
    using WindowsProvisioningConfigurationSet = Model.PersistentVMModel.WindowsProvisioningConfigurationSet;
    using LinuxProvisioningConfigurationSet = Model.PersistentVMModel.LinuxProvisioningConfigurationSet;
    using DataVirtualHardDisk = Model.PersistentVMModel.DataVirtualHardDisk;
    using OSVirtualHardDisk = Model.PersistentVMModel.OSVirtualHardDisk;
    using RoleInstance = Management.Compute.Models.RoleInstance;

    public static class PersistentVMHelper
    {
        public static void SaveStateToFile(PersistentVM role, string filePath)
        {
            if (role == null)
            {
                throw new ArgumentNullException("role", Resources.MissingPersistentVMRole);
            }
            
            XmlAttributeOverrides overrides = new XmlAttributeOverrides();
            XmlAttributes ignoreAttrib = new XmlAttributes();
            ignoreAttrib.XmlIgnore = true;
            overrides.Add(typeof(DataVirtualHardDisk), "MediaLink", ignoreAttrib);
            overrides.Add(typeof(DataVirtualHardDisk), "SourceMediaLink", ignoreAttrib);
            overrides.Add(typeof(OSVirtualHardDisk), "MediaLink", ignoreAttrib);

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PersistentVM), overrides, new Type[] { typeof(NetworkConfigurationSet) }, null, null);
            using (TextWriter writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, role);
            }
        }

        public static PersistentVM LoadStateFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new ArgumentException(Resources.MissingPersistentVMFile, "filePath");
            }

            XmlAttributeOverrides overrides = new XmlAttributeOverrides();
            XmlAttributes ignoreAttrib = new XmlAttributes();
            ignoreAttrib.XmlIgnore = true;
            overrides.Add(typeof(DataVirtualHardDisk), "MediaLink", ignoreAttrib);
            overrides.Add(typeof(DataVirtualHardDisk), "SourceMediaLink", ignoreAttrib);
            overrides.Add(typeof(OSVirtualHardDisk), "MediaLink", ignoreAttrib);
            overrides.Add(typeof(OSVirtualHardDisk), "SourceImageName", ignoreAttrib);

            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PersistentVM), overrides, new Type[] { typeof(NetworkConfigurationSet) }, null, null);

            PersistentVM role = null;
            
            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                role = serializer.Deserialize(stream) as PersistentVM;
            }

            return role;
        }

        // Returns a RoleNamesCollection based on instances in the roleInstanceList
        // whose RoleInstance.RoleName matches the roleName passed in.  Wildcards
        // are handled for the roleName passed in.
        // This function also verifies that the RoleInstance exists before adding the
        // RoleName to the RoleNamesCollection.
        public static RoleNamesCollection GetRoleNames(RoleInstanceList roleInstanceList, string roleName)
        {
            var roleNamesCollection = new RoleNamesCollection();
            if (!string.IsNullOrEmpty(roleName))
            {
                if (WildcardPattern.ContainsWildcardCharacters(roleName))
                {
                    WildcardOptions wildcardOptions = WildcardOptions.IgnoreCase | WildcardOptions.Compiled;
                    WildcardPattern wildcardPattern = new WildcardPattern(roleName, wildcardOptions);

                    foreach (var role in roleInstanceList)
                        if (!string.IsNullOrEmpty(role.RoleName) && wildcardPattern.IsMatch(role.RoleName))
                        {
                            roleNamesCollection.Add(role.RoleName);
                        }
                }
                else
                {
                    var roleInstance = roleInstanceList.Where(r => r.RoleName != null).
                        FirstOrDefault(r => r.RoleName.Equals(roleName, StringComparison.InvariantCultureIgnoreCase));
                    if (roleInstance != null)
                    {
                        roleNamesCollection.Add(roleName);
                    }
                }
            }
            return roleNamesCollection;
        }

        public static Collection<ConfigurationSet> MapConfigurationSets(IList<Management.Compute.Models.ConfigurationSet> configurationSets)
        {
            var result = new Collection<ConfigurationSet>();
            var n = configurationSets.Where(c => c.ConfigurationSetType == "NetworkConfiguration").Select(Mapper.Map<Model.PersistentVMModel.NetworkConfigurationSet>).ToList();
            var w = configurationSets.Where(c => c.ConfigurationSetType == ConfigurationSetTypes.WindowsProvisioningConfiguration).Select(Mapper.Map<WindowsProvisioningConfigurationSet>).ToList();
            var l = configurationSets.Where(c => c.ConfigurationSetType == ConfigurationSetTypes.LinuxProvisioningConfiguration).Select(Mapper.Map<LinuxProvisioningConfigurationSet>).ToList();
            n.ForEach(result.Add);
            w.ForEach(result.Add);
            l.ForEach(result.Add);
            return result;
        }

        public static  IList<Management.Compute.Models.ConfigurationSet> MapConfigurationSets(Collection<ConfigurationSet> configurationSets)
        {
            var result = new Collection<Management.Compute.Models.ConfigurationSet>();
            foreach (var networkConfig in configurationSets.OfType<NetworkConfigurationSet>())
            {
                result.Add(Mapper.Map<Management.Compute.Models.ConfigurationSet>(networkConfig));
            }
            foreach (var windowsConfig in configurationSets.OfType<WindowsProvisioningConfigurationSet>())
            {
                result.Add(Mapper.Map<Management.Compute.Models.ConfigurationSet>(windowsConfig));
            }
            foreach (var linuxConfig in configurationSets.OfType<LinuxProvisioningConfigurationSet>())
            {
                result.Add(Mapper.Map<Management.Compute.Models.ConfigurationSet>(linuxConfig));
            }
            return result;
        }

    }
}
