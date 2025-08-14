using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model
{
    public class RestApiResponse
    {
       [JsonProperty("status")] public Constants.Status Status { get; set; }
    }
    
    public class Error
    {
        [JsonProperty("code")] public int Code { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    public class ErrorData
    {
        [JsonProperty("status")] public Constants.Status Status { get; set; }
        [JsonProperty("error")] public Error ErrorInfo { get; set; }
    }

    public class CgiApiResponse
    {
        [JsonProperty("error")] public Error ErrorInfo { get; set; }
    }
    
}

