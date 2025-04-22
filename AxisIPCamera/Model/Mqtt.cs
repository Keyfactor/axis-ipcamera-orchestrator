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
        [JsonProperty("cleanSession")] public string CleanSession { get; set; }
        [JsonProperty("ssl")] public Ssl Ssl { get; set; }
    }
    
    public class Server
    {
        [JsonProperty("host")] public string Host { get; set; }
    }
    
    public class Ssl
    {
        [JsonProperty("validateServerCert")] public string ValidateServerCert { get; set; }
        [JsonProperty("clientCertID")] public string ClientCertId { get; set; }
    }

    public class MqttBody
    {
        [JsonProperty("apiVersion")] public string ApiVersion { get; set; }
        [JsonProperty("params")] public MqttParams Params { get; set; }
    }
    
    public class MqttParams
    {
        [JsonProperty("server")] public Server Server { get; set; }
        [JsonProperty("clientId")] public string ClientId { get; set; }
        [JsonProperty("cleanSession")] public string CleanSession { get; set; }
        [JsonProperty("ssl")] public Ssl Ssl { get; set; }
    }
    
}