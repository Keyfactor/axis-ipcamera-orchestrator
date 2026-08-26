// Copyright 2026 Keyfactor
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance with the License.
// You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions
// and limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyfactor.Extensions.Orchestrator.AxisIPCamera.Model
{
    public sealed class HttpContext
    {
        private readonly List<string> _warnings = new();
        private readonly List<string> _errors = new();
        
        public IReadOnlyList<string> Warnings => _warnings;
        public IReadOnlyList<string> Errors => _errors;

        public void AddWarning(string message) =>
            _warnings.Add(message);

        public void AddError(string message) =>
            _errors.Add(message);

        public HttpResult ToResult()
        {
            if (_errors.Any())
                return HttpResult.Error(FormatMessages(_errors));

            if (_warnings.Any())
                return HttpResult.Warning(FormatMessages(_warnings));

            return HttpResult.Success();
        }

        private static string FormatMessages(IEnumerable<string> messages)
        {
            return string.Join(
                Environment.NewLine,
                messages.Select((message, index) =>
                    $"({index + 1}) {message}")
            );
        }
    }

    public enum HttpStatus
    {
        Success,
        Warning,
        Error
    }

    public sealed record HttpResult(HttpStatus Status, string Message = null)
    {
        public static HttpResult Success()
            => new(HttpStatus.Success);

        public static HttpResult Warning(string message)
            => new(HttpStatus.Warning, message);

        public static HttpResult Error(string message)
            => new(HttpStatus.Error, message);

    }
}