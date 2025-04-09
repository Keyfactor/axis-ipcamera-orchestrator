namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model
{
    public static class Constants
    {
        // This is the Name of the Entry Parameter used to identify the certificate usage for each certificate on the camera
        public static string CertUsageParamName = "CertUsage";
        
        // This is the XML tag identifier for the cert alias bound to the TLS web server on the camera
        public static string HttpsAliasTagName = "acert:Id";
        
        // This is the XML tag identifier for the cert alias bound to the IEEE802.X network access control on the camera
        public static string IEEEAliasTagName = "tt:CertificateID";
        
        public enum CertificateUsage
        {
            Https,
            IEEE,
            MQTT,
            Trust,
            None
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
