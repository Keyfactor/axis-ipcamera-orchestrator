// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model
{
    public class MqttResponse
    {
        [JsonProperty("apiVersion")] public string ApiVersion { get; set; }
        [JsonProperty("data")] public MqttData Data { get; set; }
    }

    public class MqttData
    {
        [JsonProperty("config")] public Config Config { get; set; }
    }
    
    public class Config
    {
        [JsonProperty("server")] public Server Server { get; set; }
        [JsonProperty("clientId")] public string ClientId { get; set; }
        [JsonProperty("cleanSession")] public bool CleanSession { get; set; }
        [JsonProperty("ssl")] public Ssl Ssl { get; set; }
    }
    
    public class Server
    {
        [JsonProperty("protocol")] public string Protocol { get; set; } = "ssl";
        [JsonProperty("host")] public string Host { get; set; }
    }
    
    public class Ssl
    {
        [JsonProperty("validateServerCert")] public bool ValidateServerCert { get; set; }
        [JsonProperty("clientCertID")] public string ClientCertId { get; set; }
    }

    public class MqttBody
    {
        [JsonProperty("apiVersion")] public string ApiVersion { get; set; }

        [JsonProperty("method")] public string Method { get; set; } = "configureClient";
        [JsonProperty("params")] public MqttParams Params { get; set; }
    }
    
    public class MqttParams
    {
        [JsonProperty("server")] public Server Server { get; set; }
        
        [JsonProperty("clientId")] public string ClientId { get; set; }
        [JsonProperty("cleanSession")] public bool CleanSession { get; set; }
        [JsonProperty("ssl")] public Ssl Ssl { get; set; }
    }
    
}