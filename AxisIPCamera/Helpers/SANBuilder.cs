// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Helpers
{
    public static class SANBuilder
    {
        public static List<string> BuildSANString(Dictionary<string, string[]> sans, ILogger logger)
        {
            var parts = new List<string>();
            
            if (sans == null || sans.Count == 0)
            {
                logger.LogTrace($"SANs is null or empty");
                return parts;
            }

            foreach (var entry in sans)
            {
                string key = NormalizeSanKey(entry.Key);
                
                // The Axis API only supports the addition of 'dns' and 'ip' SAN type keys
                if (key is not ("DNS" or "IP"))
                    continue;
                
                if (entry.Value == null || entry.Value.Length == 0)
                        continue;
                
                // NOTE: We are separating the key and value pairs with a colon because this is the format
                // required to send SANs to the Axis API endpoint
                parts.AddRange(
                    entry.Value
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => $@"""{key}:{v.Trim()}""")
                    );
            }

            return parts;
        }

        /// <summary>
        /// Normalize SAN type keys to RFC-compliant names.
        /// **NOTE: The Axis API only supports the addition of 'dns' and 'ip' SAN types.
        /// Courtesy of B.Pokorny.
        /// </summary>
        private static string NormalizeSanKey(string key)
        {
            return key.Trim().ToLower() switch
            {
                "dns" => "DNS",
                "ip" or "ip4" or "ip6" => "IP",
                _ => key.ToLower() // default
            };
        }
    }
}