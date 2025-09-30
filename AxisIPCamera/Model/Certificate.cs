// Copyright 2025 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

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