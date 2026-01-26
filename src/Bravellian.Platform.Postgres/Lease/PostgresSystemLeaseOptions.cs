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
/// Configuration options for system leases.
/// </summary>
public class PostgresSystemLeaseOptions
{
    /// <summary>
    /// Gets or sets the connection string for the distributed lock database.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the schema name for the distributed lock table.
    /// Default is "infra".
    /// </summary>
    public string SchemaName { get; set; } = "infra";

    /// <summary>
    /// Gets or sets the default lease duration for new leases.
    /// Default is 30 seconds.
    /// </summary>
    public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the percentage of the lease duration at which renewal should occur.
    /// Default is 0.6 (60%).
    /// </summary>
    public double RenewPercent { get; set; } = 0.6;

    /// <summary>
    /// Gets or sets a value indicating whether to use a short advisory-lock gate to reduce contention.
    /// Default is false.
    /// </summary>
    public bool UseGate { get; set; }

    /// <summary>
    /// Gets or sets the timeout in milliseconds for the advisory-lock gate.
    /// Default is 200ms.
    /// </summary>
    public int GateTimeoutMs { get; set; } = 200;

    /// <summary>
    /// Gets or sets a value indicating whether database schema deployment should be performed automatically.
    /// When true, the required database schema will be created/updated on startup.
    /// Defaults to true.
    /// </summary>
    public bool EnableSchemaDeployment { get; set; } = true;
}





