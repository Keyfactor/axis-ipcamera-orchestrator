using System.Collections.Generic;
using Newtonsoft.Json;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model
{
    /* Trust Certificates */
    public class CACertificate
    {
        [JsonProperty("alias")] public string Alias { get; set; }
        [JsonProperty("certificate")] public string CertAsPem { get; set; }
    }

    public class CACertificateData
    {
        [JsonProperty("status")] public Constants.Status Status { get; set; }
        [JsonProperty("data")] public List<CACertificate> CACerts { get; set; }
    }
    
    /* Client Certificates */
    public class Certificate
    {
        [JsonProperty("alias")] public string Alias { get; set; }
        [JsonProperty("certificate")] public string CertAsPem { get; set; }
        [JsonProperty("keystore")] public Constants.Keystore Keystore { get; set; }
        public Constants.CertificateUsage Binding { get; set; } = Constants.CertificateUsage.Other;
    }

    public class CertificateData
    {
        [JsonProperty("status")] public Constants.Status Status { get; set; }
        [JsonProperty("data")] public List<Certificate> Certs { get; set; }
    }
    
    /* Self-Signed Certificate */
    public class SelfSignedCertificate
    {
        [JsonProperty("alias")] public string Alias { get; set; }
        [JsonProperty("key_type")] public string KeyType{ get; set; }
        [JsonProperty("keystore")] public string Keystore { get; set; }
        [JsonProperty("subject")] public string Subject { get; set; }
        [JsonProperty("subject_alt_names")] public string[] SANS { get; set; }
        [JsonProperty("valid_from")] public int ValidFrom { get; set; }
        [JsonProperty("valid_to")] public int ValidTo { get; set; }
    }
    
    public class SelfSignedCertificateData
    {
        [JsonProperty("data")] public SelfSignedCertificate Cert { get; set; }
    }
    
    public class CSRData
    {
        [JsonProperty("status")] public Constants.Status Status { get; set; }
        [JsonProperty("data")] public string CSR { get; set; }
    }
}