namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model
{
    public static class Constants
    {
        public enum CertificateUsage
        {
            Https,
            IEEE,
            MQTT,
            Trust,
            Unknown
        }

        // Note: There may be more keystore types depending on the Axis camera model
        public enum Keystore
        {
            TEE0, // Trusted Environment
            SE0 // Secure Element
        }

        public enum KeyTypes
        {
            UNKNOWN,
            ECP256,
            ECP384,
            ECP521,
            RSA2048,
            RSA4096
        }
        
        public enum Status
        {
            Success,
            Error
        }
        
        public static string MapKeyType(string keyAlgorithm, string keySize)
        {
            return keyAlgorithm switch
            {
                "RSA" when keySize == "2048" => "RSA-2048",
                "RSA" when keySize == "4096" => "RSA-4096",
                "ECP" when keySize == "256" => "ECP-256",
                "ECP" when keySize == "384" => "ECP-384",
                "ECP" when keySize == "521" => "ECP-521",
                _ => "UNKNOWN"
            };
        }
    }
    
}
