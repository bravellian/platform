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
using Bravellian.Platform.Email;
using Bravellian.Platform.Metrics;
using Bravellian.Platform.Operations;

namespace Bravellian.Platform;

/// <summary>
/// Configuration options for registering the Postgres platform stack in one call.
/// </summary>
public sealed class PostgresPlatformOptions
{
    /// <summary>
    /// Gets or sets the PostgreSQL connection string.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the schema name (default: "infra").
    /// </summary>
    public string SchemaName { get; set; } = "infra";

    /// <summary>
    /// Gets or sets a value indicating whether schema deployment should run at startup.
    /// </summary>
    public bool EnableSchemaDeployment { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether schema verification should run at startup.
    /// </summary>
    public bool EnableSchemaVerification { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether scheduler background workers should run.
    /// </summary>
    public bool EnableSchedulerWorkers { get; set; } = true;

    /// <summary>Optional outbox options customization.</summary>
    public Action<PostgresOutboxOptions>? ConfigureOutbox { get; set; }

    /// <summary>Optional inbox options customization.</summary>
    public Action<PostgresInboxOptions>? ConfigureInbox { get; set; }

    /// <summary>Optional scheduler options customization.</summary>
    public Action<PostgresSchedulerOptions>? ConfigureScheduler { get; set; }

    /// <summary>Optional fanout options customization.</summary>
    public Action<PostgresFanoutOptions>? ConfigureFanout { get; set; }

    /// <summary>Optional idempotency options customization.</summary>
    public Action<PostgresIdempotencyOptions>? ConfigureIdempotency { get; set; }

    /// <summary>Optional metrics exporter options customization.</summary>
    public Action<PostgresMetricsExporterOptions>? ConfigureMetrics { get; set; }

    /// <summary>Optional audit options customization.</summary>
    public Action<PostgresAuditOptions>? ConfigureAudit { get; set; }

    /// <summary>Optional operations options customization.</summary>
    public Action<PostgresOperationOptions>? ConfigureOperations { get; set; }

    /// <summary>Optional email outbox options customization.</summary>
    public Action<PostgresEmailOutboxOptions>? ConfigureEmailOutbox { get; set; }

    /// <summary>Optional email delivery options customization.</summary>
    public Action<PostgresEmailDeliveryOptions>? ConfigureEmailDelivery { get; set; }
}
