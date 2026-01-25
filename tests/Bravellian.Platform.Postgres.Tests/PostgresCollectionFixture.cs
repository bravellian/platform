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

using Npgsql;
using Testcontainers.PostgreSql;

namespace Bravellian.Platform.Tests;

/// <summary>
/// Shared PostgreSQL container fixture for all database integration tests.
/// This reduces test execution time by reusing a single Docker container across multiple test classes.
/// Each test class gets its own database within the shared container to ensure isolation.
/// </summary>
public sealed class PostgresCollectionFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgresContainer;
    private string? connectionString;
    private int databaseCounter;

    public PostgresCollectionFixture()
    {
        postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithReuse(true)
            .Build();
    }

    /// <summary>
    /// Gets the master connection string for the PostgreSQL container.
    /// </summary>
    public string MasterConnectionString => connectionString ?? throw new InvalidOperationException("Container has not been started yet.");

    public async ValueTask InitializeAsync()
    {
        await postgresContainer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var builder = new NpgsqlConnectionStringBuilder(postgresContainer.GetConnectionString())
        {
            Pooling = false,
        };
        connectionString = builder.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        await postgresContainer.DisposeAsync().ConfigureAwait(false);
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
        var dbName = $"test_{dbNumber}_{name}_{Guid.NewGuid():N}".ToLowerInvariant();

        var masterBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        await using var connection = new NpgsqlConnection(masterBuilder.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        var dbBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = dbName,
            Pooling = false,
        };

        return dbBuilder.ConnectionString;
    }
}


