// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Captures a module health check for hosts that do not wire ASP.NET Core.
/// </summary>
/// <param name="Name">The health check name.</param>
/// <param name="Check">The health check.</param>
/// <param name="Tags">Tags associated with the check.</param>
public sealed record ModuleHealthCheckRegistration(string Name, Func<HealthCheckResult> Check, IReadOnlyCollection<string> Tags);
