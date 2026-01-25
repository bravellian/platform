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

namespace Bravellian.Platform;

/// <summary>
/// Internal configuration state tracking platform registration.
/// </summary>
internal sealed class PlatformConfiguration
{
    /// <summary>
    /// Gets the selected environment style.
    /// </summary>
    public required PlatformEnvironmentStyle EnvironmentStyle { get; init; }

    /// <summary>
    /// Gets whether database discovery is used (true) or a static list (false).
    /// </summary>
    public required bool UsesDiscovery { get; init; }

    /// <summary>
    /// Gets the control plane connection string if configured.
    /// </summary>
    public string? ControlPlaneConnectionString { get; init; }

    /// <summary>
    /// Gets the control plane schema name if configured (default: "infra").
    /// </summary>
    public string? ControlPlaneSchemaName { get; init; }

    /// <summary>
    /// Gets whether schema deployment is enabled for platform tables.
    /// </summary>
    public bool EnableSchemaDeployment { get; init; }

    /// <summary>
    /// Gets whether at least one database is required at startup.
    /// True for static list-based configurations (throw exception if no databases).
    /// False for dynamic discovery configurations (allow zero databases initially).
    /// </summary>
    public bool RequiresDatabaseAtStartup { get; init; }
}





