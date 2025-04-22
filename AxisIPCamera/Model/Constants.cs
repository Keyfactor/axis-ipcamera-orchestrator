using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Crypto;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model
{
    public static class Constants
    {
        // This is the API entry point for the REST VAPIX Cert Management API
        public static string RestApiEntryPoint = "/config/rest/cert/v1beta";

        // This is the API entry point for the SOAP Cert Management API
        public static string SoapApiEntryPoint = "/vapix/services";

        // This is the API entry point for the CGI Client API
        public static string CgiApiEntryPoint = "/axis-cgi/mqtt/client.cgi";
        
        // This is the Name of the Entry Parameter used to identify the certificate usage for each certificate on the camera
        public static string CertUsageParamName = "CertUsage";
        public static string CertUsageParamDisplay = "Certificate Usage";
        
        // This is the XML tag identifier for the cert alias bound to the TLS web server on the camera
        public static string HttpsAliasTagName = "acert:Id";
        
        // This is the XML tag identifier for the cert alias bound to the IEEE802.X network access control on the camera
        public static string IEEEAliasTagName = "tt:CertificateID";
        
        // This is the JSON key identifier for the cert alias bound to the MQTT over SSL on the camera
        public static string MQTTAliasKeyName = "clientCertID";
        
        public enum CertificateUsage
        {
            Https,
            IEEE,
            MQTT,
            Trust,
            None,
            Undefined
        }

        // Note: There may be more keystore types depending on the Axis camera model
        public enum Keystore
        {
            TEE0, // Trusted Environment
            SE0 // Secure Element
        }

        public enum ApiType
        {
            Rest, // VAPIX Certificate Management API (Used for Cert Management)
            Soap, // Certificate management API (Used for HTTP and IEEE cert bindings)
            Cgi // MQTT Client API (Used for MQTT bindings)
        }
        
        public enum Status
        {
            Success,
            Error
        }
        
        /// <summary>
        /// Maps the Keyfactor Command-provided key algorithm and size to string representation
        /// the device can interpret.
        /// **NOTE: These values may need updated depending on the target camera OS.
        /// </summary>
        /// <param name="keyAlgorithm">i.e. RSA, ECP</param>
        /// <param name="keySize">i.e. 2048, 256</param>
        /// <returns>String representation of the [key algorithm]-[key size] the device API can interpret</returns>
        public static string MapKeyType(string keyAlgorithm, string keySize)
        {
            return keyAlgorithm switch
            {
                "RSA" when keySize == "2048" => "RSA-2048",
                "RSA" when keySize == "4096" => "RSA-4096",
                "ECP" when keySize == "256" => "EC-P256",
                "ECP" when keySize == "384" => "EC-P384",
                "ECP" when keySize == "521" => "EC-P521",
                _ => "UNKNOWN"
            };
        }
        
        /// <summary>
        /// Maps the certificate usage enum values to the corresponding string values that are configured
        /// for the "Certificate Usage" entry parameter inside of Command. The string values *MUST* match
        /// exactly the value configured for the certificate usage entry parameter in Command.
        /// </summary>
        /// <param name="certUsageEnum"></param>
        /// <returns>String representation of certificate usage that appears in Command on each certificate</returns>
        public static string GetCertUsageAsString(Constants.CertificateUsage certUsageEnum)
        {
            string certUsageString = "";
            switch (certUsageEnum)
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
                case Constants.CertificateUsage.Trust:
                {
                    certUsageString = "Trust";
                    break;
                }
                case Constants.CertificateUsage.None:
                {
                    certUsageString = "None";
                    break;
                }
                default:
                    break;
            }

            if(String.IsNullOrEmpty(certUsageString))
            {
                throw new Exception($"No certificate usage defined for enum '{certUsageEnum}'.");
            }
            
            return certUsageString;
        }
        
        /// <summary>
        /// Maps the certificate usage string values to the corresponding enum values that are
        /// declared in Constants.cs. The string values *MUST* match exactly the value configured
        /// for the certificate usage entry parameter in Command.
        /// </summary>
        /// <param name="certUsageString"></param>
        /// <returns>Enum representation of certificate usage that is declared in Constants.cs</returns>
        public static CertificateUsage GetCertUsageAsEnum(string certUsageString)
        {
            var certUsageEnum = CertificateUsage.None;
            switch (certUsageString)
            {
                case "HTTPS":
                {
                    certUsageEnum = Constants.CertificateUsage.Https;
                    break;
                }
                case "MQTT":
                {
                    certUsageEnum = Constants.CertificateUsage.MQTT;
                    break;
                }
                case "IEEE802.X":
                {
                    certUsageEnum = CertificateUsage.IEEE;
                    break;
                }
                case "Trust":
                {
                    certUsageEnum = CertificateUsage.IEEE;
                    break;
                }
                case "None":
                {
                    certUsageEnum = CertificateUsage.None;
                    break;
                }
                default:
                    certUsageEnum = CertificateUsage.Undefined;
                    break;
            }
            
            if(certUsageEnum == CertificateUsage.Undefined)
            {
                throw new Exception($"No certificate usage defined for string '{certUsageString}'.");
            }
            
            return certUsageEnum;
        }

        public static void ValidateCsr(string csrPem)
        {
            try
            {
                PemReader pemReader = new PemReader(new StringReader(csrPem));
                Pkcs10CertificationRequest csr = (Pkcs10CertificationRequest)pemReader.ReadObject();

                if (!csr.Verify())
                {
                    throw new Exception("CSR signature verification failed.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"CSR Validation failed: {ex.Message}");
            }
        }
        
        public static void ValidateCertificate(X509Certificate2 cert)
        {
            var errors = new StringBuilder();
            
            try
            {
                using (var chain = new X509Chain())
                {
                    bool isValid = chain.Build(cert);

                    if (!isValid)
                    {
                        foreach (var status in chain.ChainStatus)
                        {
                            errors.AppendLine($"Chain error: {status.Status} - {status.StatusInformation}");
                        }

                        throw new Exception(errors.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Certificate Validation failed: \n{ex.Message}");
            }
        }
    }
}
