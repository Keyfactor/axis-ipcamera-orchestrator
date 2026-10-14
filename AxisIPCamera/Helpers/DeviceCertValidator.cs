// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

#nullable enable
using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Helpers
{
    public static class DeviceCertValidator
    {
        /// <summary>
        /// This method is a custom HTTP validator that performs the following logic:
        /// 1. If the only validation problem is a hostname/SAN mismatch - the certificate otherwise
        ///    chains to a CA the orchestrator server trusts - and serialNumber is provided, checks
        ///    the certificate's SERIALNUMBER subject field for that value instead. This covers the
        ///    camera's factory certificate, whose identity is tied to its SERIALNUMBER rather than
        ///    wherever it is actually deployed today. Chain validation is never relaxed by this check
        ///    - the certificate still has to chain to a trusted CA.
        /// 2. Otherwise, checks the cert against standard .NET SSL policy errors, recording specific,
        ///    human-readable error messages in errorContext so they can be surfaced to the end user
        ///    (via DeviceCertValidationException) rather than a generic pass/fail.
        /// </summary>
        public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> GetValidator(
            string? serialNumber, CertificateErrorContext errorContext, ILogger logger)
        {
            return (message, cert, chain, sslPolicyErrors) =>
            {
                if (null == cert)
                {
                    errorContext.Add("SSL Cert is null");
                    return false;
                }

                if (null == chain)
                {
                    errorContext.Add("Server Cert Chain is null");
                    return false;
                }

                logger.LogTrace("Verifying cert against default system validation...");

                bool certNotAvailable = sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable);
                bool nameMismatch = sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch);
                bool chainErrors = sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors);

                if (nameMismatch && !chainErrors && !certNotAvailable && !string.IsNullOrWhiteSpace(serialNumber))
                {
                    if (MatchesSerialNumber(cert, serialNumber, logger))
                    {
                        logger.LogDebug($"Certificate SERIALNUMBER matches the recorded Store Path value '{serialNumber}' - treating the hostname mismatch as expected for the camera's factory certificate.");
                        nameMismatch = false;
                    }
                    else
                    {
                        errorContext.Add($"SERIALNUMBER attribute value does NOT match the expected value '{serialNumber}'.");
                    }
                }

                bool sslErrors = false;
                if (certNotAvailable)
                {
                    sslErrors = true;
                    errorContext.Add("The server did not provide a certificate.");
                }
                if (nameMismatch)
                {
                    sslErrors = true;
                    errorContext.Add("The device hostname does not match the CN or SAN in the server's TLS certificate.");
                }
                if (chainErrors)
                {
                    sslErrors = true;
                    chain.Build(cert);

                    foreach (var status in chain.ChainStatus)
                    {
                        errorContext.Add($"Chain status: {status.Status} - {status.StatusInformation}");
                    }
                }

                if (sslErrors)
                {
                    errorContext.Insert(0, "TLS Cert validation failed!!");
                    return false;
                }

                logger.LogDebug("Certificate chain and subject validated!!");
                return true;
            };
        }

        private const string SerialNumberPrefix = "SERIALNUMBER=";

        private static bool MatchesSerialNumber(X509Certificate2 cert, string serialNumber, ILogger logger)
        {
            var subjectDn = new X500DistinguishedName(cert.SubjectName);
            var decodedSubject = subjectDn.Decode(X500DistinguishedNameFlags.UseNewLines);
            var subjectLines = decodedSubject.Split('\n');

            foreach (var line in subjectLines)
            {
                if (line.StartsWith(SerialNumberPrefix))
                {
                    var snValue = line[SerialNumberPrefix.Length..].Trim();
                    logger.LogDebug($"Found SERIALNUMBER: {snValue}");
                    return string.Equals(snValue, serialNumber, StringComparison.Ordinal);
                }
            }

            logger.LogTrace("SERIALNUMBER attribute was not found in the certificate Subject DN");
            return false;
        }
    }
}
