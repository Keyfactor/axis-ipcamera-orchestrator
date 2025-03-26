using System;
using System.Collections.Generic;
using System.Linq;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Common.Enums;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera
{
    // The Inventory class implementes IAgentJobExtension and is meant to find all of the certificates in a given certificate store on a given server
    //  and return those certificates back to Keyfactor for storing in its database.  Private keys will NOT be passed back to Keyfactor Command 
    public class Inventory : IInventoryJobExtension
    {
        private readonly ILogger _logger;
        
        public Inventory()
        {
            _logger = LogHandler.GetClassLogger<Inventory>();
        }
        
        //Necessary to implement IInventoryJobExtension but not used.  Leave as empty string.
        public string ExtensionName => "";

        //Job Entry Point
        public JobResult ProcessJob(InventoryJobConfiguration config, SubmitInventoryUpdate submitInventory)
        {
            //METHOD ARGUMENTS...
            //config - contains context information passed from KF Command to this job run:
            //
            // config.Server.Username, config.Server.Password - credentials for orchestrated server - use to authenticate to certificate store server.
            //
            // config.ServerUsername, config.ServerPassword - credentials for orchestrated server - use to authenticate to certificate store server.
            // config.CertificateStoreDetails.ClientMachine - server name or IP address of orchestrated server
            // config.CertificateStoreDetails.StorePath - location path of certificate store on orchestrated server
            // config.CertificateStoreDetails.StorePassword - if the certificate store has a password, it would be passed here
            // config.CertificateStoreDetails.Properties - JSON string containing custom store properties for this specific store type
            
            //List<AgentCertStoreInventoryItem> is the collection that the interface expects to return from this job.  It will contain a collection of certificates found in the store along with other information about those certificates
            List<CurrentInventoryItem> inventoryItems = new List<CurrentInventoryItem>();

            try
            {
                //Code logic to:
                // 1) Connect to the orchestrated server (config.CertificateStoreDetails.ClientMachine) containing the certificate store to be inventoried (config.CertificateStoreDetails.StorePath)
                // 2) Custom logic to retrieve certificates from certificate store.
                // 3) Add certificates (no private keys) to the collection below.  If multiple certs in a store comprise a chain, the Certificates array will house multiple certs per InventoryItem.  If multiple certs
                //     in a store comprise separate unrelated certs, there will be one InventoryItem object created per certificate.

                //**** Will need to uncomment the block below and code to the extension's specific needs.  This builds the collection of certificates and related information that will be passed back to the KF Orchestrator service and then Command.
                //inventoryItems.Add(new AgentCertStoreInventoryItem()
                //{
                //    ItemStatus = OrchestratorInventoryItemStatus.Unknown, //There are other statuses, but Command can determine how to handle new vs modified certificates
                //    Alias = {valueRepresentingChainIdentifier}
                //    PrivateKeyEntry = true|false //You will not pass the private key back, but you can identify if the main certificate of the chain contains a private key in the store
                //    UseChainLevel = true|false,  //true if Certificates will contain > 1 certificate, main cert => intermediate CA cert => root CA cert.  false if Certificates will contain an array of 1 certificate
                //    Certificates = //Array of single X509 certificates in Base64 string format (certificates if chain, single cert if not), something like:
                //    ****************************
                //          foreach(X509Certificate2 certificate in certificates)
                //              certList.Add(Convert.ToBase64String(certificate.Export(X509ContentType.Cert)));
                //              certList.ToArray();
                //    ****************************
                //});
                
                _logger.MethodEntry();
                _logger.LogTrace("Begin Inventory...");
                _logger.LogDebug($"Inventory Config {JsonConvert.SerializeObject(config)}");
                _logger.LogDebug($"Client Machine: {config.CertificateStoreDetails.ClientMachine}");
                
                _logger.LogTrace("Creating Api Rest Client...");
                var client = new AxisRestClient(config, config.CertificateStoreDetails);
                _logger.LogTrace("Api Rest Client Created...");

                // Perform CA cert inventory
                CACertificateData data1 = client.ListCACertificates();
                
                // Perform cert inventory
                CertificateData data2 = client.ListCertificates();
                
                // Lookup the certificate used for HTTPS
                string httpAlias = client.GetBinding(Constants.CertificateUsage.Https);
                
                // Set the binding on the certificate object if the aliases match
                foreach (Certificate c in data2.Certs)
                {
                    if (c.Alias.Equals(httpAlias))
                    {
                        c.Binding = Constants.CertificateUsage.Https;
                    }
                    else
                    {
                        // Reset the other certs
                        c.Binding = Constants.CertificateUsage.Unknown;
                    }
                }

                inventoryItems.AddRange(data1.CACerts.Select(
                    c =>
                    {
                        try
                        {
                            _logger.LogTrace($"Building Cert List Inventory Item: {c.Alias} Pem: {c.CertAsPem}");
                            return BuildInventoryItem(c);
                        }
                        catch 
                        {
                            _logger.LogWarning($"Could not fetch the certificate: {c?.Alias} associated with description {c?.CertAsPem}.");
                            /*sb.Append(
                                $"Could not fetch the certificate: {c?.AliasName} associated with issuer {c?.Certificates}.{Environment.NewLine}");
                            warningFlag = true;*/
                            return new CurrentInventoryItem();
                        }
                    }).Where(item => item?.Certificates != null).ToList());
                
                inventoryItems.AddRange(data2.Certs.Select(
                    c =>
                    {
                        try
                        {
                            _logger.LogTrace($"Building Cert List Inventory Item: {c.Alias} Pem: {c.CertAsPem}");
                            return BuildInventoryItem(c);
                        }
                        catch 
                        {
                            _logger.LogWarning($"Could not fetch the certificate: {c?.Alias} associated with description {c?.CertAsPem}.");
                            /*sb.Append(
                                $"Could not fetch the certificate: {c?.AliasName} associated with issuer {c?.Certificates}.{Environment.NewLine}");
                            warningFlag = true;*/
                            return new CurrentInventoryItem();
                        }
                    }).Where(item => item?.Certificates != null).ToList());

                
                submitInventory.Invoke(inventoryItems);
               



                

            }
            catch (Exception ex)
            {
                //Status: 2=Success, 3=Warning, 4=Error
                return new JobResult() { Result = Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, FailureMessage = "Custom message you want to show to show up as the error message in Job History in KF Command" };
            }

            try
            {
                //Sends inventoried certificates back to KF Command
                _logger.LogTrace("Submitting Inventory To Keyfactor via submitInventory.Invoke");
                submitInventory.Invoke(inventoryItems);
                _logger.LogTrace("Submitted Inventory To Keyfactor via submitInventory.Invoke");
                //Status: 2=Success, 3=Warning, 4=Error
                return new JobResult() { Result = Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Success, JobHistoryId = config.JobHistoryId };
            }
            catch (Exception ex)
            {
                // NOTE: if the cause of the submitInventory.Invoke exception is a communication issue between the Orchestrator server and the Command server, the job status returned here
                //  may not be reflected in Keyfactor Command.
                return new JobResult() { Result = Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, FailureMessage = "Custom message you want to show to show up as the error message in Job History in KF Command" };
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
                    PrivateKeyEntry = false, // CA certificates will not have private keys in the cert store
                    UseChainLevel = false, // Will only ever have 1 cert level
                    Parameters = new Dictionary<string, object>()
                    {
                        {"CertUsage", "Trust"}
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
        
        //TODO: Add parameters for other binding aliases
        private CurrentInventoryItem BuildInventoryItem(Certificate cert)
        {
            try
            {
                _logger.MethodEntry();
                
                // Get the cert usage as a string
                string certUsageString = GetCertUsageAsString(cert.Binding);

                Dictionary<string, object> parameters = new Dictionary<string, object>();
                parameters.Add("CertUsage",certUsageString);

                var certList = new List<string>();
                certList.Add(cert.CertAsPem);
                var item = new CurrentInventoryItem
                {
                    Alias = cert.Alias,
                    Certificates =  certList,
                    ItemStatus = OrchestratorInventoryItemStatus.Unknown,
                    PrivateKeyEntry = true, // Client certs will have private keys on the camera
                    UseChainLevel = false, // Will only ever have 1 cert level
                    Parameters = parameters
                };

                _logger.MethodExit();
                return item;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error Occurred in Inventory.BuildInventoryItem for Certificates: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        private string GetCertUsageAsString(Constants.CertificateUsage certUsageEnum)
        {
            Constants.CertificateUsage target = certUsageEnum;
            string certUsageString = "";
            switch (target)
            {
                    case Constants.CertificateUsage.Https:
                    {
                        certUsageString = "HTTPS";
                        break;
                    }
                    case Constants.CertificateUsage.MQTT:
                    {
                        certUsageString = "MQTT";
                        break;
                    }
                    case Constants.CertificateUsage.IEEE:
                    {
                        certUsageString = "IEEE802.X";
                        break;
                    }
                    case Constants.CertificateUsage.Unknown:
                    {
                        certUsageString = "None";
                        break;
                    }
                default:
                    break;
            }

            return certUsageString;
        }
    }
}