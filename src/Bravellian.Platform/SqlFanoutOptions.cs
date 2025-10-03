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
/// Configuration options for SQL-based fanout services.
/// Specifies connection string and table names for fanout storage.
/// </summary>
public sealed class SqlFanoutOptions
{
    /// <summary>Gets or sets the database connection string.</summary>
    public required string ConnectionString { get; set; }

    /// <summary>Gets or sets the schema name for fanout tables.</summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>Gets or sets the table name for fanout policies.</summary>
    public string PolicyTableName { get; set; } = "FanoutPolicy";

    /// <summary>Gets or sets the table name for fanout cursors.</summary>
    public string CursorTableName { get; set; } = "FanoutCursor";

    /// <summary>Gets or sets a value indicating whether gets or sets whether to automatically deploy database schema.</summary>
    public bool EnableSchemaDeployment { get; set; } = true;
}
