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


using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Shared SQL Server container fixture for all database integration tests.
/// This reduces test execution time by reusing a single Docker container across multiple test classes.
/// Each test class gets its own database within the shared container to ensure isolation.
/// </summary>
public sealed class SqlServerCollectionFixture : IAsyncLifetime
{
    private readonly MsSqlContainer msSqlContainer;
    private string? connectionString;
    private int databaseCounter = 0;

    public SqlServerCollectionFixture()
    {
        msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .WithReuse(true)  // Enable container reuse to avoid rebuilding
            .Build();
    }

    /// <summary>
    /// Gets the master connection string for the SQL Server container.
    /// </summary>
    public string MasterConnectionString => connectionString ?? throw new InvalidOperationException("Container has not been started yet.");

    public async ValueTask InitializeAsync()
    {
        await msSqlContainer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        connectionString = msSqlContainer.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        await msSqlContainer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new isolated database for a test class within the shared container.
    /// This ensures test isolation while sharing the same Docker container.
    /// </summary>
    /// <returns>A connection string to the newly created database.</returns>
    public async Task<string> CreateTestDatabaseAsync(string name)
    {
        if (connectionString == null)
        {
            throw new InvalidOperationException("Container has not been initialized. Ensure InitializeAsync has been called before creating databases.");
        }

        var dbNumber = Interlocked.Increment(ref databaseCounter);
        var dbName = $"TestDb_{dbNumber}_{name}_{Guid.NewGuid():N}";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand($"CREATE DATABASE [{dbName}]", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        // Build connection string for the new database
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = dbName
        };

        return builder.ConnectionString;
    }
}

/// <summary>
/// Defines the SQL Server collection that all database integration tests belong to.
/// Tests in this collection will share the same SQL Server container but get individual databases.
/// </summary>
[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerCollectionFixture>
{
    public const string Name = "SQL Server Collection";
}
