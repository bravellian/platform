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
/// Configuration options for work queue clients.
/// </summary>
public class WorkQueueOptions
{
    /// <summary>
    /// Gets or sets the database connection string.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the database schema name.
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Gets or sets the table name for the work queue.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary key column name.
    /// </summary>
    public string PrimaryKeyColumn { get; set; } = "Id";

    /// <summary>
    /// Gets or sets the ordering column name for fair processing.
    /// </summary>
    public string OrderingColumn { get; set; } = "CreatedAt";

    /// <summary>
    /// Gets or sets additional WHERE clause conditions for claim operations.
    /// </summary>
    public string? AdditionalClaimFilters { get; set; }

    /// <summary>
    /// Gets or sets the stored procedure name prefix.
    /// </summary>
    public string ProcedureNamePrefix { get; set; } = string.Empty;
}