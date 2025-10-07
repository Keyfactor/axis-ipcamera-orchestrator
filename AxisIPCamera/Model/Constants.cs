// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.IO;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;

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
        
        // Below are the relative file paths to the SOAP and CGI API request body templates
        // ** NOTE: The 'Files' directory should be located in the same directory as the AxisIPCamera.dll
        public static readonly string GetHttpsTemplate = $"{Path.Combine("Files","GetHttpsBinding.xml")}";
        public static readonly string GetIEEETemplate  = $"{Path.Combine("Files","GetIEEEBinding.xml")}";
        public static readonly string GetMQTTTemplate  = $"{Path.Combine("Files","GetMQTTBinding.json")}";
        public static readonly string SetHttpsTemplate = $"{Path.Combine("Files", "SetHttpsBinding.xml")}";
        public static readonly string SetIEEETemplate  = $"{Path.Combine("Files", "SetIEEEBinding.xml")}";
        public static readonly string SetMQTTTemplate  = $"{Path.Combine("Files", "SetMQTTBinding.json")}";
        
        public enum CertificateUsage
        {
            Https,
            IEEE,
            MQTT,
            Trust,
            Other,
            Undefined
        }

        // ** NOTE: There may be more keystore types depending on the Axis camera model
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
        /// ** NOTE: These values may need updated depending on the target camera OS.
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
                case Constants.CertificateUsage.Other:
                {
                    certUsageString = "Other";
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
            var certUsageEnum = CertificateUsage.Other;
            switch (certUsageString)
            {
                case "HTTPS":
                {
                    certUsageEnum = CertificateUsage.Https;
                    break;
                }
                case "MQTT":
                {
                    certUsageEnum = CertificateUsage.MQTT;
                    break;
                }
                case "IEEE802.X":
                {
                    certUsageEnum = CertificateUsage.IEEE;
                    break;
                }
                case "Trust":
                {
                    certUsageEnum = CertificateUsage.Trust;
                    break;
                }
                case "Other":
                {
                    certUsageEnum = CertificateUsage.Other;
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
    }
}
