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

using System.Reflection;

namespace Bravellian.Platform;

/// <summary>
/// Loads SQL scripts from embedded resources in the assembly.
/// </summary>
internal static class SqlResourceLoader
{
    private static readonly Assembly Assembly = typeof(SqlResourceLoader).Assembly;

    /// <summary>
    /// Gets the SQL script content from an embedded resource.
    /// </summary>
    /// <param name="resourcePath">The resource path (e.g., "Schema.MultiDatabase.Tables.Outbox.sql").</param>
    /// <returns>The SQL script content.</returns>
    public static string GetScript(string resourcePath)
    {
        var fullResourceName = $"Bravellian.Platform.{resourcePath}";
        
        using var stream = Assembly.GetManifestResourceStream(fullResourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded resource not found: {fullResourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets a table creation script from the MultiDatabase project.
    /// </summary>
    /// <param name="tableName">The table name without .sql extension.</param>
    /// <returns>The SQL script content.</returns>
    public static string GetMultiDatabaseTableScript(string tableName)
    {
        return GetScript($"Schema.MultiDatabase.Tables.{tableName}.sql");
    }

    /// <summary>
    /// Gets a stored procedure script from the MultiDatabase project.
    /// </summary>
    /// <param name="procedureName">The procedure name without .sql extension.</param>
    /// <returns>The SQL script content.</returns>
    public static string GetMultiDatabaseProcedureScript(string procedureName)
    {
        return GetScript($"Schema.MultiDatabase.StoredProcedures.{procedureName}.sql");
    }

    /// <summary>
    /// Gets a user-defined type script from the MultiDatabase project.
    /// </summary>
    /// <param name="typeName">The type name without .sql extension.</param>
    /// <returns>The SQL script content.</returns>
    public static string GetMultiDatabaseTypeScript(string typeName)
    {
        return GetScript($"Schema.MultiDatabase.Types.{typeName}.sql");
    }

    /// <summary>
    /// Gets the infra schema creation script from the MultiDatabase project.
    /// </summary>
    /// <returns>The SQL script content.</returns>
    public static string GetMultiDatabaseInfraSchemaScript()
    {
        return GetScript("Schema.MultiDatabase.Security.infra.sql");
    }

    /// <summary>
    /// Gets a table creation script from the ControlPlane project.
    /// </summary>
    /// <param name="tableName">The table name without .sql extension.</param>
    /// <returns>The SQL script content.</returns>
    public static string GetControlPlaneTableScript(string tableName)
    {
        return GetScript($"Schema.ControlPlane.Tables.{tableName}.sql");
    }

    /// <summary>
    /// Gets a stored procedure script from the ControlPlane project.
    /// </summary>
    /// <param name="procedureName">The procedure name without .sql extension.</param>
    /// <returns>The SQL script content.</returns>
    public static string GetControlPlaneProcedureScript(string procedureName)
    {
        return GetScript($"Schema.ControlPlane.StoredProcedures.{procedureName}.sql");
    }

    /// <summary>
    /// Gets the infra schema creation script from the ControlPlane project.
    /// </summary>
    /// <returns>The SQL script content.</returns>
    public static string GetControlPlaneInfraSchemaScript()
    {
        return GetScript("Schema.ControlPlane.Security.infra.sql");
    }
}
