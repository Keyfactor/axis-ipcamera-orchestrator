// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera
{ 
    public class Inventory : IInventoryJobExtension
    {
        private readonly ILogger _logger;

        // Necessary to implement IInventoryJobExtension but not used.  Leave as empty string.
        public string ExtensionName => "";

        public IPAMSecretResolver Resolver;
        
        public Inventory(IPAMSecretResolver resolver)
        {
            _logger = LogHandler.GetClassLogger<Inventory>();
            Resolver = resolver;
        }
        
        // Job Entry Point
        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventory)
        {
            List<CurrentInventoryItem> inventoryItems = new List<CurrentInventoryItem>();
            var warningFlag = false;
            
            try
            {
                _logger.MethodEntry();
                
                _logger.LogTrace($"Begin Inventory for Client Machine {config.CertificateStoreDetails.ClientMachine}...");
                _logger.LogDebug($"Inventory Config: {JsonConvert.SerializeObject(config)}");
                
                _logger.LogTrace("Create HTTPS client to connect to device");
                var client = new AxisHttpClient(config, config.CertificateStoreDetails, Resolver);

                // Perform CA cert inventory
                _logger.LogTrace("Retrieve all CA certificates");
                CACertificateData data1 = client.ListCACertificates();
                
                // Perform client cert inventory
                _logger.LogTrace("Retrieve all client certificates");
                CertificateData data2 = client.ListCertificates();
                
                // Get the default keystore
                _logger.LogTrace("Retrieve the default keystore");
                Constants.Keystore defaultKeystore = client.GetDefaultKeystore();
                string defaultKeystoreString = Enum.GetName(typeof(Constants.Keystore), defaultKeystore);
                _logger.LogDebug($"Inventory - Default keystore: {defaultKeystoreString}");
                
                // Create new list of client certs that are only tied to the default keystore
                _logger.LogTrace("Filtering list of client certificates to those stored in the default keystore");
                CertificateData data2DefKey = new CertificateData
                {
                    Status = Constants.Status.Success,
                    Certs = new List<Certificate>()
                };
                foreach (var cert in data2.Certs.Where(cert => cert.Keystore == defaultKeystore))
                {
                    data2DefKey.Certs.Add(cert);
                }
                
                _logger.LogTrace("Retrieve all certificate bindings for each possible certificate usage type");
                // Lookup the certificate used for HTTPS, MQTT, IEEE802.X
                string httpAlias = client.GetCertUsageBinding(Constants.CertificateUsage.Https);
                string ieeeAlias = client.GetCertUsageBinding(Constants.CertificateUsage.IEEE);
                string mqttAlias = client.GetCertUsageBinding(Constants.CertificateUsage.MQTT);
                
                // Set the binding on the client certificates object if the aliases found for each certificate usage match
                _logger.LogTrace("Mark each client certificate with the appropriate certificate usage type");
                foreach (Certificate c in data2DefKey.Certs)
                {
                    if (c.Alias.Equals(httpAlias))
                    {
                        _logger.LogDebug($"Client cert with alias '{c.Alias}' is used for HTTPS");
                        c.Binding = Constants.CertificateUsage.Https;
                    }
                    else if (c.Alias.Equals(ieeeAlias))
                    {
                        _logger.LogDebug($"Client cert with alias '{c.Alias}' is used for IEEE");
                        c.Binding = Constants.CertificateUsage.IEEE;
                    }
                    else if (c.Alias.Equals(mqttAlias))
                    {
                        _logger.LogDebug($"Client cert with alias '{c.Alias}' is used for MQTT");
                        c.Binding = Constants.CertificateUsage.MQTT;
                    }
                    else
                    {
                        // If no match, reset the cert usage
                        _logger.LogDebug($"Client cert with alias '{c.Alias}' has no known certificate usage");
                        c.Binding = Constants.CertificateUsage.Other;
                    }
                }

                // Build the list of CA certificates and add to the InventoryItems object that is sent back to Command
                inventoryItems.AddRange(data1.CACerts.Select(
                    c =>
                    {
                        try
                        {
                            _logger.LogDebug($"Building CA Cert List Inventory Item: {c.Alias} Pem: {c.CertAsPem}");
                            return BuildInventoryItem(c);
                        }
                        catch 
                        {
                            _logger.LogWarning($"Could not fetch the CA certificate: {c?.Alias} associated with description {c?.CertAsPem}.");
                            warningFlag = true;
                            return new CurrentInventoryItem();
                        }
                    }).Where(item => item?.Certificates != null).ToList());
                
                // Build the list of client certificates and add to the InventoryItems object that is sent back to Command
                inventoryItems.AddRange(data2DefKey.Certs.Select(
                    c =>
                    {
                        try
                        {
                            _logger.LogTrace($"Building Client Cert List Inventory Item: {c.Alias} Pem: {c.CertAsPem}");
                            return BuildInventoryItem(c);
                        }
                        catch 
                        {
                            _logger.LogWarning($"Could not fetch the client certificate: {c?.Alias} associated with description {c?.CertAsPem}.");
                            warningFlag = true;
                            return new CurrentInventoryItem();
                        }
                    }).Where(item => item?.Certificates != null).ToList());
                
                _logger.MethodExit();

                if (warningFlag)
                {
                    _logger.LogTrace("Found Warning during Inventory Item Creation");
                    return new JobResult()
                    {
                        Result = OrchestratorJobStatusJobResult.Warning, 
                        JobHistoryId = config.JobHistoryId,
                        FailureMessage = "Could not fetch 1 or more certificates. Refer to the log for more detailed information."
                    };
                }
            }
            catch (Exception e1)
            {
                // Status: 2=Success, 3=Warning, 4=Error
                return new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, 
                    FailureMessage = $"Inventory Job Failed During Inventory Item Creation: {e1.Message} - Refer to logs for more detailed information." };
            }

            try
            {
                // Sends inventoried certificates back to KF Command
                _logger.LogTrace("Submitting Inventory To Keyfactor via submitInventory.Invoke");
                submitInventory.Invoke(inventoryItems);
                _logger.LogTrace("Submitted Inventory To Keyfactor via submitInventory.Invoke");
                
                // Status: 2=Success, 3=Warning, 4=Error
                return new JobResult() { Result = OrchestratorJobStatusJobResult.Success, JobHistoryId = config.JobHistoryId };
            }
            catch (Exception e2)
            {
                // ** NOTE: If the cause of the submitInventory.Invoke exception is a communication issue between the Orchestrator server and the Command server, the job status returned here
                //  may not be reflected in Keyfactor Command.
                return new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, 
                    FailureMessage = $"Inventory Job Failed During Inventory Item Submission: {e2.Message} - Refer to logs for more detailed information." };
            }
        }

        private CurrentInventoryItem BuildInventoryItem(CACertificate caCert)
        {
            try
            {
                _logger.MethodEntry();

                var caCertList = new List<string>();
                caCertList.Add(caCert.CertAsPem);
                var item = new CurrentInventoryItem
                {
                    Alias = caCert.Alias,
                    Certificates =  caCertList,
                    ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                    PrivateKeyEntry = false, // CA certificates will not have private keys associated
                    UseChainLevel = false, // Will only ever have 1 single cert
                    Parameters = new Dictionary<string, object>()
                    {
                        {Constants.CertUsageParamName, Constants.GetCertUsageAsString(Constants.CertificateUsage.Trust)}
                    }
                };

                _logger.MethodExit();
                
                return item;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error Occurred in Inventory.BuildInventoryItem for CA Certs: {LogHandler.FlattenException(e)}");
                throw;
            }
        }
        
        private CurrentInventoryItem BuildInventoryItem(Certificate cert)
        {
            try
            {
                _logger.MethodEntry();

                var certList = new List<string>();
                certList.Add(cert.CertAsPem);
                var item = new CurrentInventoryItem
                {
                    Alias = cert.Alias,
                    Certificates =  certList,
                    ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                    PrivateKeyEntry = true, // Client certs will have private keys on the camera
                    UseChainLevel = false, // Will only ever have 1 single cert
                    Parameters = new Dictionary<string, object>()
                    {
                        {Constants.CertUsageParamName, Constants.GetCertUsageAsString(cert.Binding)}
                    }
                };

                _logger.MethodExit();
                
                return item;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error Occurred in Inventory.BuildInventoryItem for Client Certificates: {LogHandler.FlattenException(e)}");
                throw;
            }
        }
    }
}