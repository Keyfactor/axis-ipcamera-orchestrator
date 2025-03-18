using System;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using Keyfactor.Logging;
using Keyfactor.Orchestrators.Extensions;
using Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Client
{

    public class AxisSoapClient
    {
        private const string ApiEntryPoint = "vapix/services";
        
        private readonly string _baseRestClientUrl;
        private readonly string _username;
        private readonly string _password;
        private string HTTPCert;
        private string IEEECert;
        private string MQTTCert;
        private ILogger Logger { get; }

        public AxisSoapClient(JobConfiguration config, CertificateStore store)
        {
            try
            {
                Logger = LogHandler.GetClassLogger<AxisSoapClient>();
                
                Logger.MethodEntry();
                Logger.LogTrace("Initializing Axis IP Camera Soap Client");

                // TODO: Need to consider onboarding of camera
                _baseRestClientUrl = (config.UseSSL) ? $"https://{store.ClientMachine}" : $"http://{store.ClientMachine}";
                //_baseRestClientUrl = $"https://{store.ClientMachine}";
                _username = config.ServerUsername;
                _password = config.ServerPassword;
                
                Logger.LogDebug($"Base Rest Client URL: {_baseRestClientUrl}");

                // Initialize a REST client with the Basic Auth username and password credentials
                Logger.LogDebug("Adding Basic Auth Credentials to the client options");
               /* var options = new RestClientOptions(_baseRestClientUrl + "/" + ApiEntryPoint)
                {
                    Authenticator = new HttpBasicAuthenticator(_username, _password)
                };
                
                Logger.LogDebug("Turning off SSL validation --- FOR TESTING ONLY");
                options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                
                Logger.LogDebug($"Username: {_username}, Password: {_password}");
                _restClient = new RestClient(options);
                
                Logger.LogTrace("Completed Initialization of Axis IP Camera Rest Client");
                
                Logger.MethodExit();*/
            }
            catch (Exception e)
            {
                Logger.LogError("Error in Constructor AxisRestClient(): " + LogHandler.FlattenException(e));
                throw;
            }
        }
    }
}