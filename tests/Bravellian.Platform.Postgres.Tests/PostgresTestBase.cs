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

using Testcontainers.PostgreSql;

namespace Bravellian.Platform.Tests;

/// <summary>
/// Base test class that provides a PostgreSQL TestContainer for integration testing.
/// Automatically manages the container lifecycle and database schema setup.
/// When used with the PostgresCollection, shares a single container across multiple test classes.
/// </summary>
public abstract class PostgresTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer? postgresContainer;
    private readonly PostgresCollectionFixture? sharedFixture;
    private string? connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresTestBase"/> class with a standalone container.
    /// </summary>
    protected PostgresTestBase(ITestOutputHelper testOutputHelper)
    {
        postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
            .Build();

        TestOutputHelper = testOutputHelper;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresTestBase"/> class with a shared container.
    /// This constructor is used when the test class is part of the PostgresCollection.
    /// </summary>
    protected PostgresTestBase(ITestOutputHelper testOutputHelper, PostgresCollectionFixture sharedFixture)
    {
        this.sharedFixture = sharedFixture;
        TestOutputHelper = testOutputHelper;
    }

    protected ITestOutputHelper TestOutputHelper { get; }

    /// <summary>
    /// Gets the connection string for the running PostgreSQL container.
    /// Only available after InitializeAsync has been called.
    /// </summary>
    protected string ConnectionString => connectionString ?? throw new InvalidOperationException("Container has not been started yet. Make sure InitializeAsync has been called.");

    public virtual async ValueTask InitializeAsync()
    {
        if (sharedFixture != null)
        {
            connectionString = await sharedFixture.CreateTestDatabaseAsync("shared").ConfigureAwait(false);
        }
        else
        {
            await postgresContainer!.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(postgresContainer.GetConnectionString())
            {
                Pooling = false,
            };
            connectionString = builder.ConnectionString;
        }

        await SetupDatabaseSchema().ConfigureAwait(false);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (postgresContainer != null)
        {
            await postgresContainer.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sets up the required database schema for the Platform components.
    /// </summary>
    private async Task SetupDatabaseSchema()
    {
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(ConnectionString, "infra", "Outbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureInboxSchemaAsync(ConnectionString, "infra", "Inbox").ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(ConnectionString, "infra", "Jobs", "JobRuns", "Timers").ConfigureAwait(false);
    }
}


