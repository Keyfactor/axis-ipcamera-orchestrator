// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Text;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;
using Keyfactor.Orchestrators.Extensions.Interfaces;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera
{
    public class Management : IManagementJobExtension
    {
        private readonly ILogger _logger;
        
        //Necessary to implement IManagementJobExtension but not used.  Leave as empty string.
        public string ExtensionName => "";

        public IPAMSecretResolver Resolver;
        public Management(IPAMSecretResolver resolver)
        {
            _logger = LogHandler.GetClassLogger<Management>();
            Resolver = resolver;
        }

        //Job Entry Point
        public JobResult ProcessJob(ManagementJobConfiguration config)
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
            //
            // config.JobCertificate.EntryContents - Base64 encoded string representation (PKCS12 if private key is included, DER if not) of the certificate to add for Management-Add jobs.
            // config.JobCertificate.Alias - optional string value of certificate alias (used in java keystores and some other store types)
            // config.OperationType - enumeration representing function with job type.  Used only with Management jobs where this value determines whether the Management job is a CREATE/ADD/REMOVE job.
            // config.Overwrite - Boolean value telling the Orchestrator Extension whether to overwrite an existing certificate in a store.  How you determine whether a certificate is "the same" as the one provided is AnyAgent implementation dependent
            // config.JobCertificate.PrivateKeyPassword - For a Management Add job, if the certificate being added includes the private key (therefore, a pfx is passed in config.JobCertificate.EntryContents), this will be the password for the pfx.


            //NLog Logging to c:\CMS\Logs\CMS_Agent_Log.txt
            
            try
            {
                _logger.MethodEntry();
                _logger.LogTrace($"Begin Management for Client Machine {config.CertificateStoreDetails.ClientMachine}...");
                
                //Management jobs, unlike Discovery, Inventory, and Reenrollment jobs can have 3 different purposes:
                switch (config.OperationType)
                {
                    case CertStoreOperationType.Add:
                    {
                        _logger.LogInformation("Entered Management-Add Operation");
                        
                        //OperationType == Add - Add a certificate to the certificate store passed in the config object
                        //Code logic to:
                        // 1) Connect to the orchestrated server (config.CertificateStoreDetails.ClientMachine) containing the certificate store
                        // 2) Custom logic to add certificate to certificate store (config.CertificateStoreDetails.StorePath) possibly using alias as an identifier if applicable (config.JobCertificate.Alias).  Use alias and overwrite flag (config.Overwrite)
                        //     to determine if job should overwrite an existing certificate in the store, for example a renewal.

                        // Retrieve management config from Command
                        _logger.LogDebug($"Management Config {JsonConvert.SerializeObject(config)}");
                        _logger.LogDebug($"Client Machine: {config.CertificateStoreDetails.ClientMachine}");

                        // Get needed information from config
                        string alias = config.JobCertificate.Alias;
                        bool overwrite = config.Overwrite;
                        string certBase64Der = config.JobCertificate.Contents;

                        _logger.LogDebug($"Certificate contents:{certBase64Der}");

                        // Prevent add of client certs; Client certs may only be added via reenrollment
                        if (IsCACertificate(certBase64Der))
                        {
                            _logger.LogInformation("Certificate is a CA trust cert. Proceeding with Add operation...");
                        }
                        else
                        {
                            _logger.LogWarning("Certificate is an end-entity cert. Unable to add this certificate type to a device.");
                            return new JobResult()
                            {
                                Result = OrchestratorJobStatusJobResult.Warning, 
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage = $"UNSUPPORTED OPERATION --- This certificate cannot be used as a Trust. Unable to add end-entity certificates to a device."
                            };
                        }

                        // Create client to connect to device
                        _logger.LogTrace("Creating Api HTTP Client...");
                        var client = new AxisHttpClient(config, config.CertificateStoreDetails, Resolver);
                        _logger.LogTrace("Api HTTP Client Created...");
                        
                        // Ignore the 'Overwrite' flag; Currently NOT supporting overwriting an existing CA cert with the same alias.
                        // The existing CA cert needs to be deleted first and then the new CA cert can be added with the same alias.
                        // Log warning if user attempts to add a CA cert with the same alias.
                        
                        _logger.LogInformation($"Overwrite flag = {overwrite} --- IGNORING");
                        // Perform CA cert inventory
                        _logger.LogTrace("Retrieve all CA certificates");
                        CACertificateData data1 = client.ListCACertificates();
                        
                        // Look for a CA cert with the same alias as the one requested
                        _logger.LogTrace($"Searching for an existing CA cert with alias '{alias}'...");
                        var existingCACert = data1.CACerts.FirstOrDefault(c => c.Alias == alias);
                        
                        if (null == existingCACert)
                        {
                            _logger.LogInformation($"Alias '{alias}' does not exist for any CA certificates. Proceeding with Add operation...");
                        }
                        else
                        {
                            _logger.LogWarning($"A CA certificate was found with the alias '{alias}'. Unable to add this certificate.");
                            return new JobResult()
                            {
                                Result = OrchestratorJobStatusJobResult.Warning, 
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage = $"ALIAS ALREADY EXISTS FOR CA CERTIFICATE --- Provide a new alias and resubmit."
                            };
                        }

                        // Build PEM content
                        string formattedDer = InsertLineBreaks(certBase64Der, 64);
                        _logger.LogDebug(($"Formatted certificate contents:\n{formattedDer}"));

                        StringBuilder pemBuilder = new StringBuilder();
                        pemBuilder.Append(@"-----BEGIN CERTIFICATE-----\n");
                        var noLineBreaks = formattedDer.Replace("\n", @"\n");
                        pemBuilder.Append(noLineBreaks);
                        pemBuilder.Append(@"\n-----END CERTIFICATE-----");
                        var pemCert = pemBuilder.ToString();

                        // Add certificate with alias to the device
                        client.AddCACertificate(alias, pemCert);

                        break;
                    }
                    case CertStoreOperationType.Remove:
                    {
                        _logger.LogInformation("Entered Management-Remove Operation");
                        
                        //OperationType == Remove - Delete a certificate from the certificate store passed in the config object
                        //Code logic to:
                        // 1) Connect to the orchestrated server (config.CertificateStoreDetails.ClientMachine) containing the certificate store
                        // 2) Custom logic to remove the certificate in a certificate store (config.CertificateStoreDetails.StorePath), possibly using alias (config.JobCertificate.Alias) or certificate thumbprint to identify the certificate (implementation dependent)

                        // Retrieve management config from Command
                        _logger.LogDebug($"Management Config {JsonConvert.SerializeObject(config)}");
                        _logger.LogDebug($"Client Machine: {config.CertificateStoreDetails.ClientMachine}");
                        
                        // Get needed information from config
                        string alias = config.JobCertificate.Alias;
                        string certBase64Der = config.JobCertificate.Contents;

                        // Prevent removal of client certs; Client certs may be removed as part of a future update
                        if (IsCACertificate(certBase64Der))
                        {
                            _logger.LogInformation("Certificate is a CA trust cert. Proceeding with Remove operation...");
                        }
                        else
                        {
                            _logger.LogWarning("Certificate is an end-entity cert. Unable to remove this certificate type from a device.");
                            return new JobResult()
                            {
                                Result = OrchestratorJobStatusJobResult.Warning, 
                                JobHistoryId = config.JobHistoryId,
                                FailureMessage = $"UNSUPPORTED OPERATION --- This certificate is an end-entity cert. Unable to remove end-entity certificates from a device."
                            };
                        }
                        
                        // Create client to connect to device
                        _logger.LogTrace("Creating Api HTTP Client...");
                        var client = new AxisHttpClient(config, config.CertificateStoreDetails, Resolver);
                        _logger.LogTrace("Api HTTP Client Created...");

                        // Remove certificate with alias from the device
                        client.RemoveCACertificate(alias);

                        break;
                    }
                    default:
                        //Invalid OperationType.  Return error.  Should never happen though
                        return new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, FailureMessage = $"Site {config.CertificateStoreDetails.StorePath} on server {config.CertificateStoreDetails.ClientMachine}: Unsupported operation: {config.OperationType.ToString()}" };
                }
            }
            catch (Exception ex)
            {
                //Status: 2=Success, 3=Warning, 4=Error
                return new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, FailureMessage = $"Management Job Failed During '{config.OperationType.ToString()}' Operation: {ex.Message} - Refer to logs for more detailed information." };
            }

            //Status: 2=Success, 3=Warning, 4=Error
            return new JobResult() { Result = OrchestratorJobStatusJobResult.Success, JobHistoryId = config.JobHistoryId };
        }
        
        /// <summary>
        /// Inserts line breaks every n-characters.
        /// </summary>
        /// <param name="input">String to break apart every n-lines</param>
        /// <param name="lineLength">Length of each line</param>
        /// <returns>Formatted string</returns>
        private static string InsertLineBreaks(string input, int lineLength)
        {
            int length = input.Length;
            int lines = (length + lineLength - 1) / lineLength; // Calculate the number of lines needed
            char[] result = new char[length + lines - 1]; // Extra space for new line characters

            int inputIndex = 0;
            int resultIndex = 0;

            for (int i = 0; i < lines; i++)
            {
                int remainingChars = Math.Min(lineLength, length - inputIndex);
                input.Substring(inputIndex, remainingChars).CopyTo(0, result, resultIndex, remainingChars);
                inputIndex += remainingChars;
                resultIndex += remainingChars;

                // Add a new line after every 64 characters except for the last line
                if (i < lines - 1)
                {
                    result[resultIndex++] = '\n';
                }
            }

            return new string(result);
        }

        /// <summary>
        /// ASSUMPTION: This function assumes a certificate to be an end entity certificate
        /// if the basic constraints extension is NOT present in a version 3 certificate.
        /// If the basic constraints extension IS present, it must have a value marked for 'CertificateAuthority'.
        /// FALLBACK: If this check produces a false positive, the Axis API will fail on the
        /// HTTP request and details will be logged accordingly.
        /// </summary>
        /// <param name="certBase64Der">Cert contents to add represented as Base-64 encoded DER</param>
        /// <returns>True if CA cert; False otherwise</returns>
        private static bool IsCACertificate(string certBase64Der)
        {
            // Convert the cert contents to a byte array
            byte[] certBytes = Convert.FromBase64String(certBase64Der);
                        
            // Create an X509 object so we can analyze the contents
            X509Certificate2 certToAdd = new X509Certificate2(certBytes);

            foreach (X509Extension ext in certToAdd.Extensions)
            {
                if (ext.Oid?.Value == "2.5.29.19") // Indicates if the subject may act as a CA, with public key used to verify cert signatures
                {
                    if (ext is X509BasicConstraintsExtension basicConstraints)
                    {
                        return basicConstraints.CertificateAuthority;
                    }
                }
            }

            return false;
        }
    }
}