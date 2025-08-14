#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;
using X509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Helpers
{
    public static class DeviceCertValidator
    {
        /// <summary>
        /// This method is a custom HTTP validator that performs the following logic:
        /// 1. Checks the TLS cert against the file of Axis-only cert chains
        /// a) If the cert chain is validated, we have an Axis device ID cert --- go ahead and validate the serial number
        /// aa) If the serial number provided for the 'Store Path' doesn't match the value provided for the "SERIALNUMBER"
        ///     attribute in the DN, deny the session
        /// ab) If the serial number does match, proceed with the session --- Return TRUE
        /// 2. If the TLS cert chain is NOT validated against the Axis-only cert chain, check the Trust Store and perform normal TLS validation
        /// </summary>
        public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> GetValidator(
            string expectedValue, CertificateErrorContext errorContext, ILogger logger)
        {
            return (message, cert, chain, sslPolicyErrors) =>
            {
                bool customChainValid = false;
                
                if (null == cert)
                {
                    errorContext.Add("SSL Cert is null");
                    return false;
                }

                if (null == chain)
                {
                    errorContext.Add("Server Cert Chain is null");
                    return false;
                }
                
                // VALIDATION 1: Verify the TLS cert chain against the AXIS PKI --- Did this cert come off an AXIS PKI?
                // This check will be done with SKI/AKI matching against the chain
                logger.LogTrace($"Performing Cert Validator Check #1: Verify the TLS cert chain against custom chain of AXIS PKI certs...");
                
                // Load custom trusted certs
                string trustedRootCertPath = "C:\\Program Files\\Keyfactor\\Keyfactor Orchestrator\\extensions\\AxisIPCamera\\Files\\Axis.Root";
                string trustedIntCertPath = "C:\\Program Files\\Keyfactor\\Keyfactor Orchestrator\\extensions\\AxisIPCamera\\Files\\Axis.Intermediate";

                X509CertificateParser parser = new X509CertificateParser();
                var customChain = new List<X509Certificate> { };
                
                // Add TLS cert as leaf certificate to the end of the custom chain
                customChain.Add(parser.ReadCertificate(cert.RawData));
                
                logger.LogTrace($"Loading Trusted Intermediate Certs from {trustedIntCertPath}");
                var trustedIntCerts = parser.ReadCertificates(File.ReadAllBytes(trustedIntCertPath));

                if (0 == trustedIntCerts.Count)
                {
                    logger.LogTrace("No Trusted Intermediate Certs found");
                    errorContext.Add($"No Trusted Intermediate Certs found at '{trustedIntCertPath}'");
                    return false;
                }

                // Add each intermediate cert to the end of the custom chain
                foreach (var trustedCert in trustedIntCerts)
                {
                    customChain.Add(trustedCert);
                }
                
                logger.LogTrace($"{trustedIntCerts.Count} Trusted Intermediate Certs found");
                
                logger.LogTrace($"Loading Trusted Root Cert from {trustedRootCertPath}");
                var trustedRootCerts = parser.ReadCertificates(File.ReadAllBytes(trustedRootCertPath));
                
                // Verify there is only 1 Root cert
                if (trustedRootCerts.Count > 1)
                {
                    logger.LogTrace("Custom Root trust must only contain 1 certificate");
                    errorContext.Add($"More than 1 certificate found at '{trustedRootCertPath}'");
                    return false;
                }
                else if (0 == trustedRootCerts.Count)
                {
                    logger.LogTrace("No Trusted Root Certs found");
                    errorContext.Add($"No Trusted Certs found at '{trustedRootCertPath}'");
                    return false;
                }
                
                // Add the root cert to the end of the custom chain
                customChain.Add(trustedRootCerts[0]);
                
                logger.LogTrace($"Attempting to verify the AKI/SKI values of the TLS cert against custom chain...");
                customChainValid = VerifyAkiSkiChain(customChain, logger);
                
                // VALIDATION 2: If the cert came off the AXIS PKI, we need to validate the SERIALNUMBER in the Subject DN
                // Otherwise, proceed with default SSL validation. We don't need to perform VALIDATION 2 if the cert did not come from the AXIS PKI.
                if (customChainValid)
                {
                    // Verify the SSL cert Subject DN contains the validating attribute and it matches the expected value
                    logger.LogTrace($"Cert chain is valid!");
                    logger.LogTrace($"Performing Cert Validator Check #2: Check the subject DN for attribute SERIALNUMBER and verify it matches the expected value...");
                    
                    var subjectDn = new X500DistinguishedName(cert.SubjectName);
                    var subjectString = subjectDn.Name;
                
                    logger.LogDebug($"Device ID cert Subject DN: {subjectString}");

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
                                errorContext.Add(
                                    $"SERIALNUMBER attribute value does NOT match the expected value '{expectedValue}'");
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
                        errorContext.Add("SERIALNUMBER attribute was not found in the certificate Subject DN");
                        return false;
                    }

                    return true;
                }

                // VALIDATION 3: Check for standard SSL errors
                logger.LogTrace($"Skipping Cert Validator Check #2: Check the subject for SERIALNUMBER and verify it matches the expected value...");
                logger.LogTrace($"Performing Cert Validator Check #3: Verify cert against default system validation...");
                bool sslErrors = false;
                if (sslPolicyErrors == SslPolicyErrors.None)
                {
                    logger.LogTrace("Certificate chain is valid.");
                }
                else if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                {
                    sslErrors = true;
                    errorContext.Add("The server did not provide a certificate.");
                }
                else if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                {
                    sslErrors = true;
                    errorContext.Add("The device hostname does not match the CN or SAN in the server's TLS certificate.");
                }
                else if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
                { 
                    sslErrors = true;
                    errorContext.Add("Certificate chain is NOT valid.");

                    if (!chain.Build(cert))
                    {
                        sslErrors = true;
                        errorContext.Add("Could not build the cert chain!");
                    }
                    
                    foreach (var status in chain.ChainStatus)
                    {
                        sslErrors = true;
                        errorContext.Add($"Chain status: {status.Status} - {status.StatusInformation}");
                    }
                }

                if (sslErrors)
                {
                    errorContext.Insert(0, "TLS Cert validation failed!!");
                    return false;
                }
                
                logger.LogInformation("Certificate chain and subject validated!!");
                return true;
            };
        }
        
        private static bool VerifyAkiSkiChain(List<X509Certificate> customChain, ILogger logger)
        {
            logger.MethodEntry();
            
            for (int i = 0; i < customChain.Count - 1; i++)
            {
                var childCert = customChain[i];
                var parentCert = customChain[i + 1];
                
                // Get the SKI from the parent cert
                var skiExt = parentCert.GetExtensionValue(X509Extensions.SubjectKeyIdentifier);
                var ski = SubjectKeyIdentifier.GetInstance(X509ExtensionUtilities.FromExtensionValue(skiExt))
                    .GetKeyIdentifier();
                logger.LogTrace($"Parent cert {parentCert.SubjectDN.ToString()} has SKI of '{BitConverter.ToString(ski).Replace("-", "").ToLowerInvariant()}'");
                
                // Get the AKI from the child cert
                var akiExt = childCert.GetExtensionValue(X509Extensions.AuthorityKeyIdentifier);
                var aki = AuthorityKeyIdentifier.GetInstance(X509ExtensionUtilities.FromExtensionValue(akiExt))
                    .GetKeyIdentifier();
                logger.LogTrace($"Child cert {childCert.SubjectDN.ToString()} has AKI of '{BitConverter.ToString(aki).Replace("-", "").ToLowerInvariant()}'");
                
                // Compare the SKI and AKI to ensure they match
                if (ski == null || aki == null || !aki.SequenceEqual(ski))
                {
                    logger.LogTrace($"Mismatch between parent cert SKI and child cert AKI!");
                    logger.MethodExit();
                    return false;
                }
            }

            logger.LogTrace($"SKIs and AKIs match up the custom cert chain");
            logger.MethodExit();
            return true;
        }
    }
}