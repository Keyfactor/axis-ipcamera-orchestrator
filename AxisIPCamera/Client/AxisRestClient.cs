using System;
using System.Collections.Generic;
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


namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client
{
    public class AxisRestClient
    {
        private const string ApiEntryPoint = "config/rest/cert/v1beta";
        
        private readonly string _baseRestClientUrl;
        private readonly string _username;
        private readonly string _password;
        private RestClient _restClient;
        private ILogger Logger { get; }

        public AxisRestClient(JobConfiguration config, CertificateStore store)
        {
            // TODO: Do a null check on the Rest client
            try
            {
                Logger = LogHandler.GetClassLogger<AxisRestClient>();
                
                Logger.MethodEntry();
                Logger.LogTrace("Initializing Axis IP Camera Rest Client");

                // TODO: Need to consider onboarding of camera
                _baseRestClientUrl = (config.UseSSL) ? $"https://{store.ClientMachine}" : $"http://{store.ClientMachine}";
                //_baseRestClientUrl = $"https://{store.ClientMachine}";
                _username = config.ServerUsername;
                _password = config.ServerPassword;
                
                Logger.LogDebug($"Base Rest Client URL: {_baseRestClientUrl}");

                // Initialize a REST client with the Basic Auth username and password credentials
                Logger.LogDebug("Adding Basic Auth Credentials to the client options");
                var options = new RestClientOptions(_baseRestClientUrl + "/" + ApiEntryPoint)
                {
                    Authenticator = new HttpBasicAuthenticator(_username, _password)
                };
                
                Logger.LogDebug("Turning off SSL validation --- FOR TESTING ONLY");
                options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                
                Logger.LogDebug($"Username: {_username}, Password: {_password}");
                _restClient = new RestClient(options);
                
                Logger.LogTrace("Completed Initialization of Axis IP Camera Rest Client");
                
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
        /// Gathers all the CA certificates on the device 
        /// </summary>
        /// <returns></returns>
        public CACertificateData ListCACertificates()
        {
            var certsFound = new CACertificateData();
            
            try
            {
                Logger.MethodEntry();
                certsFound = HTTP_GetCACertificates();

            }
            /*catch (ApigeeException ex1)
            {
                Logger.LogError("Error completing certificate inventory: " + LogHandler.FlattenException(ex1));
                throw new Exception(ex1.Message);
            }*/
            catch (Exception ex2)
            {
                Logger.LogError("Error completing CA certificate inventory: " + LogHandler.FlattenException(ex2));
                throw new Exception(ex2.Message);
            }

            Logger.MethodExit();
            return certsFound;
        }
        
        /// <summary>
        /// Gathers all the certificates on the device 
        /// </summary>
        /// <returns></returns>
        public CertificateData ListCertificates()
        {
            var certsFound = new CertificateData();
            
            try
            {
                Logger.MethodEntry();
                certsFound = HTTP_GetCertificates();

            }
            /*catch (ApigeeException ex1)
            {
                Logger.LogError("Error completing certificate inventory: " + LogHandler.FlattenException(ex1));
                throw new Exception(ex1.Message);
            }*/
            catch (Exception ex2)
            {
                Logger.LogError("Error completing certificate inventory: " + LogHandler.FlattenException(ex2));
                throw new Exception(ex2.Message);
            }

            Logger.MethodExit();
            return certsFound;
        }

        /// <summary>
        /// 
        /// </summary>
        public Constants.Keystore GetDefaultKeystore()
        {
            try
            {
                Logger.MethodEntry();
                var defaultKeystore = HTTP_GetDefaultKeystore();

                Logger.MethodExit();
                
                return defaultKeystore.Keystore;
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing certificate reenrollment: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }

        public void CreateSelfSignedCert(string alias, string keyType, string keystore, string subject, string[] sans)
        {
            try
            {
                Logger.MethodEntry();
                
                // Compose the self-signed cert body
                SelfSignedCertificateData ssc = new SelfSignedCertificateData();
                ssc.Cert = new SelfSignedCertificate();
                ssc.Cert.Alias = alias;
                ssc.Cert.KeyType = keyType;
                ssc.Cert.Keystore = keystore;
                ssc.Cert.Subject = subject;
                ssc.Cert.SANS = sans;
                ssc.Cert.ValidFrom = 0; // TODO: Make this a parameter, but for now, the cert validity period will be determined by the template, which it may overwrite this setting anyway
                ssc.Cert.ValidTo = 0; // TODO: Make this a parameter, but for now, the cert validity period will be determined by the template, which it may overwrite this setting anyway

                string jsonBody = JsonConvert.SerializeObject(ssc);
                Logger.LogDebug($"Self-signed cert JSON body: {jsonBody}");

                HTTP_PostCreateCertificate(jsonBody);
                
                Logger.MethodExit();
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing certificate reenrollment: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }
        
        public string ObtainCSR(string alias)
        {
            try
            {
                Logger.MethodEntry();
                
                // Compose the body
                string jsonBody = @"{""data"":{}}";
                
                Logger.LogDebug($"CSR JSON body: {jsonBody}");

                Logger.MethodExit();
                
                return HTTP_PostObtainCSR(alias,jsonBody);
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing certificate reenrollment: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }
        
        public void ReplaceCertificate(string alias,string pemCert)
        {
            try
            {
                Logger.MethodEntry();
                
                // Compose the cert body
                string jsonBody = @"{""data"":{""certificate"":""" + pemCert + @"""}}";
                Logger.LogDebug($"Pem cert JSON body: {jsonBody}");

                HTTP_PostReplaceCertificate(alias,jsonBody);
                
                Logger.MethodExit();
            }
            catch (Exception e)
            {
                Logger.LogError("Error completing certificate reenrollment: " + LogHandler.FlattenException(e));
                throw new Exception(e.Message);
            }
        }
        
        
        // Axis REST Calls
        /**
         * HTTP GET: Retrieve a list of CA certificates
         */
        private CACertificateData HTTP_GetCACertificates()
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = "/ca_certificates";
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
                Logger.LogError($"Error Occured in AxisRestClient.HTTP_GetCACertificates: {LogHandler.FlattenException(e)}");
                throw;
            }
        }

        /**
         * HTTP GET: Retrieve a list of certificates
         */
        private CertificateData HTTP_GetCertificates()
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = "/certificates";
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
                Logger.LogError($"Error Occured in AxisRestClient.HTTP_GetCertificates: {LogHandler.FlattenException(e)}");
                throw;
            }
        }
        
        /**
         * HTTP GET: Retrieve default keystore 
         */
        private KeystoreData HTTP_GetDefaultKeystore()
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = "/settings/keystore";
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
                Logger.LogError($"Error Occured in AxisRestClient.HTTP_GetDefaultKeystore: {LogHandler.FlattenException(e)}");
                throw;
            }
        }
        
        /**
         * HTTP POST: Retrieve a list of certificates
         */
        private void HTTP_PostCreateCertificate(string jsonBody)
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = "create_certificate";
                var request = new RestRequest(resource,Method.Post);
                request.RequestFormat = DataFormat.Json;
                
                Logger.LogTrace($"Rest Request: {_restClient.BuildUri(request)}");
                
                // Add the certificate body
                request.AddJsonBody(jsonBody);

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
                Logger.LogError($"Error Occured in AxisRestClient.HTTP_PostCreateCertificate: {LogHandler.FlattenException(e)}");
                throw;
            }
        }
        
        /**
         * HTTP POST: Obtain CSR from self-signed cert generated on the device
         */
        private string HTTP_PostObtainCSR(string alias, string jsonBody)
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"certificates/{alias}/get_csr";
                var request = new RestRequest(resource,Method.Post);
                request.RequestFormat = DataFormat.Json;
                
                Logger.LogTrace($"Rest Request: {_restClient.BuildUri(request)}");
                
                // Add the certificate body
                request.AddJsonBody(jsonBody);

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
                Logger.LogError($"Error Occured in AxisRestClient.HTTP_PostObtainCSR: {LogHandler.FlattenException(e)}");
                throw;
            }
        }
        
        /**
         * HTTP POST: Replace certificate but keep private key
         */
        private void HTTP_PostReplaceCertificate(string alias, string jsonBody)
        {
            try
            {
                Logger.MethodEntry();
                if (_restClient is null)
                {
                    throw new Exception("Axis IP Camera Rest Client was not initialized");
                }
                //_restClient ??= new RestClient(_baseRestClientUrl);

                var resource = $"certificates/{alias}";
                var request = new RestRequest(resource,Method.Patch);
                request.RequestFormat = DataFormat.Json;
                
                Logger.LogTrace($"Rest Request: {_restClient.BuildUri(request)}");
                
                // Add the certificate body
                request.AddJsonBody(jsonBody);

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
                Logger.LogError($"Error Occured in AxisRestClient.HTTP_PostReplaceCertificate: {LogHandler.FlattenException(e)}");
                throw;
            }
        }
    }
}
