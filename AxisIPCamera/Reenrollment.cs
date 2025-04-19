using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Orchestrators.Common.Enums;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera
{
    public class Reenrollment : IReenrollmentJobExtension
    {
        private readonly ILogger _logger;
        public Reenrollment()
        {
            _logger = LogHandler.GetClassLogger<Reenrollment>();
        }
        
        //Necessary to implement IReenrollmentJobExtension but not used.  Leave as empty string.
        public string ExtensionName => "";

        //Job Entry Point
        public JobResult ProcessJob(ReenrollmentJobConfiguration config, SubmitReenrollmentCSR submitReenrollment)
        {
            var backupFlag = false;
            
            try
            {
                _logger.MethodEntry();
                
                _logger.LogTrace($"Begin Reenrollment for Client Machine {config.CertificateStoreDetails.ClientMachine}");
                _logger.LogDebug($"Reenrollment Config: {JsonConvert.SerializeObject(config)}");
                _logger.LogDebug($"Client Machine: {config.CertificateStoreDetails.ClientMachine}");

                // Log each key-value pair in the Job Properties for debugging
                _logger.LogDebug("Begin Job Properties:");
                foreach (var itm in config.JobProperties)
                {
                    _logger.LogDebug($"{itm.Key}:{itm.Value}");
                }
                _logger.LogDebug("End Job Properties");
                
                // Get required reenrollment fields
                string certUsage = config.JobProperties[Constants.CertUsageParamName].ToString() ?? throw new Exception($"{Constants.CertUsageParamDisplay} returned null");
                var certUsageEnum = Constants.GetCertUsageAsEnum(certUsage);
                string keyAlgorithm = config.JobProperties["keyType"].ToString() ?? throw new Exception("Key Algorithm returned null");
                string keySize = config.JobProperties["keySize"].ToString() ?? throw new Exception("Key Size returned null");
                string subject = config.JobProperties["subjectText"].ToString() ?? throw new Exception("Subject returned null");
                // IGNORING --- bool overwrite = Convert.ToBoolean(config.JobProperties["Overwrite"]);
                
                string reenrollAlias = config.Alias ?? throw new Exception("Alias returned null");
                _logger.LogDebug($"Alias: {reenrollAlias}");
                
                // Prevent reenrollment on Trust certificates
                if (certUsageEnum == Constants.CertificateUsage.Trust)
                {
                    throw new Exception(
                        "Reenrollment cannot be performed on a store when the certificate usage is marked as 'Trust'");
                }
                
                _logger.LogTrace("Create HTTPS client to connect to device");
                var client = new AxisHttpClient(config, config.CertificateStoreDetails);

                // Get current binding for reenrollment certificate usage provided
                _logger.LogTrace($"Check {certUsage} binding for same alias");
                var boundAlias = client.GetCertUsageBinding(Constants.GetCertUsageAsEnum(certUsage));
                if (!string.IsNullOrEmpty(boundAlias))
                {
                    _logger.LogDebug($"Alias currently bound to certificate usage type {certUsage}: {boundAlias}");
                    
                    if (boundAlias == reenrollAlias)
                    {
                        _logger.LogDebug($"Alias '{reenrollAlias}' provided for reenrollment matches alias '{boundAlias}' currently bound " +
                                         $"to certificate usage type {certUsage}");
                        backupFlag = true;
                        
                        // TODO - If possible: Create backup of the existing certificate
                        // For now, throw an exception
                        throw new Exception(
                            $"Alias '{reenrollAlias}' already exists for certificate usage type {certUsage}. Reenroll using another alias.");
                    }

                    _logger.LogTrace($"Alias '{reenrollAlias}' provided for reenrollment differs from alias '{boundAlias}' currently bound " +
                                     $"to certificate usage type {certUsage}. Proceeding...");
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
                        $"The key algorithm '{keyAlgorithm}' and key size '{keySize}' selected for reenrollment do not correspond to a valid key algorithm and" +
                        $"key size on the device.");
                }
                
                // Get the default keystore
                _logger.LogTrace("Retrieve the default keystore");
                Constants.Keystore defaultKeystore = client.GetDefaultKeystore();
                string defaultKeystoreString = Enum.GetName(typeof(Constants.Keystore), defaultKeystore);
                _logger.LogDebug($"Reenrollment - Default keystore: {defaultKeystoreString}");
                
                _logger.LogTrace("Generating self-signed cert with private key on device");
                List<string> sansList = new List<string>();
                if (certUsageEnum == Constants.CertificateUsage.Https)
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
                    
                    // Extract the IP address from the Client Machine
                    var ipMatch = Regex.Match(config.CertificateStoreDetails.ClientMachine,
                        @"^(?<ip>(?:\d{1,3}\.){3}\d{1,3})", RegexOptions.IgnoreCase);
                    if (!ipMatch.Success)
                    {
                        _logger.LogTrace("Value provided for the Client Machine does not match IPv4 format.");
                        throw new Exception(
                            "Value provided for the Client Machine does not match IPv4 format.");
                    }

                    sansList.Add("DNS:" + cnMatch.Groups[1].Value);
                    sansList.Add("IP:" + ipMatch.Groups["ip"].Value);
                }
                client.CreateSelfSignedCert(reenrollAlias,keyType,defaultKeystoreString,subject,sansList.ToArray());
                
                _logger.LogTrace("Obtaining CSR using self-signed certificate");
                var csr = client.ObtainCSR(reenrollAlias);
                _logger.LogDebug($"CSR: \n{csr}");

                _logger.LogTrace("Validating CSR");
                Constants.ValidateCsr(csr);
                _logger.LogTrace("CSR is valid");
                
                // Submit CSR to be signed in Keyfactor
                _logger.LogTrace("Submitting CSR to be signed in Command");
                var x509Cert = submitReenrollment.Invoke(csr);

                /* TODO: This isn't working --- determine how to fix this, if necessary
                 _logger.LogTrace("Validating certificate");
                Constants.ValidateCertificate(x509Cert);
                _logger.LogTrace("Certificate is valid");
                */
                
                // Build PEM content
                // **NOTE: The static newline (\n) characters are required in the API request
                StringBuilder pemBuilder = new StringBuilder();
                pemBuilder.Append(@"-----BEGIN CERTIFICATE-----\n");
                string s = Convert.ToBase64String(x509Cert.RawData, Base64FormattingOptions.InsertLineBreaks);
                var noLineBreaks = s.Replace(Environment.NewLine,@"\n");
                pemBuilder.Append(noLineBreaks);
                pemBuilder.Append(@"\n-----END CERTIFICATE-----");
                var pemCert = pemBuilder.ToString();

                _logger.LogTrace($"Replacing self-signed cert '{reenrollAlias}' with the following cert: " + pemCert);
                client.ReplaceCertificate(reenrollAlias,pemCert);
                
                _logger.LogTrace($"Setting '{certUsage}' binding to alias '{reenrollAlias}'");
                client.SetCertUsageBinding(reenrollAlias, certUsageEnum);

                // TODO: Should we do a delete of original cert here?
                
            }
            catch (Exception ex)
            {
                //Status: 2=Success, 3=Warning, 4=Error
                return new JobResult() { Result = Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, FailureMessage = $"Reenrollment Job Failed: {ex.Message}" };
            }

            //Status: 2=Success, 3=Warning, 4=Error
            return new JobResult() { Result = Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Success, JobHistoryId = config.JobHistoryId };
        }
    }
}