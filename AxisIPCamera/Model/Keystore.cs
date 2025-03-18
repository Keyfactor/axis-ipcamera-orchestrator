using System.Collections.Generic;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model
{
    public class KeystoreData
    {
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("data")] public Constants.Keystore Keystore { get; set; }
    }
}