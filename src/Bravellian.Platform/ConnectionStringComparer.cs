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

using Microsoft.Data.SqlClient;

/// <summary>
/// Provides utility methods for comparing SQL Server connection strings.
/// </summary>
internal static class ConnectionStringComparer
{
    /// <summary>
    /// Compares two SQL Server connection strings for equivalence by parsing and comparing
    /// their components (server, database, etc.) rather than doing string comparison.
    /// This handles semantically equivalent but textually different connection strings
    /// (e.g., "Data Source=localhost" vs "Server=localhost", parameter order differences).
    /// </summary>
    /// <param name="connectionString1">The first connection string to compare.</param>
    /// <param name="connectionString2">The second connection string to compare.</param>
    /// <returns>True if the connection strings are equivalent; otherwise, false.</returns>
    public static bool AreEquivalent(string? connectionString1, string? connectionString2)
    {
        if (string.IsNullOrWhiteSpace(connectionString1) && string.IsNullOrWhiteSpace(connectionString2))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(connectionString1) || string.IsNullOrWhiteSpace(connectionString2))
        {
            return false;
        }

        try
        {
            var builder1 = new SqlConnectionStringBuilder(connectionString1);
            var builder2 = new SqlConnectionStringBuilder(connectionString2);

            // Compare the key components that define database identity
            return string.Equals(builder1.DataSource, builder2.DataSource, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(builder1.InitialCatalog, builder2.InitialCatalog, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // If parsing fails, fall back to exact string comparison
            return string.Equals(connectionString1, connectionString2, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Checks if the given database is the control plane database by comparing its connection string
    /// with the control plane connection string from configuration.
    /// </summary>
    /// <param name="database">The database to check.</param>
    /// <param name="platformConfiguration">The platform configuration containing the control plane connection string.</param>
    /// <returns>True if the database is the control plane database; otherwise, false.</returns>
    public static bool IsControlPlaneDatabase(PlatformDatabase database, PlatformConfiguration? platformConfiguration)
    {
        if (platformConfiguration == null ||
            string.IsNullOrEmpty(platformConfiguration.ControlPlaneConnectionString))
        {
            return false;
        }

        return AreEquivalent(database.ConnectionString, platformConfiguration.ControlPlaneConnectionString);
    }
}
