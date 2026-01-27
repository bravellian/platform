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

using Bravellian.Platform.Audit;

namespace Bravellian.Platform;

/// <summary>
/// Configuration options for PostgreSQL-backed audit events.
/// </summary>
public sealed class PostgresAuditOptions
{
    /// <summary>
    /// Gets or sets the database connection string.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the database schema name.
    /// Defaults to "infra".
    /// </summary>
    public string SchemaName { get; set; } = "infra";

    /// <summary>
    /// Gets or sets the table name for audit events.
    /// Defaults to "AuditEvents".
    /// </summary>
    public string AuditEventsTable { get; set; } = "AuditEvents";

    /// <summary>
    /// Gets or sets the table name for audit anchors.
    /// Defaults to "AuditAnchors".
    /// </summary>
    public string AuditAnchorsTable { get; set; } = "AuditAnchors";

    /// <summary>
    /// Gets or sets validation options for audit events.
    /// </summary>
    public AuditValidationOptions ValidationOptions { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether database schema deployment should be performed automatically.
    /// When true, the required database schema will be created/updated on startup.
    /// Defaults to true.
    /// </summary>
    public bool EnableSchemaDeployment { get; set; } = true;
}
