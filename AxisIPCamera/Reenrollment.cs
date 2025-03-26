using System;
using System.Collections.Generic;
using System.Text;

using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;

using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera
{
    // The Reenrollment class implementes IAgentJobExtension and is meant to:
    //  1) Generate a new public/private keypair locally
    //  2) Generate a CSR from the keypair,
    //  3) Submit the CSR to KF Command to enroll the certificate and retrieve the certificate back
    //  4) Deploy the newly re-enrolled certificate to a certificate store
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
            // config.JobProperties = Dictionary of custom parameters to use in building CSR and placing enrolled certiciate in a the proper certificate store

            //NLog Logging to c:\CMS\Logs\CMS_Agent_Log.txt
            
            try
            {
                _logger.MethodEntry();
                _logger.LogDebug($"Begin Reenrollment...");
                //Code logic to:
                //  1) Generate a new public/private keypair locally from any config.JobProperties passed
                //  2) Generate a CSR from the keypair (PKCS10),
                //  3) Submit the CSR to KF Command to enroll the certificate using:
                //      string resp = (string)submitEnrollmentRequest.Invoke(Convert.ToBase64String(PKCS10_bytes);
                //      X509Certificate2 cert = new X509Certificate2(Convert.FromBase64String(resp));
                //  4) Deploy the newly re-enrolled certificate (cert in #3) to a certificate store
                
                _logger.LogDebug($"Reenrollment Config {JsonConvert.SerializeObject(config)}");
                _logger.LogDebug($"Client Machine: {config.CertificateStoreDetails.ClientMachine}");
                
                _logger.LogTrace("Creating Api Rest Client...");
                var client = new AxisRestClient(config, config.CertificateStoreDetails);
                _logger.LogTrace("Api Rest Client Created...");

                foreach (var itm in config.JobProperties)
                {
                    _logger.LogDebug($"{itm.Key}:{itm.Value}");
                }
                // Here are the job properties:
                // entropy
                // CertUsage
                // keyType
                // keySize
                // subjectText
                
                // Get required reenrollment fields
                // TODO: Add check for property so null exceptions aren't thrown
                string alias = config.Alias;
                string subject = config.JobProperties["subjectText"].ToString();
                // This is actually a property on the config bool overwrite = Convert.ToBoolean(config.JobProperties["Overwrite"]);
                string certUsage = config.JobProperties["CertUsage"].ToString();
                string keyAlgorithm = config.JobProperties["keyType"].ToString();
                string keySize = config.JobProperties["keySize"].ToString();
                
                _logger.LogDebug($"Alias: {alias}");
                
                // TODO: If there is a certificate that is already bound to the certUsage and overwrite it NOT checked, what do we do here?
                // 1) Throw an error, 2) Or just overwrite it by default, ignoring the override flag --- delete the previous one first
                
                // TODO: Add logic if the cert usage is a Trust Store, throw exception
                
                // Map the key type and key size from the job properties to a corresponding key type available on the device
                // TODO: Redo the key type mappings
                var keyType = Constants.MapKeyType(keyAlgorithm, keySize);
                _logger.LogDebug($"Mapped Key Type: {keyType}");
                if (keyType == "UNKNOWN")
                {
                    throw new Exception(
                        $"The key algorithm '{keyAlgorithm}' and key size '{keySize}' selected for reenrollment do not correspond to a valid key algorithm and" +
                        $"key size on the device.");
                }
                
                // TODO: Should we do a delete of original cert here?
                
                // Get the default keystore
                var defaultKeystore = client.GetDefaultKeystore();
                string defaultKeystoreString = Enum.GetName(typeof(Constants.Keystore), defaultKeystore);
                //string keytypeString = Enum.GetName(typeof(Constants.KeyTypes), keyType);
                _logger.LogDebug($"Default keystore: {defaultKeystoreString}");
                _logger.LogDebug($"Assigned key type: {keyType}");
                
                // TODO: Add logic to create different self-signed cert for non-HTTP cert usage
                // Generate self-signed cert with private key on the device (*Include SANs)
                string[] sans = new[] { "DNS:http.kfdemo.com" }; // TODO: Update this to read from Command
                client.CreateSelfSignedCert(alias,keyType,defaultKeystoreString,subject,sans);
                
                // Obtain CSR using self-signed cert
                var csr = client.ObtainCSR(alias);
                _logger.LogDebug($"CSR: \n{csr}");
                
                // TODO: Validate the contents of the CSR
                
                // Submit CSR to be signed in Keyfactor
                var x509Cert = submitReenrollment.Invoke(csr);

                // TODO: Validate certificate returned from Keyfactor
                
                // Build PEM content
                StringBuilder pemBuilder = new StringBuilder();
                pemBuilder.Append(@"-----BEGIN CERTIFICATE-----\n");
                string s = Convert.ToBase64String(x509Cert.RawData, Base64FormattingOptions.InsertLineBreaks);
                var noLineBreaks = s.Replace(Environment.NewLine,@"\n");
                pemBuilder.Append(noLineBreaks);
                pemBuilder.Append(@"\n-----END CERTIFICATE-----");
                var pemCert = pemBuilder.ToString();

                //pemCert = pemCert.Replace("\r", @"\n");
                
                
                // Replace existing certificate on device
                _logger.LogTrace($"Replacing cert '{alias}' with the following cert: " + pemCert);
                
                client.ReplaceCertificate(alias,pemCert);
                
                // Update the binding on the camera
                // TODO: Add logic to change this based on the binding type
                client.SetCertUsageBinding(alias, Constants.CertificateUsage.Https);


                // Send the binding information back to Command


            }
            catch (Exception ex)
            {
                //Status: 2=Success, 3=Warning, 4=Error
                return new JobResult() { Result = Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Failure, JobHistoryId = config.JobHistoryId, FailureMessage = ex.Message };
            }

            //Status: 2=Success, 3=Warning, 4=Error
            return new JobResult() { Result = Keyfactor.Orchestrators.Common.Enums.OrchestratorJobStatusJobResult.Success, JobHistoryId = config.JobHistoryId };
        }
    }
}