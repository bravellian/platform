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
/// Configuration options for the platform control plane database.
/// </summary>
public sealed class PlatformControlPlaneOptions
{
    /// <summary>
    /// Gets or initializes the connection string for the control plane database.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Gets or initializes the schema name for platform tables in the control plane database (default: "infra").
    /// </summary>
    public string SchemaName { get; init; } = "infra";

    /// <summary>
    /// Gets or initializes whether to automatically create platform tables and procedures at startup.
    /// </summary>
    public bool EnableSchemaDeployment { get; init; }
}
