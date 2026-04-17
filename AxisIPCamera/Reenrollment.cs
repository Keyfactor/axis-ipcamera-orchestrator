// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using System.Net.Http;
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
using Keyfactor.PKI.Enums;
using Keyfactor.PKI.X509;
//using Org.BouncyCastle.X509;

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
                string jsonConfig = JsonConvert.SerializeObject(config);
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
                var formattedSANs = SANBuilder.BuildSANString(config.SANs,_logger);
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
                string reenrollAlias = config.Alias ?? throw new Exception("Alias returned null");
                _logger.LogDebug($"Alias: {reenrollAlias}");
                
                // Prevent reenrollment on Trust certificates
                if (certUsageEnum is Constants.CertificateUsage.Trust)
                {
                    throw new Exception(
                        "Reenrollment cannot be performed on a store when the certificate usage is marked as 'Trust' or 'None'");
                }
                
                _logger.LogTrace("Create HTTPS client to connect to device");
                var client = new AxisHttpClient(config, config.CertificateStoreDetails, _resolver);

                // Get current binding for reenrollment certificate usage provided
                _logger.LogTrace($"Check '{certUsage}' binding for same alias");
                var boundAlias = client.GetCertUsageBinding(Constants.GetCertUsageAsEnum(certUsage));
                var replaceCert = false;
                if (!string.IsNullOrEmpty(boundAlias))
                {
                    _logger.LogDebug($"Alias currently bound to certificate usage type '{certUsage}': {boundAlias}");
                    
                    if (boundAlias == reenrollAlias)
                    {
                        _logger.LogDebug($"Alias '{reenrollAlias}' provided for reenrollment matches alias '{boundAlias}' currently bound " +
                                         $"to certificate usage type {certUsage}. Proceeding with rekeying, CSR, and replacing cert for alias...");
                        replaceCert = true;
                    }
                    else
                    {
                        _logger.LogTrace($"Alias '{reenrollAlias}' provided for reenrollment differs from alias '{boundAlias}' currently bound " +
                                         $"to certificate usage type {certUsage}. Proceeding with new key, CSR, and adding cert for alias...");
                    }
                }
                else
                {
                    _logger.LogDebug($"No alias currently bound to certificate usage type {certUsage}");
                }

                // Map the key type and key size from the job properties to a corresponding key type available on the device
                string keyType = Constants.MapKeyType(keyAlgorithm, keySize);
                _logger.LogDebug($"Mapped Key Type: {keyType}");
                if (keyType == "UNKNOWN")
                {
                    throw new Exception(
                        $"The key algorithm '{keyAlgorithm}' and key size '{keySize}' selected for reenrollment " +
                        $"do not correspond to a valid key algorithm and" +
                        $"key size on the device.");
                }
                
                // Get the default keystore
                _logger.LogTrace("Retrieve the default keystore");
                Constants.Keystore defaultKeystore = client.GetDefaultKeystore();
                string defaultKeystoreString = defaultKeystore.ToString();
                _logger.LogDebug($"Reenrollment - Default keystore: {defaultKeystoreString}");

                // If no SANs are provided and the cert usage is 'HTTPS' and this is a new alias ---
                // Add 1 for DNS and 1 for IP address to eliminate TLS errors
                if(formattedSANs.Count == 0 && certUsageEnum == Constants.CertificateUsage.Https && !replaceCert)
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

                    formattedSANs.Add($@"""DNS:{cnMatch.Groups[1].Value}""");
                    formattedSANs.Add($@"""IP:{ipMatch.Groups["ip"].Value}""");
                }

                if (!replaceCert)
                {
                    _logger.LogTrace("Generating self-signed cert with private key on device");
                    client.CreateSelfSignedCert(reenrollAlias,keyType,defaultKeystoreString,subject);
                }
                
                _logger.LogTrace("Obtaining CSR");
                var csr = client.ObtainCSR(reenrollAlias,subject,formattedSANs);
                _logger.LogDebug($"CSR: \n{csr}");

                _logger.LogTrace("Validating CSR");
                Constants.ValidateCsr(csr);
                _logger.LogTrace("CSR is valid");
                
                // Submit CSR to be signed in Keyfactor
                _logger.LogTrace("Submitting CSR to be signed in Command");
                var x509Cert = submitReenrollment.Invoke(csr);
                
                // TESTING build chain functionality
                /*using var aiaClient = new HttpClient();
                var builder = new ChainBuilder(aiaClient);
                var bcX509Cert = new X509CertificateParser().ReadCertificate(x509Cert.RawData);
                var chain = builder.BuildChain(bcX509Cert, CertificateCollectionOrder.EndEntityFirst);

                int i = 0;
                foreach (var cert in chain.Certificates)
                {
                    i++;
                    _logger.LogTrace($"Cert {i}: {cert.SubjectDN.ToString()}");
                }*/

                // Build PEM content
                // ** NOTE: The static newline (\n) characters are required in the API request
                StringBuilder pemBuilder = new StringBuilder();
                pemBuilder.Append(@"-----BEGIN CERTIFICATE-----\n");
                string s = Convert.ToBase64String(x509Cert.RawData, Base64FormattingOptions.InsertLineBreaks);
                var noLineBreaks = s.Replace(Environment.NewLine,@"\n");
                pemBuilder.Append(noLineBreaks);
                pemBuilder.Append(@"\n-----END CERTIFICATE-----");
                var pemCert = pemBuilder.ToString();

                _logger.LogTrace($"Replacing cert '{reenrollAlias}' with the following cert: " + pemCert);
                client.ReplaceCertificate(reenrollAlias,pemCert);
                
                _logger.LogTrace($"Setting '{certUsage}' binding to alias '{reenrollAlias}'");
                client.SetCertUsageBinding(reenrollAlias, certUsageEnum);
            }
            catch (Exception ex)
            {
                //Status: 2=Success, 3=Warning, 4=Error
                return new JobResult() { Result = OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, 
                    FailureMessage = $"Reenrollment Job Failed: {ex.Message} - Refer to logs for more detailed information." };
            }

            //Status: 2=Success, 3=Warning, 4=Error
            return new JobResult() { Result = OrchestratorJobStatusJobResult.Success, JobHistoryId = config.JobHistoryId };
        }
    }
}