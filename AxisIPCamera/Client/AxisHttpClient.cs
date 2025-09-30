// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.
 
using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Authenticators;

using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;
using Keyfactor.Orchestrators.Extensions.Interfaces;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Helpers;

/* AxisHttpClient.cs
 * ---------------------------------------------------------------------------------------------------
 * Description:
 * This class serves as an interface to make HTTP requests to the Axis camera web server and receive/parse responses.
 * The requests made are those required for implementation of the following orchestrator jobs:
 * -Inventory
 * -Reenrollment
 * -Management-Add (*To a Trust only)
 * -Management-Delete (*From a Trust only)
 *
 * Notes:
 * As part of this integration, each certificate on the Axis camera will appear in Command with an associated
 * "Certificate Usage" entry parameter.
 * The possible certificate usages include the following:
 * -HTTPS
 * -MQTT
 * -IEEE802.x
 * -Trust
 *
 * It's important to note that this integration makes use of 3 different Axis APIs.
 * (1) VAPIX Certificate Management API (REST) = This is used for generating and installing new certs, fetching certs,
 * removing and replacing certs (**This is a newer REST API that is only available on AXIS OS 11 and 12)
 *
 * (2) Certificate management API (SOAP) = This is used to retrieve and set the certificates associated with
 * HTTPS and IEEE802.x applications (**This is the older SOAP API that is available on AXIS 0S 10 and below.)
 * 
 * (3) MQTT Client API (CGI) = This is used to retrieve and set the certificate associated with
 * MQTT applications (**This implements client.cgi as its communications interface)
 * 
 */
namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client
{
    public class AxisHttpClient
    {
        private readonly RestClient _httpClient;
        private ILogger Logger { get; }

        public AxisHttpClient(JobConfiguration config, CertificateStore store, IPAMSecretResolver resolver)
        {
            try
            {
                var errorContext = new CertificateErrorContext();
                
                Logger = LogHandler.GetClassLogger<AxisHttpClient>();
                Logger.LogTrace("Entered AxisHttpClient constructor.");
                Logger.LogTrace("Initializing Axis IP Camera HTTP client");
                
                // ** NOTE: Ignoring the default config.UseSSL custom field --- we will always connect to the device via HTTPS
                var baseRestClientUrl = $"https://{store.ClientMachine}";
                
                Logger.LogDebug($"Base HTTP client URL: {baseRestClientUrl}");

                // Initialize custom HTTP handler to validate device identity
                RestClientOptions options = null;
                Logger.LogTrace($"Adding custom TLS cert validator to the HTTP client options...");
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        DeviceCertValidator.GetValidator(store.StorePath, errorContext, Logger)
                };

                // Initialize HTTP client options with the base URL and custom TLS cert validator
                options = new RestClientOptions(baseRestClientUrl)
                {
                    ConfigureMessageHandler = _ => handler
                };

                // Add Basic Auth username and password credentials
                Logger.LogTrace("Adding Basic Auth Credentials to the HTTP client options...");
                string username = PAMUtilities.ResolvePAMField(resolver, Logger, "API Username", config.ServerUsername);
                string password = PAMUtilities.ResolvePAMField(resolver, Logger, "API Password", config.ServerPassword);
                
                options.Authenticator = new HttpBasicAuthenticator(config.ServerUsername, config.ServerPassword);

                // Add SSL validation
                Logger.LogTrace("Validating connection to the device...");

                _httpClient = new RestClient(options);
                var request = new RestRequest("/"); // Initiates the TLS handshake to retrieve the server cert
                var response = _httpClient.Execute(request);

                // Build the list of errors to log to the console
                StringBuilder errorSb = new StringBuilder();
                if (errorContext.HasErrors)
                {
                    foreach (var error in errorContext.Errors)
                    {
                        errorSb.AppendLine(error);
                    }
                    throw new Exception(errorSb.ToString());
                }
                
                Logger.LogTrace($"Connection to the device response status code: {response.StatusCode}");
                Logger.LogTrace("Completed Initialization of Axis IP Camera HTTP Client");
                Logger.LogTrace("Leaving AxisHttpClient constructor.");
            }
            catch (Exception e)
            {
                Logger.LogError("Error initializing Axis IP Camera HTTP Client: " + LogHandler.FlattenException(e));
                throw new Exception($"Device identity could not be verified successfully --- {e.Message}");
            }
        }

        // Business Logic for Orchestrator Jobs

        /// <summary>
        /// Retrieves all the CA certificates on the device. 
        /// </summary>
        /// <returns>CACertificateData object</returns>
        public CACertificateData ListCACertificates()
        {
            var certsFound = new CACertificateData();

            try
            {
                Logger.MethodEntry();

                var getCACertsResource = $"{Constants.RestApiEntryPoint}/ca_certificates";
                var httpResponse = ExecuteHttp(getCACertsResource, Method.Get);

                // Decode the HTTP response if failed
                if (httpResponse is {IsSuccessful:false})
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    RestApiResponse apiResponse = JsonConvert.DeserializeObject<RestApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                        return JsonConvert.DeserializeObject<CACertificateData>(httpResponse.Content);
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception(
                            $"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error retrieving CA certificates on device: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Gathers all the client certificates on the device.
        /// </summary>
        /// <returns>CertificateData object</returns>
        public CertificateData ListCertificates()
        {
            var certsFound = new CertificateData();

            try
            {
                Logger.MethodEntry();

                var getCertsResource = $"{Constants.RestApiEntryPoint}/certificates";
                var httpResponse = ExecuteHttp(getCertsResource, Method.Get);
                
                // Decode the HTTP response if failed
                if (httpResponse is {IsSuccessful:false})
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP response");
                    }

                    RestApiResponse apiResponse = JsonConvert.DeserializeObject<RestApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                        return JsonConvert.DeserializeObject<CertificateData>(httpResponse.Content);
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception($"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error retrieving client certificates on device: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Gets the default keystore configured on the device.
        /// </summary>
        /// <returns>Keystore Enum</returns>
        public Constants.Keystore GetDefaultKeystore()
        {
            try
            {
                Logger.MethodEntry();

                var getDefaultKeystoreResource = $"{Constants.RestApiEntryPoint}/settings/keystore";
                var httpResponse = ExecuteHttp(getDefaultKeystoreResource, Method.Get);
                
                // Decode the HTTP response if failed
                if (httpResponse is {IsSuccessful:false})
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    RestApiResponse apiResponse = JsonConvert.DeserializeObject<RestApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                        return JsonConvert.DeserializeObject<KeystoreData>(httpResponse.Content).Keystore;
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception(
                            $"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error retrieving default keystore configuration on device: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Generates a new certificate with private key on the device. Private key is generated inside the provided keystore.
        /// This certificate is self-signed and will be used to fetch a CSR and get it signed by a CA via Command.
        /// </summary>
        /// <param name="alias">Unique identifier for the cert</param>
        /// <param name="keyType">Combination of key algorithm and key size</param>
        /// <param name="keystore">Default keystore for the device</param>
        /// <param name="subject">Subject provided for the certificate</param>
        /// <param name="sans">Subject Alternative Names</param>
        public void CreateSelfSignedCert(string alias, string keyType, string keystore, string subject, string[] sans)
        {
            try
            {
                Logger.MethodEntry();

                var postSelfSignedCertResource = $"{Constants.RestApiEntryPoint}/create_certificate";
                
                // Compose the self-signed cert body
                SelfSignedCertificateData ssc = new SelfSignedCertificateData
                {
                    Cert = new SelfSignedCertificate
                    {
                        Alias = alias,
                        KeyType = keyType,
                        Keystore = keystore,
                        Subject = subject,
                        SANS = sans,
                        ValidFrom = 0, // Cert validity period will be determined by the template
                        ValidTo = 0 // Cert validity period will be determined by the template
                    }
                };

                string jsonBody = JsonConvert.SerializeObject(ssc);
                var httpResponse = ExecuteHttp(postSelfSignedCertResource, Method.Post, Constants.ApiType.Rest, jsonBody);
                
                // Decode the HTTP response if failed
                if (httpResponse is {IsSuccessful:false})
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    RestApiResponse apiResponse = JsonConvert.DeserializeObject<RestApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception(
                            $"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error creating self-signed certificate: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Obtains a CSR for the self-signed certificate with private key on the device.
        /// Fields from the self-signed certificate will be copied into the CSR. 
        /// </summary>
        /// <param name="alias">Unique identifier for the cert to be generated from the CSR</param>
        /// <returns>CSR string</returns>
        public string ObtainCSR(string alias)
        {
            try
            {
                Logger.MethodEntry();

                var postCSRResource = $"{Constants.RestApiEntryPoint}/certificates/{alias}/get_csr";
                
                // Compose the body --- This is required, but leaving the contents blank.
                // All information obtained in the self-signed cert will be used to create the CSR.
                // If there are attributes assigned by the CA, those will override the attributes that end up
                // in the certificate signed by the CA. 
                string jsonBody = @"{""data"":{}}";
                var httpResponse = ExecuteHttp(postCSRResource, Method.Post, Constants.ApiType.Rest, jsonBody);
                
                // Decode the HTTP response if failed
                if (httpResponse is {IsSuccessful:false})
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    RestApiResponse apiResponse = JsonConvert.DeserializeObject<RestApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                        return JsonConvert.DeserializeObject<CSRData>(httpResponse.Content).CSR;
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception(
                            $"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing certificate reenrollment: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Replaces the self-signed certificate with a new certificate signed by a CA via Command.
        /// The private key for the new certificate must be the same private key as the one for the self-signed certificate.
        /// </summary>
        /// <param name="alias">Unique identifier for the self-signed certificate</param>
        /// <param name="pemCert">PEM contents of the new signed certificate</param>
        public void ReplaceCertificate(string alias, string pemCert)
        {
            try
            {
                Logger.MethodEntry();

                var patchCertResource = $"{Constants.RestApiEntryPoint}/certificates/{alias}";
                
                // Compose the cert body
                string jsonBody = @"{""data"":{""certificate"":""" + pemCert + @"""}}";
                var httpResponse = ExecuteHttp(patchCertResource, Method.Patch, Constants.ApiType.Rest, jsonBody);
                
                // Decode the HTTP response if failed
                if (httpResponse is {IsSuccessful:false})
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    RestApiResponse apiResponse = JsonConvert.DeserializeObject<RestApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception(
                            $"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing certificate replacement: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Adds a CA certificate to the device.
        /// </summary>
        /// <param name="alias">Unique identifier for the CA certificate to be added</param>
        /// <param name="pemCert">PEM contents of the CA certificate</param>
        public void AddCACertificate(string alias, string pemCert)
        {
            try
            {
                Logger.MethodEntry();

                var postCACertResource = $"{Constants.RestApiEntryPoint}/ca_certificates";
                
                // Compose the CA cert body
                string jsonBody = @"{""data"":{""alias"":""" + alias + @""",""certificate"":""" + pemCert + @"""}}";
                var httpResponse = ExecuteHttp(postCACertResource, Method.Post, Constants.ApiType.Rest, jsonBody);
                
                // Decode the HTTP response if failed
                if (httpResponse is { IsSuccessful: false })
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    RestApiResponse apiResponse = JsonConvert.DeserializeObject<RestApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception(
                            $"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing CA certificate add: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Removes a CA certificate from the device.
        /// </summary>
        /// <param name="alias">Unique identifier of the CA certificate to be removed</param>
        public void RemoveCACertificate(string alias)
        {
            try
            {
                Logger.MethodEntry();

                var deleteCACertResource = $"{Constants.RestApiEntryPoint}/ca_certificates/{alias}";
                var httpResponse = ExecuteHttp(deleteCACertResource, Method.Delete);
                
                // Decode the HTTP response if failed
                if (httpResponse is { IsSuccessful: false })
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    RestApiResponse apiResponse = JsonConvert.DeserializeObject<RestApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception(
                            $"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing CA certificate remove: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Updates the binding for a specific certificate usage.
        /// Certificate usages include - HTTPS, IEEE802.X, and MQTT.
        /// </summary>
        /// <param name="alias">Unique identifier of the cert to be bound</param>
        /// <param name="certUsage">Enum representing the certificate usage (Constants.CertificateUsage)</param>
        public void SetCertUsageBinding(string alias, Constants.CertificateUsage certUsage)
        {
            string certUsageString = Constants.GetCertUsageAsString(certUsage);
            try
            {
                Logger.MethodEntry();

                string body = "";
                RestResponse httpResponse = null;
                string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

                // Compose the request body based on the certificate usage
                switch (certUsage)
                {
                    case Constants.CertificateUsage.Https:
                    {
                        Logger.LogTrace(
                            $"Reading XML request body template from {assemblyPath}\\{Constants.SetHttpsTemplate}");
                        var xmlTemplate = File.ReadAllText($"{assemblyPath}\\{Constants.SetHttpsTemplate}");
                        body = xmlTemplate.Replace("{ALIAS}", alias);

                        httpResponse = ExecuteHttp(Constants.SoapApiEntryPoint, Method.Post, Constants.ApiType.Soap,
                            body);

                        break;
                    }
                    case Constants.CertificateUsage.IEEE:
                    {
                        Logger.LogTrace(
                            $"Reading XML request body template from {assemblyPath}\\{Constants.SetIEEETemplate}");
                        var xmlTemplate = File.ReadAllText($"{assemblyPath}\\{Constants.SetIEEETemplate}");
                        body = xmlTemplate.Replace("{ALIAS}", alias);

                        httpResponse = ExecuteHttp(Constants.SoapApiEntryPoint, Method.Post, Constants.ApiType.Soap,
                            body);

                        break;
                    }
                    case Constants.CertificateUsage.MQTT:
                    {
                        // Get the config info that is required for the request body used to set the binding
                        Logger.LogTrace(
                            "Retrieve required MQTT configuration data required for the JSON request body to set the binding");
                        var clientStatusBody = File.ReadAllText($"{assemblyPath}\\{Constants.GetMQTTTemplate}");
                        var clientStatusResponse = ExecuteHttp(Constants.CgiApiEntryPoint, Method.Post,
                            Constants.ApiType.Cgi,
                            clientStatusBody);

                        // Decode the HTTP response if failed
                        if (clientStatusResponse is { IsSuccessful: false })
                        {
                            Logger.LogError(
                                $"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(clientStatusResponse)}");
                            throw new Exception($"HTTP Request unsuccessful.");
                        }
                        // Decode the API response when HTTP response is successful
                        else
                        {
                            if (clientStatusResponse != null && string.IsNullOrEmpty(clientStatusResponse.Content))
                            {
                                throw new Exception("No content returned from HTTP Response");
                            }

                            CgiApiResponse apiResponse;
                            try
                            {
                                Logger.LogTrace("Parsing the JSON response");
                                apiResponse =
                                    JsonConvert.DeserializeObject<CgiApiResponse>(clientStatusResponse.Content);
                            }
                            catch (JsonReaderException ex1)
                            {
                                throw new Exception($"JSON response body is malformed: {ex1.Message}");
                            }
                            catch (Exception ex2)
                            {
                                throw new Exception($"Unable to parse JSON response: {ex2.Message}");
                            }

                            if (apiResponse
                                    .ErrorInfo is null) // No error was encountered, parse a successful API response
                            {
                                Logger.LogTrace("CGI API returned success!");
                                MqttResponse clientStatusData;
                                try
                                {
                                    clientStatusData =
                                        JsonConvert.DeserializeObject<MqttResponse>(clientStatusResponse.Content);
                                }
                                catch (JsonReaderException ex1)
                                {
                                    throw new Exception($"JSON response body is malformed: {ex1.Message}");
                                }
                                catch (Exception ex2)
                                {
                                    throw new Exception($"Unable to parse JSON response: {ex2.Message}");
                                }

                                var jsonTemplate = File.ReadAllText($"{assemblyPath}\\{Constants.SetMQTTTemplate}");
                                Logger.LogDebug("Client Status Return Values - ");
                                Logger.LogDebug("API Version: " + clientStatusData.ApiVersion);
                                Logger.LogDebug("Host: " + clientStatusData.Data.Config.Server.Host);
                                Logger.LogDebug("Client ID: " + clientStatusData.Data.Config.ClientId);
                                Logger.LogDebug("Alias: " + alias);
                                body = jsonTemplate.Replace("{API_VERSION}", clientStatusData.ApiVersion)
                                    .Replace("{HOST}", clientStatusData.Data.Config.Server.Host)
                                    .Replace("{CLIENT_ID}", clientStatusData.Data.Config.ClientId)
                                    .Replace("{ALIAS}", alias);

                                // Get validate server cert setting
                                MqttBody updatedBody = JsonConvert.DeserializeObject<MqttBody>(body);
                                updatedBody.Params.Ssl.ValidateServerCert =
                                    clientStatusData.Data.Config.Ssl.ValidateServerCert;
                                updatedBody.Params.CleanSession = clientStatusData.Data.Config.CleanSession;
                                var finalBody = JsonConvert.SerializeObject(updatedBody);
                                Logger.LogDebug("JSON Body: " + finalBody);

                                httpResponse = ExecuteHttp(Constants.CgiApiEntryPoint, Method.Post,
                                    Constants.ApiType.Cgi, finalBody);
                            }
                            else
                            {
                                Logger.LogTrace("CGI API returned an error");
                                throw new Exception(
                                    $"CGI API error encountered - {apiResponse.ErrorInfo.Message} - (Code: {apiResponse.ErrorInfo.Code})");
                            }

                            break;
                        }
                    }
                    default:
                        break;
                }

                // Decode the HTTP response if failed
                if (httpResponse is { IsSuccessful: false })
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    // Parse the response based on the certificate usage --- make sure there were no errors
                    switch (certUsage)
                    {
                        case Constants.CertificateUsage.Https:
                        {
                            ParseSoapResponse(httpResponse.Content);
                            break;
                        }
                        case Constants.CertificateUsage.IEEE:
                        {
                            ParseSoapResponse(httpResponse.Content);
                            break;
                        }
                        case Constants.CertificateUsage.MQTT:
                        {
                            CgiApiResponse apiResponse;
                            try
                            {
                                Logger.LogTrace("Parsing the JSON response");
                                apiResponse = JsonConvert.DeserializeObject<CgiApiResponse>(httpResponse.Content);
                            }
                            catch (JsonReaderException ex1)
                            {
                                throw new Exception($"JSON response body is malformed: {ex1.Message}");
                            }
                            catch (Exception ex2)
                            {
                                throw new Exception($"Unable to parse JSON response: {ex2.Message}");
                            }

                            if (apiResponse.ErrorInfo is null) // No error was encountered, parse a successful API response
                            {
                                Logger.LogTrace("CGI API returned success!");
                            }
                            else
                            {
                                Logger.LogTrace("CGI API returned an error");
                                throw new Exception(
                                    $"CGI API error encountered - {apiResponse.ErrorInfo.Message} - (Code: {apiResponse.ErrorInfo.Code})");
                            }

                            break;
                        }
                        default:
                            break;
                    }

                    Logger.MethodExit();
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Error setting '{certUsageString}' binding: {LogHandler.FlattenException(e)}");
                throw new Exception(e.Message);
            }
        }

        /// <summary>
        /// Retrieves the binding for a specific certificate usage.
        /// Certificate usages include - HTTPS, IEEE802.X, and MQTT.
        /// </summary>
        /// <param name="certUsage">Enum representing the certificate usage (Constants.CertificateUsage)</param>
        public string GetCertUsageBinding(Constants.CertificateUsage certUsage)
        {
            try
            {
                Logger.MethodEntry();

                string body = "";
                RestResponse httpResponse = null;
                string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                string certUsageString = Constants.GetCertUsageAsString(certUsage);
                string boundCertAlias = "UNKNOWN";
                
                // Compose the request body based on the certificate usage
                Logger.LogTrace($"Retrieving certificate binding for '{certUsageString}'");
                switch (certUsage)
                {
                    case Constants.CertificateUsage.Https:
                    {
                        Logger.LogTrace($"Reading XML request body template from {assemblyPath}\\{Constants.GetHttpsTemplate}");
                        body = File.ReadAllText($"{assemblyPath}\\{Constants.GetHttpsTemplate}");
                        httpResponse = ExecuteHttp(Constants.SoapApiEntryPoint, Method.Post, Constants.ApiType.Soap,body);
                        
                        break;
                    }
                    case Constants.CertificateUsage.IEEE:
                    {
                        Logger.LogTrace($"Reading XML request body template from {assemblyPath}\\{Constants.GetIEEETemplate}");
                        body = File.ReadAllText($"{assemblyPath}\\{Constants.GetIEEETemplate}");
                        httpResponse = ExecuteHttp(Constants.SoapApiEntryPoint, Method.Post, Constants.ApiType.Soap,body);
                        
                        break;
                    }
                    case Constants.CertificateUsage.MQTT:
                    {
                        Logger.LogTrace($"Reading JSON request body template from {assemblyPath}\\{Constants.GetMQTTTemplate}");
                        body = File.ReadAllText($"{assemblyPath}\\{Constants.GetMQTTTemplate}");
                        httpResponse = ExecuteHttp(Constants.CgiApiEntryPoint, Method.Post, Constants.ApiType.Cgi,body);

                        break;
                    }
                    default:
                        break;
                }
                
                // Decode the HTTP response if failed
                if (httpResponse is {IsSuccessful:false})
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHttpStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (httpResponse != null && string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    // Parse the response based on the certificate usage
                    switch (certUsage)
                    {
                        case Constants.CertificateUsage.Https:
                        {
                            XmlDocument xDoc = new XmlDocument();
                            XmlNodeList xNodes = null;
                            xDoc.LoadXml(httpResponse.Content);

                            // Extract the XML node identified by the tag name 
                            xNodes = xDoc.GetElementsByTagName(Constants.HttpsAliasTagName);
                            if (xNodes is null)
                            {
                                throw new Exception(
                                    $"Could not locate XML tag '{Constants.HttpsAliasTagName}' in the SOAP response. Unable to extract the certificate alias.");
                            }

                            for (int i = 0; i < xNodes.Count; i++)
                            {
                                if (i > 0)
                                {
                                    throw new Exception(
                                        "More than 1 certificate alias was found in the SOAP response. This should never happen.");
                                }

                                if (xNodes[i] is null)
                                {
                                    throw new Exception($"XML element at position {i} is null.");
                                }

                                boundCertAlias = xNodes[i].InnerXml;
                            }

                            break;
                        }
                        case Constants.CertificateUsage.IEEE:
                        {
                            XmlDocument xDoc = new XmlDocument();
                            XmlNodeList xNodes = null;
                            xDoc.LoadXml(httpResponse.Content);

                            // Extract the XML node identified by the tag name 
                            xNodes = xDoc.GetElementsByTagName(Constants.IEEEAliasTagName);
                            if (xNodes is null)
                            {
                                throw new Exception(
                                    $"Could not locate XML tag '{Constants.IEEEAliasTagName}' in the SOAP response. Unable to extract the certificate alias.");
                            }

                            for (int i = 0; i < xNodes.Count; i++)
                            {
                                if (i > 0)
                                {
                                    throw new Exception(
                                        "More than 1 certificate alias was found in the SOAP response. This should never happen.");
                                }

                                if (xNodes[i] is null)
                                {
                                    throw new Exception($"XML element at position {i} is null.");
                                }

                                boundCertAlias = xNodes[i].InnerXml;
                            }

                            break;
                        }
                        case Constants.CertificateUsage.MQTT:
                        {
                            CgiApiResponse apiResponse;
                            try
                            {
                                Logger.LogTrace("Parsing the JSON response");
                                apiResponse = JsonConvert.DeserializeObject<CgiApiResponse>(httpResponse.Content);
                            }
                            catch (JsonReaderException ex1)
                            {
                                throw new Exception($"JSON response body is malformed: {ex1.Message}");
                            }
                            catch (Exception ex2)
                            {
                                throw new Exception($"Unable to parse JSON response: {ex2.Message}");
                            }
                            
                            if (apiResponse.ErrorInfo is null) // No error was encountered, parse a successful API response
                            {
                                Logger.LogTrace("CGI API returned success!");
                                MqttResponse mqttResponse;
                                try
                                {
                                    mqttResponse = JsonConvert.DeserializeObject<MqttResponse>(httpResponse.Content);
                                }
                                catch (JsonReaderException ex1)
                                {
                                    throw new Exception($"JSON response body is malformed: {ex1.Message}");
                                }
                                catch (Exception ex2)
                                {
                                    throw new Exception($"Unable to parse JSON response: {ex2.Message}");
                                }
                                
                                // If no client certificate is assigned or the value is "None" for the MQTT binding,
                                // the 'clientCertId' key will not appear in the JSON response
                                if (string.IsNullOrEmpty(mqttResponse.Data.Config.Ssl.ClientCertId))
                                {
                                    Logger.LogTrace($"No client certificate assigned to '{certUsageString}'");
                                    boundCertAlias = "";
                                }
                                else
                                {
                                    boundCertAlias = mqttResponse.Data.Config.Ssl.ClientCertId;   
                                }
                            }
                            else
                            {
                                Logger.LogTrace("CGI API returned an error");
                                throw new Exception(
                                    $"CGI API error encountered - {apiResponse.ErrorInfo.Message} - (Code: {apiResponse.ErrorInfo.Code})");
                            }
                            
                            break;
                        }
                        default:
                            break;
                    }

                    Logger.LogDebug($"Bound certificate alias for '{certUsageString}': {boundCertAlias}");

                    Logger.MethodExit();
                }

                return boundCertAlias;
            }
            catch (Exception e)
            {
                Logger.LogError($"Error retrieving certificate binding for: " +
                    LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        // GET / DELETE Requests
        private RestResponse ExecuteHttp(string resource, Method httpMethod)
        {
            try
            {
                Logger.MethodEntry();

                // Check if HTTP client was properly initialized
                if (_httpClient is null)
                {
                    throw new Exception("Axis IP Camera HTTP Client was not initialized.");
                }

                var request = new RestRequest(resource, httpMethod);

                Logger.LogDebug($"HTTP Request URI: {_httpClient.BuildUri(request)}");
                Logger.LogDebug($"HTTP Method: {httpMethod.ToString()}");

                Logger.LogTrace("Executing REST Request...");
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace("HTTP Request completed");

                Logger.LogDebug($"HTTP Response: {httpResponse?.Content}");

                Logger.MethodExit();

                return httpResponse;
            }
            catch (Exception e)
            {
                Logger.LogError($"Error Occured in AxisRestClient.ExecuteHttp: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        // POST Requests
        private RestResponse ExecuteHttp(string resource, Method httpMethod, Constants.ApiType apiType, string body)
        {
            try
            {
                Logger.MethodEntry();

                // Check if HTTP client was properly initialized
                if (_httpClient is null)
                {
                    throw new Exception("Axis IP Camera HTTP Client was not initialized.");
                }

                var request = new RestRequest(resource, httpMethod);

                // JSON body required for REST Cert Management API and CGI client API
                if (apiType is Constants.ApiType.Rest or Constants.ApiType.Cgi)
                {
                    request.RequestFormat = DataFormat.Json;
                    if (String.IsNullOrEmpty(body))
                    {
                        throw new Exception("JSON body is null or empty.");
                    }
                    Logger.LogDebug($"JSON Body: {body}");
                    request.AddJsonBody(body);
                }

                // XML body required for SOAP Cert Management API 
                if (apiType == Constants.ApiType.Soap)
                {
                    request.AddHeader("Content-Type", "application/xml");
                    if (String.IsNullOrEmpty(body))
                    {
                        throw new Exception("XML body is null or empty.");
                    }
                    Logger.LogDebug($"XML Body: {body}");
                    request.AddParameter("application/xml", body, ParameterType.RequestBody);
                }

                Logger.LogDebug($"HTTP Request URI: {_httpClient.BuildUri(request)}");
                Logger.LogDebug($"HTTP Method: {httpMethod.ToString()}");

                Logger.LogTrace("Executing HTTP Request...");
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace("HTTP Request completed");

                Logger.LogDebug($"HTTP Response: {httpResponse?.Content}");

                Logger.MethodExit();

                return httpResponse;
            }
            catch (Exception e)
            {
                Logger.LogError($"Error Occured in AxisRestClient.ExecuteHttp: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /// <summary>
        /// Decodes the given HTTP status code into a human-readable string.
        /// These include some of the most common HTTP status codes.
        /// </summary>
        /// <param name="httpResponse"></param>
        /// <returns>Human-readable representation of the HTTP status code</returns>
        private string DecodeHttpStatus(RestResponse httpResponse)
        {
            Logger.MethodEntry();
            
            Logger.LogDebug($"HTTP Response Code: {httpResponse.StatusCode}");
            var codeString = "";
            switch (httpResponse.StatusCode)
            {
                case HttpStatusCode.BadRequest:
                {
                    codeString = "Bad Request! (400)";
                    break;
                }
                case HttpStatusCode.Unauthorized:
                {
                    codeString = "Unauthorized! (401)";
                    break;
                }
                case HttpStatusCode.Forbidden:
                {
                    codeString = "Forbidden! (403)";
                    break;
                }
                case HttpStatusCode.NotFound:
                {
                    codeString = "Not Found! (404)";
                    break;
                }
                case HttpStatusCode.InternalServerError:
                {
                    codeString = "Internal Server Error! (500)";
                    break;
                }
                default:
                {
                    codeString = "No response received! Possible causes: Timeouts, no network connectivity, DNS resolution failure, SSL issues, firewall configuration, etc.";
                    if (!string.IsNullOrEmpty(httpResponse.ErrorMessage))
                    {
                        codeString += $"\nError message: {httpResponse.ErrorMessage}";
                    }
                    else if (httpResponse.ErrorException != null)
                    {
                        codeString += $"\nException: {httpResponse.ErrorException.Message}";
                    }
                    break;
                }
            }

            Logger.MethodExit();
            
            return codeString;
        }
        
        /// <summary>
        /// Parses the given SOAP response body into a message and code.
        /// </summary>
        /// <param name="soapResponse"></param>
        private void ParseSoapResponse(string soapResponse)
        {
            Logger.MethodEntry();
            
            XNamespace soapEnv = "http://www.w3.org/2003/05/soap-envelope";

            StringBuilder soapError = new StringBuilder();
            try
            {
                var doc = XDocument.Parse(soapResponse);
                var fault = doc.Descendants(soapEnv + "Fault").FirstOrDefault();

                if (fault != null)
                {
                    var code = fault.Descendants(soapEnv + "Value").FirstOrDefault()?.Value;
                    var reason = fault.Descendants(soapEnv + "Text").FirstOrDefault()?.Value;
                    var detail = fault.Element(soapEnv + "Detail");

                    soapError.Append("SOAP API error encountered - ");
                    soapError.Append(reason ?? "(No error reason provided)");
                    soapError.Append(" - (Code: " + code + ")");

                    if (detail != null)
                    {
                        var detailContent = detail.Elements().FirstOrDefault();
                        Logger.LogTrace($"Detail Element: {detailContent?.Name.LocalName ?? "(none)"}");
                    }
                    else
                    {
                        Logger.LogTrace("No SOAP Fault detail element found.");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Unable to parse SOAP response: {ex.Message}");
            }

            if (soapError.Length == 0)
            {
                Logger.LogTrace("SOAP API returned success!");
            }
            else
            {
                Logger.LogTrace("SOAP API returned an error");
                throw new Exception(soapError.ToString());
            }
            
            Logger.MethodExit();
        }
    }
}
