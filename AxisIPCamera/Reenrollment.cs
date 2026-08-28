// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using Keyfactor.Logging;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Helpers;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;
using Keyfactor.Orchestrators.Common.Enums;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Extensions.Interfaces;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera
{
    public class Reenrollment : IReenrollmentJobExtension
    {
        private readonly ILogger _logger;
        
        private readonly IPAMSecretResolver _resolver; 
        public string ExtensionName => "";
        
        public Reenrollment(IPAMSecretResolver resolver)
        {
            _logger = LogHandler.GetClassLogger<Reenrollment>();
            _resolver = resolver;
        }

        // Job Entry Point
        public JobResult ProcessJob(ReenrollmentJobConfiguration config, SubmitReenrollmentCSR submitReenrollment)
        {
            try
            {
                _logger.MethodEntry();
                
                _logger.LogTrace($"Begin Reenrollment for Client Machine {config.CertificateStoreDetails.ClientMachine}");
                string jsonConfig = JsonConvert.SerializeObject(config, Formatting.Indented);
                _logger.LogDebug($"Reenrollment Config: {jsonConfig.Replace(config.ServerPassword,"**********")}");

                // Log each key-value pair in the Job Properties for debugging
                _logger.LogDebug("Begin Job Properties ---");
                foreach (var itm in config.JobProperties)
                {
                    _logger.LogDebug($"{itm.Key}:{itm.Value}");
                }
                _logger.LogDebug("--- End Job Properties");
                
                // Log each SAN, if provided
                _logger.LogDebug("Begin SANs ---");
                var formattedSANs = SANBuilder.BuildSANList(config.SANs,_logger);
                if (formattedSANs.Count == 0)
                {
                    _logger.LogDebug($"No SAN values found.");
                }
                else
                {
                    foreach (var san in formattedSANs)
                    {
                        _logger.LogDebug($"{san}");
                    }   
                }
                _logger.LogDebug("--- End SANs");
                
                // Get required reenrollment fields
                string certUsage = config.JobProperties[Constants.CertUsageParamName].ToString() ?? throw new Exception($"{Constants.CertUsageParamDisplay} returned null");
                var certUsageEnum = Constants.GetCertUsageAsEnum(certUsage);
                string keyAlgorithm = config.JobProperties["keyType"].ToString() ?? throw new Exception("Key Algorithm returned null");
                string keySize = config.JobProperties["keySize"].ToString() ?? throw new Exception("Key Size returned null");
                string subject = config.JobProperties["subjectText"].ToString() ?? throw new Exception("Subject returned null");
                string newAlias = config.Alias ?? throw new Exception("Alias returned null");
                _logger.LogDebug($"Alias: {newAlias}");
                
                // Prevent reenrollment on Trust certificates
                if (certUsageEnum is Constants.CertificateUsage.Trust)
                {
                    throw new Exception(
                        "Reenrollment cannot be performed on a store when the certificate usage is marked as 'Trust' or 'Other'");
                }
                
                _logger.LogTrace("Create HTTPS client to connect to device");
                var client = new AxisHttpClient(config, config.CertificateStoreDetails, _resolver);

                // Get the existing alias name associated with the supplied cert usage
                _logger.LogTrace($"Check '{certUsage}' binding for same alias");
                var oldAlias = client.GetCertUsageBinding(Constants.GetCertUsageAsEnum(certUsage));
                var oldCertExists = false;
                if (!string.IsNullOrEmpty(oldAlias))
                {
                    oldCertExists = true;
                    _logger.LogDebug($"Alias currently bound to certificate usage type '{certUsage}': {oldAlias}");
                    
                    // compare the old alias name with the new alias name ---
                    // 1) if the names are the same, append a reserved time-based suffix to the end of the name
                    // This new name [AliasA_Timestamp] will be used to create the new cert.
                    // OR
                    // 2) EDGE CASE: if the old alias name currently tied to the cert usage does NOT match the new alias name,
                    // also create a new name [CertB_Timestamp] for the new cert in case the user-supplied cert name is already
                    // associated with an existing certificate that is NOT bound to a cert usage
                    newAlias = CertificateName.CreateUniqueCertName(newAlias);
                }
                else
                {
                    _logger.LogDebug($"No alias currently bound to certificate usage type {certUsage}. Proceeding with new key, CSR, and adding cert for new alias...");
                }

                // Map the key type and key size from the job properties to a corresponding key type available on the device
                _logger.LogTrace($"Mapping key type and key size from job properties to a corresponding key type available on the device: '{keyAlgorithm}' '{keySize}'");
                string keyType = Constants.MapKeyType(keyAlgorithm, keySize);
                _logger.LogDebug($"Mapped Key Type: {keyType}");
                if (keyType == "UNKNOWN")
                {
                    throw new Exception(
                        $"The key algorithm '{keyAlgorithm}' and key size '{keySize}' selected for reenrollment " +
                        $"do not correspond to a valid key algorithm and " +
                        $"key size on the device.");
                }
                
                // Get the default keystore
                _logger.LogTrace("Retrieve the default keystore");
                Constants.Keystore defaultKeystore = client.GetDefaultKeystore();
                string defaultKeystoreString = defaultKeystore.ToString();
                _logger.LogDebug($"Reenrollment - Default keystore: {defaultKeystoreString}");

                // If no SANs are provided and the cert usage is 'HTTPS' ---
                // Add 1 for DNS and 1 for IP address to eliminate TLS errors
                if(formattedSANs.Count == 0 && certUsageEnum == Constants.CertificateUsage.Https)
                {
                    _logger.LogTrace("Extracting CN and IP address to add as SANs to the certificate");
                    // Extract the CN from the Subject
                    var cnMatch = Regex.Match(subject, @"CN=([^,]+)", RegexOptions.IgnoreCase);
                    if (!cnMatch.Success)
                    {
                        _logger.LogTrace("No value provided in the Subject for 'CN'.");
                        throw new Exception(
                            "No value provided in the Subject for 'CN'. This is required for HTTPS certificates.");
                    }

                    _logger.LogTrace($"Extracted CN attribute from the Subject: {cnMatch.Groups[1].Value}");

                    // Extract the IP address from the Client Machine
                    var ipMatch = Regex.Match(config.CertificateStoreDetails.ClientMachine,
                        @"^(?<ip>(?:\d{1,3}\.){3}\d{1,3})", RegexOptions.IgnoreCase);
                    if (!ipMatch.Success)
                    {
                        _logger.LogTrace("Value provided for the Client Machine does not match IPv4 format.");
                        throw new Exception(
                            "Value provided for the Client Machine does not match IPv4 format.");
                    }

                    _logger.LogTrace($"Extracted IP Address from the Client Machine: {ipMatch.Groups["ip"].Value}");

                    formattedSANs.Add($"DNS:{cnMatch.Groups[1].Value}");
                    formattedSANs.Add($"IP:{ipMatch.Groups["ip"].Value}");
                }
                
                _logger.LogTrace("Generating private key pair on device");
                client.CreateSelfSignedCert(newAlias,keyType,defaultKeystoreString,subject,formattedSANs.ToArray());
                
                _logger.LogTrace("Obtaining CSR");
                var csr = client.ObtainCSR(newAlias);
                _logger.LogDebug($"CSR: \n{csr}");
                
                _logger.LogTrace("Validating CSR");
                Constants.ValidateCsr(csr);
                _logger.LogTrace("CSR is valid");
                
                // Submit CSR to be signed
                _logger.LogTrace("Submitting CSR to Command to enroll for signed certificate");
                var x509Cert = submitReenrollment.Invoke(csr);
                
                // Build PEM content
                // ** NOTE: The static newline (\n) characters are required in the API request
                StringBuilder pemBuilder = new StringBuilder();
                pemBuilder.Append(@"-----BEGIN CERTIFICATE-----\n");
                string s = Convert.ToBase64String(x509Cert.RawData, Base64FormattingOptions.InsertLineBreaks);
                var noLineBreaks = s.Replace(Environment.NewLine,@"\n");
                pemBuilder.Append(noLineBreaks);
                pemBuilder.Append(@"\n-----END CERTIFICATE-----");
                var pemCert = pemBuilder.ToString();
                    
                _logger.LogTrace($"Replacing cert '{newAlias}' with the following cert: " + pemCert);
                client.ReplaceCertificate(newAlias,pemCert);
                    
                _logger.LogTrace($"Setting '{certUsage}' binding to alias '{newAlias}'");
                client.SetCertUsageBinding(newAlias,certUsageEnum);
                    
                // Perform unused certificate cleanup --- 
                // 1) If a bound alias exists, delete the bound alias
                if (oldCertExists)
                {
                    _logger.LogTrace($"Removing certificate and private key associated with alias '{oldAlias}'");
                    var result = client.RemoveCertificate(oldAlias);

                    if (result.Status == HttpStatus.Warning)
                    {
                        return new JobResult() { Result = OrchestratorJobStatusJobResult.Warning, JobHistoryId = config.JobHistoryId, 
                            FailureMessage = $"Reenrollment Job Had Warnings - Refer to logs for more detailed information." };
                    }
                }
            }
            catch (Exception ex)
            {
                //Status: 2=Success, 3=Warning, 4=Error
                _logger.LogError($"Reenrollment Job Failed: {ex.Message}");
                return new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, 
                    FailureMessage = $"Reenrollment Job Failed: {ex.Message} - Refer to logs for more detailed information." };
            }

            //Status: 2=Success, 3=Warning, 4=Error
            return new JobResult() { Result = OrchestratorJobStatusJobResult.Success, JobHistoryId = config.JobHistoryId };
        }
    }
}