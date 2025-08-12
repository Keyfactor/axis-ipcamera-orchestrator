#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Security;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client
{
    public static class DeviceCertValidator
    {
        /// <summary>
        /// This method is a custom HTTP validator that performs the following logic:
        /// 1. Checks the SSL cert against the file of Axis-only cert chains
        /// a) If the cert chain is validated, we have an Axis device ID cert --- go ahead and validate the serial number
        /// aa) If the serial number provided for the 'Store Path' doesn't match the value provided for the "SERIALNUMBER"
        ///     attribute in the DN, deny the session
        /// ab) If the serial number does match, proceed with the session --- Return TRUE
        /// 2. If the SSL cert chain is NOT validated against the Axis-only cert chain, check the Trust Store and perform normal SSL validation
        /// </summary>
        public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> GetValidator(
            string expectedValue, ILogger logger)
        {
            return (message, cert, chain, sslPolicyErrors) =>
            {
                bool customChainValid = false;
                bool subjectErrors = false;
                bool sslErrors = false;
                StringBuilder sslMessage = new StringBuilder();
                StringBuilder subjectMessage = new StringBuilder();
                
                try
                {
                    var customChain = new X509Chain();
                    customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    
                    // VALIDATION 1: Verify the cert chain against the AXIS PKI --- Did this cert come off an AXIS PKI?
                    logger.LogTrace($"Performing Cert Validator Check #1: Verify the cert chain against a custom chain of AXIS PKI certs...");
                    
                    // Load custom trusted certs
                    var trustedCerts =
                        LoadTrustedCertsFromFile(
                            "C:\\Program Files\\Keyfactor\\Keyfactor Orchestrator\\extensions\\AxisIPCamera\\Files\\Axis.Trust");
                    foreach (var tCert in trustedCerts)
                    {
                        customChain.ChainPolicy.ExtraStore.Add(tCert);
                    }
                    
                    // Disable the system root trust --- This is necessary because .NET expects the chain to terminate 
                    // at a trusted root in the OS store
                    customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                    if (null == cert)
                    {
                        logger.LogError("SSL Cert is null");
                        return false;
                    }
                    
                    // Attempt to build the SSL cert against the custom trust chain
                    customChainValid = customChain.Build(cert);
                    
                    // VALIDATION 2: If the cert came off the AXIS PKI, we need to validate the SERIALNUMBER in the Subject DN
                    // Otherwise, proceed with default SSL validation. We don't need to perform VALIDATION 2 if the cert did not come from the AXIS PKI.
                    if (customChainValid)
                    {
                        // Verify the SSL cert Subject DN contains the validating attribute and it matches the expected value
                        logger.LogInformation($"Cert chain is valid!");
                        logger.LogTrace($"Performing Cert Validator Check #2: Check the subject for SERIALNUMBER and verify it matches the expected value...");
                        
                        var subjectDn = new X500DistinguishedName(cert.SubjectName);
                        var subjectString = subjectDn.Name;
                    
                        logger.LogTrace($"Device ID cert Subject DN: {subjectString}");

                        var decodedSubject = subjectDn.Decode(X500DistinguishedNameFlags.UseNewLines);
                        var subjectLines = decodedSubject.Split('\n');

                        bool foundAttribute = false;
                        foreach (var line in subjectLines)
                        {
                            if (line.StartsWith("SERIALNUMBER="))
                            {
                                foundAttribute = true;
                                var snValue = line.Substring(13).Trim();
                                logger.LogDebug($"Found SERIALNUMBER: {snValue}");

                                if (snValue != expectedValue)
                                {
                                    subjectErrors = true;
                                    logger.LogTrace($"SERIALNUMBER attribute value does NOT match the expected value '{expectedValue}'");
                                    subjectMessage.Append(
                                        $"SERIALNUMBER attribute value does not match the expected value '{expectedValue}'");
                                    return false;
                                }
                                else
                                {
                                    logger.LogTrace($"SERIALNUMBER attribute value matches expected value! Proceed...");
                                    return true;
                                }
                            }
                        }

                        if (!foundAttribute)
                        {
                            logger.LogTrace("SERIALNUMBER attribute was not found in the certificate Subject DN");
                            subjectMessage.Append("SERIALNUMBER attribute was not found in the certificate Subject DN");
                        }
                        else
                        {
                            // At this point, we have found the SERIALNUMBER attribute in the Subject DN and it matches
                            if (subjectErrors)
                            {
                                // IGNORE THE NAME MISMATCH
                                return true;
                            }
                        }
                        

                    }
                    else
                    {
                        // VALIDATION 3: Check for standard SSL errors
                        logger.LogInformation($"Performing Cert Validator Check #3: Verify cert against default system validation...");
                        if (sslPolicyErrors == SslPolicyErrors.None)
                        {
                            sslErrors = false;
                            sslMessage.AppendLine("* Certificate chain is valid.");
                        }
                        else if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                        {
                            sslErrors = true;
                            sslMessage.AppendLine("* The server did not provide a certificate.");
                        }
                        else if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                        {
                            sslErrors = true;
                            sslMessage.AppendLine("* The certificate name does not match the host name.");
                        }
                        else if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                        { 
                            sslErrors = true;
                            sslMessage.AppendLine("* Certificate chain is NOT valid.");

                            if (!chain.Build(cert))
                            {
                                sslMessage.AppendLine("Could not build the cert chain!");
                            }
                        
                            foreach (var status in chain.ChainStatus)
                            {
                                sslMessage.AppendLine($"Chain status: {status.Status} - {status.StatusInformation}");
                            }
                        }
                    }
                    
                    if (subjectErrors)
                    {
                        //throw new CertificateSubjectValidationException(subjectMessage.ToString());
                        return false;
                    }
                    
                    
                    if (sslErrors)
                    {
                        //throw new CertificateSslException(sslMessage.ToString());
                        return false;
                    }
                    
                    logger.LogInformation("Certificate chain and subject validated.");
                    return true;
                }
                catch (Exception e)
                {
                    throw new Exception($"Custom HTTP Validation failed! - Message: {e.Message}");
                }
            };
        }
        
        private static List<X509Certificate2> LoadTrustedCertsFromFile(string path)
        {
            var certs = new List<X509Certificate2>();
            var pem = File.ReadAllText(path);

            var matches = Regex.Matches(pem, "-----BEGIN CERTIFICATE-----(.*?)-----END CERTIFICATE-----", RegexOptions.Singleline);
            foreach (Match match in matches)
            {
                var base64 = match.Groups[1].Value.Replace("\r", "").Replace("\n", "");
                var rawData = Convert.FromBase64String(base64);
                certs.Add(new X509Certificate2(rawData));
            }

            return certs;
        }
    }

    public class CertificateSslException : SecurityException
    {
        public CertificateSslException(string message) : base(message) { }
    }
    
    public class CertificateSubjectValidationException : SecurityException
    {
        public CertificateSubjectValidationException(string message) : base(message) { }
    }
}