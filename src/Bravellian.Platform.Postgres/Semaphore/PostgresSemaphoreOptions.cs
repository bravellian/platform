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

namespace Bravellian.Platform.Semaphore;

/// <summary>
/// Configuration options for semaphore behavior.
/// </summary>
public sealed class PostgresSemaphoreOptions
{
    /// <summary>
    /// Gets or sets the minimum allowed TTL in seconds (default: 1).
    /// </summary>
    public int MinTtlSeconds { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum allowed TTL in seconds (default: 3600 = 1 hour).
    /// </summary>
    public int MaxTtlSeconds { get; set; } = 3600;

    /// <summary>
    /// Gets or sets the default TTL in seconds used by renewal helpers (default: 30).
    /// </summary>
    public int DefaultTtlSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum allowed limit per semaphore (default: 10000).
    /// </summary>
    public int MaxLimit { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the reaper cadence in seconds (default: 30).
    /// </summary>
    public int ReaperCadenceSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum number of rows to delete per reaper iteration (default: 1000).
    /// </summary>
    public int ReaperBatchSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the connection string for the semaphore database.
    /// For control-plane modes, this is the control plane connection string.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the schema name for semaphore tables (default: "infra").
    /// </summary>
    public string SchemaName { get; set; } = "infra";
}





