using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ConstrainedExecution;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Authenticators;
using System.Xml;

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
 * (1) VAPIX Certificate Management API = This is used for generating and installing new certs, fetching certs,
 * removing and replacing certs (**This is a newer REST API that is only available on AXIS OS 11 and 12)
 *
 * (2) Certificate management API = This is used to retrieve and set the certificates associated with
 * HTTPS and IEEE802.x applications (**This is the older SOAP API that is available on AXIS 0S 10 and below.)
 * 
 * (3) MQTT Client API = This is used to retrieve and set the certificate associated with
 * MQTT applications (**This implements client.cgi as its communications interface)
 * 
 */
namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client
{
    public class AxisHttpClient
    {
        private readonly RestClient _httpClient;
        private ILogger Logger { get; }

        public AxisHttpClient(JobConfiguration config, CertificateStore store)
        {
            try
            {
                Logger = LogHandler.GetClassLogger<AxisHttpClient>();
                Logger.MethodEntry();

                Logger.LogTrace("Initializing Axis IP Camera HTTP Client");

                var baseRestClientUrl = (config.UseSSL) ? $"https://{store.ClientMachine}" : $"http://{store.ClientMachine}";

                // TODO: Need to consider onboarding of camera
                Logger.LogDebug($"Base HTTP Client URL: {baseRestClientUrl}");

                // Initialize HTTP client options with the base URL
                var options = new RestClientOptions(baseRestClientUrl);

                // Add Basic Auth username and password credentials
                Logger.LogTrace("Adding Basic Auth Credentials to the HTTP client options...");

                // TODO: Do we want to remove this log statement in PRODUCTION?
                Logger.LogDebug($"Username: {config.ServerUsername}, Password: {config.ServerPassword}");
                options.Authenticator = new HttpBasicAuthenticator(config.ServerUsername, config.ServerPassword);

                // Add SSL validation
                Logger.LogTrace("Checking for SSL validation...");
                Logger.LogDebug($"Use SSL: {config.UseSSL}");

                Logger.LogTrace("Turning off SSL validation --- FOR TESTING ONLY");

                // TODO: Enable this flag in PRODUCTION
                //if (config.UseSSL)
                //{
                options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                //}

                _httpClient = new RestClient(options);

                Logger.LogTrace("Completed Initialization of Axis IP Camera HTTP Client");

                Logger.MethodExit();
            }
            catch (Exception e)
            {
                Logger.LogError("Error in Constructor AxisRestClient(): " + LogHandler.FlattenException(e));
                throw;
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

                var getCACertsResource = $"{Constants.ApiEntryPoint}/ca_certificates";
                var httpResponse = ExecuteHttp(getCACertsResource, Method.Get, Constants.ApiType.VapixCertMgmt);

                // Decode the HTTP response if failed
                if (!httpResponse.IsSuccessful)
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHTTPStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
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
                throw new Exception(e.Message + " Refer to logs for more detailed information.");
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

                var getCertsResource = $"{Constants.ApiEntryPoint}/certificates";
                var httpResponse = ExecuteHttp(getCertsResource, Method.Get, Constants.ApiType.VapixCertMgmt);
                
                // Decode the HTTP response if failed
                if (!httpResponse.IsSuccessful)
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHTTPStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP response");
                    }

                    ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
                    if (apiResponse.Status == Constants.Status.Success)
                    {
                        Logger.MethodExit();
                        return JsonConvert.DeserializeObject<CertificateData>(httpResponse.Content);
                    }
                    else
                    {
                        ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                        throw new Exception($"API error encountered - {error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code}");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error retrieving client certificates on device: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message + " Refer to logs for more detailed information.");
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

                var getDefaultKeystoreResource = $"{Constants.ApiEntryPoint}/settings/keystore";
                var httpResponse = ExecuteHttp(getDefaultKeystoreResource, Method.Get, Constants.ApiType.VapixCertMgmt);
                
                // Decode the HTTP response if failed
                if (!httpResponse.IsSuccessful)
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHTTPStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
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
                throw new Exception(e.Message + " Refer to logs for more detailed information.");
            }
        }

        /// <summary>
        /// Generates a new certificate with private key on the device. Private key is generated inside the provided keystore.
        /// This certificate is self-signed and will be used to fetch a CSR and get it signed by Command.
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

                var postSelfSignedCertResource = $"{Constants.ApiEntryPoint}/create_certificate";
                
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
                var httpResponse = ExecuteHttp(postSelfSignedCertResource, Method.Post, Constants.ApiType.VapixCertMgmt, jsonBody);
                
                // Decode the HTTP response if failed
                if (!httpResponse.IsSuccessful)
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHTTPStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
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
                throw new Exception(e.Message + " Refer to logs for more detailed information.");
            }
        }

        public string ObtainCSR(string alias)
        {
            try
            {
                Logger.MethodEntry();

                var postCSRResource = $"{Constants.ApiEntryPoint}/certificates/{alias}/get_csr";
                
                // Compose the body --- This is required, but leaving the contents blank.
                // All information obtained in the self-signed cert will be used to create the CSR.
                string jsonBody = @"{""data"":{}}";
                var httpResponse = ExecuteHttp(postCSRResource, Method.Post, Constants.ApiType.VapixCertMgmt, jsonBody);
                
                // Decode the HTTP response if failed
                if (!httpResponse.IsSuccessful)
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHTTPStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
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
                throw new Exception(e.Message + " Refer to logs for more detailed information.");
            }
        }

        public void ReplaceCertificate(string alias, string pemCert)
        {
            try
            {
                Logger.MethodEntry();

                var patchCertResource = $"{Constants.ApiEntryPoint}/certificates/{alias}";
                
                // Compose the cert body
                string jsonBody = @"{""data"":{""certificate"":""" + pemCert + @"""}}";
                var httpResponse = ExecuteHttp(patchCertResource, Method.Patch, Constants.ApiType.VapixCertMgmt, jsonBody);
                
                // Decode the HTTP response if failed
                if (!httpResponse.IsSuccessful)
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHTTPStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }
                // Decode the API response when HTTP response is successful
                else
                {
                    if (string.IsNullOrEmpty(httpResponse.Content))
                    {
                        throw new Exception("No content returned from HTTP Response");
                    }

                    ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
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
                throw new Exception(e.Message + " Refer to logs for more detailed information.");
            }
        }

        public void AddCertificate(string alias, string pemCert)
        {
            try
            {
                Logger.MethodEntry();

                // Compose the cert body
                string jsonBody = @"{""data"":{""alias"":""" + alias + @""",""certificate"":""" + pemCert + @"""}}";
                Logger.LogDebug($"Pem cert JSON body: {jsonBody}");

                HTTP_PostAddCACertificate(alias, jsonBody);

                Logger.MethodExit();
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing certificate add: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        public void SetCertUsageBinding(string alias, Constants.CertificateUsage certUsage)
        {
            try
            {
                Logger.MethodEntry();
                
                string body = "";
                RestResponse httpResponse = null;
                string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                
                // Compose the body
                switch (certUsage)
                {
                    case Constants.CertificateUsage.Https:
                    {
                        var xmlTemplate = File.ReadAllText($"{assemblyPath}\\Files\\SetHttpsBinding.xml");
                        body = xmlTemplate.Replace("{ALIAS}", alias);
                        
                        httpResponse = ExecuteHttp("/vapix/services", Method.Post, Constants.ApiType.CertMgmt,body);
                        
                        break;
                    }
                    case Constants.CertificateUsage.IEEE:
                    {
                        var xmlTemplate = File.ReadAllText($"{assemblyPath}\\Files\\SetIEEEBinding.xml");
                        body = xmlTemplate.Replace("{ALIAS}", alias);
                        
                        httpResponse = ExecuteHttp("/vapix/services", Method.Post, Constants.ApiType.CertMgmt,body);
                        
                        break;
                    }
                    case Constants.CertificateUsage.MQTT:
                    {
                        var jsonTemplate = File.ReadAllText($"{assemblyPath}\\Files\\SetMQTTBinding.json");
                        body = jsonTemplate.Replace("{ALIAS}", alias);
                        
                        httpResponse = ExecuteHttp("/axis-cgi/mqtt/client.cgi", Method.Post, Constants.ApiType.MQTTClient,body);

                        break;
                    }
                    default:
                        break;
                }
               
                // Decode the HTTP response if failed
                if (httpResponse is { IsSuccessful: false })
                {
                    Logger.LogError($"HTTP Request unsuccessful - HTTP Response: {DecodeHTTPStatus(httpResponse)}");
                    throw new Exception($"HTTP Request unsuccessful.");
                }

                Logger.MethodExit();
            }
            catch (Exception e)
            {
                Logger.LogError("Error setting binding: " + LogHandler.FlattenException(e) + " Refer to logs for more detailed information.");
                throw new Exception(e.Message);
            }
        }

        public string GetCertUsageBinding(Constants.CertificateUsage certUsage)
        {
            try
            {
                Logger.MethodEntry();

                string body = "";
                RestResponse httpResponse = null;
                string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                
                // Compose the body
                switch (certUsage)
                {
                    case Constants.CertificateUsage.Https:
                    {
                        var xmlTemplate = File.ReadAllText($"{assemblyPath}\\Files\\GetHttpsBinding.xml");
                        
                        httpResponse = ExecuteHttp("/vapix/services", Method.Post, Constants.ApiType.CertMgmt,body);
                        
                        break;
                    }
                    case Constants.CertificateUsage.IEEE:
                    {
                        var xmlTemplate = File.ReadAllText($"{assemblyPath}\\Files\\GetIEEEBinding.xml");
                        
                        httpResponse = ExecuteHttp("/vapix/services", Method.Post, Constants.ApiType.CertMgmt,body);
                        
                        break;
                    }
                    case Constants.CertificateUsage.MQTT:
                    {
                        var jsonTemplate = File.ReadAllText($"{assemblyPath}\\Files\\GetMQTTBinding.json");
                        
                        httpResponse = ExecuteHttp("/axis-cgi/mqtt/client.cgi", Method.Post, Constants.ApiType.MQTTClient,body);

                        break;
                    }
                    default:
                        break;
                }
                
                //var bindingAlias = HTTP_PostGetBinding(certUsage);
                
                // Parse the response to retrieve the certificate alias
                /*XmlDocument xdoc = new XmlDocument();
                XmlNodeList xnodes = null;
                xdoc.LoadXml(httpResponse.Content);

                var tagName = "";
                if (certUsage == Constants.CertificateUsage.Https)
                {
                    tagName = Constants.HttpsAliasTagName;
                }
                else if (certUsage == Constants.CertificateUsage.IEEE)
                {
                    tagName = Constants.IEEEAliasTagName;
                }
                else
                {
                    throw new Exception("Unknown certificate usage. Unable to parse the SOAP response.");
                }

                // Extract the XML node identified by the tag name 
                xnodes = xdoc.GetElementsByTagName(tagName);

                if (xnodes is null)
                {
                    throw new Exception(
                        $"Could not locate XML tag '{tagName}' in the SOAP response. Unable to extract the certificate alias.");
                }

                for (int i = 0; i < xnodes.Count; i++)
                {
                    if (i > 0)
                    {
                        throw new Exception(
                            "More than 1 certificate alias was found in the SOAP response. This should never happen.");
                    }

                    boundCertAlias = xnodes[i].InnerXml;
                    Logger.LogDebug($"Bound Alias: {boundCertAlias}");
                }

                Logger.MethodExit();*/

                return "";
            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error retrieving certificate binding for {Enum.GetName(typeof(Constants.CertificateUsage), certUsage)}: " +
                    LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        // VAPIX Certificate Management API Calls
        private RestResponse ExecuteHttp(string resource, Method httpMethod, Constants.ApiType apiType, string body = null)
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

                // Cert management POST requests
                if (apiType == Constants.ApiType.VapixCertMgmt && (httpMethod == Method.Post || httpMethod == Method.Patch))
                {
                    request.RequestFormat = DataFormat.Json;
                    if (String.IsNullOrEmpty(body))
                    {
                        throw new Exception("JSON body is null or empty.");
                    }
                    Logger.LogDebug($"JSON Body: {body}");
                    request.AddJsonBody(body);
                }

                // Cert binding POST requests
                if (apiType == Constants.ApiType.CertMgmt && httpMethod == Method.Post)
                {
                    request.AddHeader("Content-Type", "application/xml");
                    if (String.IsNullOrEmpty(body))
                    {
                        throw new Exception("XML body is null or empty.");
                    }
                    Logger.LogDebug($"XML Body: {body}");
                    request.AddParameter("application/xml", body, ParameterType.RequestBody);
                }

                Logger.LogDebug($"REST Request URI: {_httpClient.BuildUri(request)}");
                Logger.LogDebug($"HTTP Method: {httpMethod.ToString()}");

                Logger.LogTrace("Executing REST Request...");
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace("REST Request completed");

                Logger.LogDebug($"REST Response: {httpResponse?.Content}");

                Logger.MethodExit();

                return httpResponse;
            }
            catch (Exception e)
            {
                Logger.LogError($"Error Occured in AxisRestClient.ExecuteHttp: {LogHandler.FlattenException(e)}");
                throw;
            }
        }
        

        // Axis REST Calls
        /**
         * HTTP GET: Retrieve a list of CA certificates
         */
        /*private CACertificateData HTTP_GetCACertificates()
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"{ApiEntryPoint}/ca_certificates";
                var request = new RestRequest(resource);

                Logger.LogTrace($"Rest Request: {_restClient.BuildUri(request)}");

                //var response = await _restClient.GetAsync(request); TODO: Look into this to see if this is what I should be doing
                var httpResponse = _restClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace($"Rest Response: {httpResponse.Content}");

                ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
                if (apiResponse.Status == Constants.Status.Success)
                {
                    return JsonConvert.DeserializeObject<CACertificateData>(httpResponse.Content);
                }
                else
                {
                    ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                    throw new Exception($"{error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code}");
                }

            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_GetCACertificates: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP GET: Retrieve a list of certificates
         */
        /*private CertificateData HTTP_GetCertificates()
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"{ApiEntryPoint}/certificates";
                var request = new RestRequest(resource);

                Logger.LogTrace($"Rest Request: {_restClient.BuildUri(request)}");

                //var response = await _restClient.GetAsync(request); TODO: Look into this to see if this is what I should be doing
                var httpResponse = _restClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace($"Rest Response: {httpResponse.Content}");

                ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
                if (apiResponse.Status == Constants.Status.Success)
                {
                    return JsonConvert.DeserializeObject<CertificateData>(httpResponse.Content);
                }
                else
                {
                    ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                    throw new Exception($"{error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code}");
                }

            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_GetCertificates: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP GET: Retrieve default keystore 
         */
        /*private KeystoreData HTTP_GetDefaultKeystore()
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"{ApiEntryPoint}/settings/keystore";
                var request = new RestRequest(resource);

                Logger.LogTrace($"Rest Request: {_restClient.BuildUri(request)}");

                //var response = await _restClient.GetAsync(request); TODO: Look into this to see if this is what I should be doing
                var httpResponse = _restClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace($"Rest Response: {httpResponse.Content}");

                ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
                if (apiResponse.Status == Constants.Status.Success)
                {
                    return JsonConvert.DeserializeObject<KeystoreData>(httpResponse.Content);
                }
                else
                {
                    ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                    throw new Exception($"{error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                }

            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_GetDefaultKeystore: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP POST: Retrieve a list of certificates
         
        private void HTTP_PostCreateCertificate(string jsonBody)
        {
            try
            {
                Logger.MethodEntry();
                if (_httpClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"{ApiEntryPoint}/create_certificate";
                var request = new RestRequest(resource, Method.Post);
                request.RequestFormat = DataFormat.Json;

                Logger.LogTrace($"Rest Request: {_httpClient.BuildUri(request)}");

                // Add the certificate body
                request.AddJsonBody(jsonBody);

                //var response = await _restClient.GetAsync(request); TODO: Look into this to see if this is what I should be doing
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace($"Rest Response: {httpResponse.Content}");

                ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
                if (apiResponse.Status == Constants.Status.Success)
                {
                    // TODO: Should I do something here?
                }
                else
                {
                    ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                    throw new Exception($"{error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                }

            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_PostCreateCertificate: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP POST: Obtain CSR from self-signed cert generated on the device
         
        private string HTTP_PostObtainCSR(string alias, string jsonBody)
        {
            try
            {
                Logger.MethodEntry();
                if (_httpClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"{ApiEntryPoint}/certificates/{alias}/get_csr";
                var request = new RestRequest(resource, Method.Post);
                request.RequestFormat = DataFormat.Json;

                Logger.LogTrace($"Rest Request: {_httpClient.BuildUri(request)}");

                // Add the certificate body
                request.AddJsonBody(jsonBody);

                //var response = await _restClient.GetAsync(request); TODO: Look into this to see if this is what I should be doing
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace($"Rest Response: {httpResponse.Content}");

                ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
                if (apiResponse.Status == Constants.Status.Success)
                {
                    return JsonConvert.DeserializeObject<CSRData>(httpResponse.Content).CSR;
                }
                else
                {
                    ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                    throw new Exception($"{error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                }

            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_PostObtainCSR: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP POST: Replace certificate but keep private key
         
        private void HTTP_PostReplaceCertificate(string alias, string jsonBody)
        {
            try
            {
                Logger.MethodEntry();
                if (_httpClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"{ApiEntryPoint}/certificates/{alias}";
                var request = new RestRequest(resource, Method.Patch);
                request.RequestFormat = DataFormat.Json;

                Logger.LogTrace($"Rest Request: {_httpClient.BuildUri(request)}");

                // Add the certificate body
                request.AddJsonBody(jsonBody);

                //var response = await _restClient.GetAsync(request); TODO: Look into this to see if this is what I should be doing
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace($"Rest Response: {httpResponse.Content}");

                ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
                if (apiResponse.Status == Constants.Status.Success)
                {
                    // TODO: Should I do something here?
                }
                else
                {
                    ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                    throw new Exception($"{error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                }

            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_PostReplaceCertificate: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP POST: Replace certificate but keep private key
         */
        private void HTTP_PostAddCACertificate(string alias, string jsonBody)
        {
            try
            {
                Logger.MethodEntry();
                if (_httpClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"{Constants.ApiEntryPoint}/ca_certificates";
                var request = new RestRequest(resource, Method.Post);
                request.RequestFormat = DataFormat.Json;

                Logger.LogTrace($"Rest Request: {_httpClient.BuildUri(request)}");

                // Add the certificate body
                request.AddJsonBody(jsonBody);

                //var response = await _restClient.GetAsync(request); TODO: Look into this to see if this is what I should be doing
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace($"Rest Response: {httpResponse.Content}");

                ApiResponse apiResponse = JsonConvert.DeserializeObject<ApiResponse>(httpResponse.Content);
                if (apiResponse.Status == Constants.Status.Success)
                {
                    // TODO: Should I do something here?
                }
                else
                {
                    ErrorData error = JsonConvert.DeserializeObject<ErrorData>(httpResponse.Content);
                    throw new Exception($"{error.ErrorInfo.Message} - (Code: {error.ErrorInfo.Code})");
                }

            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_PostAddCACertificate: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP POST: Set the binding for the provided alias and cert usage
         
        private void HTTP_PostSetBinding(string alias, Constants.CertificateUsage certUsage)
        {
            try
            {
                Logger.MethodEntry();
                if (_httpClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"/vapix/services";
                var request = new RestRequest(resource, Method.Post);
                request.AddHeader("Content-Type", "application/xml");

                Logger.LogTrace($"Rest Request: {_httpClient.BuildUri(request)}");

                var body = "";
                switch (certUsage)
                {
                    case Constants.CertificateUsage.Https:
                    {
                        body = @"<SOAP-ENV:Envelope
" + "\n" +
                               @"xmlns:wsdl=""http://schemas.xmlsoap.org/wsdl/""
" + "\n" +
                               @"xmlns:aweb=""http://www.axis.com/vapix/ws/webserver""
" + "\n" +
                               @"xmlns:acert=""http://www.axis.com/vapix/ws/cert""
" + "\n" +
                               @"xmlns:xs=""http://www.w3.org/2001/XMLSchema""
" + "\n" +
                               @"xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
" + "\n" +
                               @"xmlns:xsd=""http://www.w3.org/2001/XMLSchema""
" + "\n" +
                               @"xmlns:SOAP-ENV=""http://www.w3.org/2003/05/soap-envelope"">
" + "\n" +
                               @"
" + "\n" +
                               @"  <SOAP-ENV:Body>
" + "\n" +
                               @"    <aweb:SetWebServerTlsConfiguration xmlns=""http://www.axis.com/vapix/ws/webserver"">
" + "\n" +
                               @"    <Configuration>
" + "\n" +
                               @"      <Tls>true</Tls>
" + "\n" +
                               @"      <aweb:ConnectionPolicies>
" + "\n" +
                               @"        <aweb:Admin>Https</aweb:Admin>
" + "\n" +
                               @"      </aweb:ConnectionPolicies>
" + "\n" +
                               @"      <aweb:Ciphers>
" + "\n" +
                               @"        <acert:Cipher>ECDHE-ECDSA-AES128-GCM-SHA256</acert:Cipher>
" + "\n" +
                               @"        <acert:Cipher>ECDHE-RSA-AES128-GCM-SHA256</acert:Cipher>
" + "\n" +
                               @"        <acert:Cipher>ECDHE-ECDSA-AES256-GCM-SHA384</acert:Cipher>
" + "\n" +
                               @"        <acert:Cipher>ECDHE-RSA-AES256-GCM-SHA384</acert:Cipher>
" + "\n" +
                               @"        <acert:Cipher>ECDHE-ECDSA-CHACHA20-POLY1305</acert:Cipher>
" + "\n" +
                               @"        <acert:Cipher>ECDHE-RSA-CHACHA20-POLY1305</acert:Cipher>
" + "\n" +
                               @"        <acert:Cipher>DHE-RSA-AES128-GCM-SHA256</acert:Cipher>
" + "\n" +
                               @"        <acert:Cipher>DHE-RSA-AES256-GCM-SHA384</acert:Cipher>
" + "\n" +
                               @"      </aweb:Ciphers>
" + "\n" +
                               @"      <aweb:CertificateSet>
" + "\n" +
                               @"        <acert:Certificates>
" + "\n" +
                               $@"          <acert:Id>{alias}</acert:Id>
" + "\n" +
                               @"        </acert:Certificates>
" + "\n" +
                               @"        <acert:CACertificates></acert:CACertificates>
" + "\n" +
                               @"        <acert:TrustedCertificates></acert:TrustedCertificates>
" + "\n" +
                               @"      </aweb:CertificateSet>
" + "\n" +
                               @"    </Configuration>
" + "\n" +
                               @"    </aweb:SetWebServerTlsConfiguration>
" + "\n" +
                               @"  </SOAP-ENV:Body>
" + "\n" +
                               @"</SOAP-ENV:Envelope>";
                        break;

                    }
                    default:
                        break;
                }

                request.AddParameter("application/xml", body, ParameterType.RequestBody);

                //TODO: May need to wait for reboot of camera after HTTP cert is updated
                //var response = await _restClient.GetAsync(request); TODO: Look into this to see if this is what I should be doing
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace($"Rest Response: {httpResponse.Content}");

                if (httpResponse.IsSuccessful)
                {
                    // TODO: Should I do something here?
                }
                else
                {
                    throw new Exception($"SOAP Response {httpResponse.Content} - (Code: {httpResponse.StatusCode})");
                }

            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_PostSetBinding: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP POST (SOAP): Get the certificate binding for the provided certificate usage
         */
        private string HTTP_PostGetBinding(Constants.CertificateUsage certUsage)
        {
            string boundCertAlias = "Unknown";

            try
            {
                Logger.MethodEntry();

                Logger.LogTrace(
                    $"Retrieving {Enum.GetName(typeof(Constants.CertificateUsage), certUsage)} cert binding");

                // Check if HTTP client was properly initialized
                if (_httpClient is null)
                {
                    throw new Exception("Axis IP Camera HTTP Client was not initialized");
                }

                var resource = $"/vapix/services";
                var request = new RestRequest(resource, Method.Post);
                request.AddHeader("Content-Type", "application/xml");
                Logger.LogDebug($"SOAP Request URI: {_httpClient.BuildUri(request)}");
                Logger.LogDebug($"HTTP Method: {Method.Post.ToString()}");

                Logger.LogTrace("Constructing request body...");

                string body = "";
                switch (certUsage)
                {
                    case Constants.CertificateUsage.Https:
                    {
                        body = @"<?xml version=""1.0"" encoding=""UTF-8""?> 
" + "\n" +
                               @"<Envelope xmlns=""http://www.w3.org/2003/05/soap-envelope""> 
" + "\n" +
                               @"<Header/> 
" + "\n" +
                               @"    <Body > 
" + "\n" +
                               @"        <GetWebServerTlsConfiguration xmlns=""http://www.axis.com/vapix/ws/webserver""> </GetWebServerTlsConfiguration> 
" + "\n" +
                               @"    </Body> 
" + "\n" +
                               @"</Envelope>";
                        break;

                    }
                    case Constants.CertificateUsage.IEEE:
                    {
                        body = @"<?xml version=""1.0"" encoding=""UTF-8""?> 
" + "\n" +
                               @"<Envelope xmlns=""http://www.w3.org/2003/05/soap-envelope""> 
" + "\n" +
                               @"<Header/>
" + "\n" +
                               @"   <Body >
" + "\n" +
                               @"       <GetDot1XConfiguration xmlns=""http://www.onvif.org/ver10/device/wsdl"">
" + "\n" +
                               @"           <Dot1XConfigurationToken>EAPTLS_WIRED</Dot1XConfigurationToken>
" + "\n" +
                               @"       </GetDot1XConfiguration>
" + "\n" +
                               @"   </Body>
" + "\n" +
                               @"</Envelope>";
                        break;
                    }
                    default:
                        break;
                }

                request.AddParameter("application/xml", body, ParameterType.RequestBody);

                Logger.LogTrace("Executing SOAP Request...");
                Logger.LogDebug($"SOAP Request body: {body}");
                var httpResponse = _httpClient.Execute(request);
                if (httpResponse is null)
                {
                    throw new InvalidOperationException();
                }

                Logger.LogTrace("SOAP Request completed");

                if (string.IsNullOrEmpty(httpResponse.Content))
                {
                    throw new Exception("No content returned from HTTP response");
                }
                else
                {
                    Logger.LogDebug($"SOAP Response: {httpResponse.Content}");
                }

                if (httpResponse.IsSuccessful)
                {
                    // Parse the response to retrieve the certificate alias
                    XmlDocument xdoc = new XmlDocument();
                    XmlNodeList xnodes = null;
                    xdoc.LoadXml(httpResponse.Content);

                    var tagName = "";
                    if (certUsage == Constants.CertificateUsage.Https)
                    {
                        tagName = Constants.HttpsAliasTagName;
                    }
                    else if (certUsage == Constants.CertificateUsage.IEEE)
                    {
                        tagName = Constants.IEEEAliasTagName;
                    }
                    else
                    {
                        throw new Exception("Unknown certificate usage. Unable to parse the SOAP response.");
                    }

                    // Extract the XML node identified by the tag name 
                    xnodes = xdoc.GetElementsByTagName(tagName);

                    if (xnodes is null)
                    {
                        throw new Exception(
                            $"Could not locate XML tag '{tagName}' in the SOAP response. Unable to extract the certificate alias.");
                    }

                    for (int i = 0; i < xnodes.Count; i++)
                    {
                        if (i > 0)
                        {
                            throw new Exception(
                                "More than 1 certificate alias was found in the SOAP response. This should never happen.");
                        }

                        boundCertAlias = xnodes[i].InnerXml;
                        Logger.LogDebug($"Bound Alias: {boundCertAlias}");
                    }
                }
                else
                {
                    throw new Exception($"SOAP Response {httpResponse.Content} - (Code: {httpResponse.StatusCode})");
                }

                Logger.MethodExit();

                return boundCertAlias;
            }
            catch (Exception e)
            {
                Logger.LogError(
                    $"Error Occured in AxisRestClient.HTTP_PostGetBinding: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /// <summary>
        /// Decodes the give HTTP status code into a human-readable string.
        /// These include some of the most common HTTP status codes.
        /// </summary>
        /// <param name="httpResponse"></param>
        /// <returns>Human-readable representation of the HTTP status code</returns>
        private string DecodeHTTPStatus(RestResponse httpResponse)
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
    }
}
