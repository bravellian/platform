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

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Background service that verifies the expected platform schema exists at startup.
/// </summary>
internal sealed class DatabaseSchemaVerificationService : BackgroundService
{
    private static readonly ColumnSpec[] OutboxColumns =
    [
        new ColumnSpec("CreatedOn", "datetimeoffset", IsNullable: false),
        new ColumnSpec("ProcessedOn", "datetimeoffset", IsNullable: true),
        new ColumnSpec("AttemptCount", "int", IsNullable: false),
        new ColumnSpec("DueOn", "datetimeoffset", IsNullable: true),
    ];

    private static readonly ColumnSpec[] InboxColumns =
    [
        new ColumnSpec("CreatedOn", "datetimeoffset", IsNullable: false),
        new ColumnSpec("ProcessedOn", "datetimeoffset", IsNullable: true),
        new ColumnSpec("AttemptCount", "int", IsNullable: false),
        new ColumnSpec("DueOn", "datetimeoffset", IsNullable: true),
        new ColumnSpec("CorrelationId", "nvarchar", IsNullable: true),
        new ColumnSpec("ProcessedBy", "nvarchar", IsNullable: true),
    ];

    private readonly ILogger<DatabaseSchemaVerificationService> logger;
    private readonly IOptionsMonitor<SqlOutboxOptions> outboxOptions;
    private readonly IOptionsMonitor<SqlInboxOptions> inboxOptions;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly PlatformConfiguration? platformConfiguration;
    private readonly IPlatformDatabaseDiscovery? databaseDiscovery;
    private readonly IStartupLatch? startupLatch;

    public DatabaseSchemaVerificationService(
        ILogger<DatabaseSchemaVerificationService> logger,
        IOptionsMonitor<SqlOutboxOptions> outboxOptions,
        IOptionsMonitor<SqlInboxOptions> inboxOptions,
        IDatabaseSchemaCompletion? schemaCompletion = null,
        PlatformConfiguration? platformConfiguration = null,
        IPlatformDatabaseDiscovery? databaseDiscovery = null,
        IStartupLatch? startupLatch = null)
    {
        this.logger = logger;
        this.outboxOptions = outboxOptions;
        this.inboxOptions = inboxOptions;
        this.schemaCompletion = schemaCompletion;
        this.platformConfiguration = platformConfiguration;
        this.databaseDiscovery = databaseDiscovery;
        this.startupLatch = startupLatch;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var step = startupLatch?.Register("platform-schema-verification");

        if (schemaCompletion is not null)
        {
            await schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
        }

        if (IsMultiDatabase())
        {
            await VerifyMultiDatabaseSchemasAsync(stoppingToken).ConfigureAwait(false);
        }
        else
        {
            await VerifySingleDatabaseSchemasAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private bool IsMultiDatabase()
    {
        return platformConfiguration is not null &&
               (platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseNoControl ||
                platformConfiguration.EnvironmentStyle == PlatformEnvironmentStyle.MultiDatabaseWithControl);
    }

    private async Task VerifyMultiDatabaseSchemasAsync(CancellationToken stoppingToken)
    {
        if (databaseDiscovery == null)
        {
            logger.LogWarning("Schema verification requested, but no database discovery service is available");
            return;
        }

        var databases = await databaseDiscovery.DiscoverDatabasesAsync(stoppingToken).ConfigureAwait(false);
        if (databases.Count == 0)
        {
            logger.LogWarning("Schema verification skipped because no databases were discovered");
            return;
        }

        foreach (var database in databases)
        {
            if (ConnectionStringComparer.IsControlPlaneDatabase(database, platformConfiguration))
            {
                continue;
            }

            await VerifySchemaAsync(database.ConnectionString, database.SchemaName, "Outbox", "Inbox", stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task VerifySingleDatabaseSchemasAsync(CancellationToken stoppingToken)
    {
        var outbox = outboxOptions.CurrentValue;
        var inbox = inboxOptions.CurrentValue;

        if (string.IsNullOrWhiteSpace(outbox.ConnectionString) && string.IsNullOrWhiteSpace(inbox.ConnectionString))
        {
            logger.LogWarning("Schema verification skipped because no connection strings were configured");
            return;
        }

        var connectionString = !string.IsNullOrWhiteSpace(outbox.ConnectionString)
            ? outbox.ConnectionString
            : inbox.ConnectionString;

        await VerifySchemaAsync(
            connectionString!,
            outbox.SchemaName,
            outbox.TableName,
            inbox.TableName,
            stoppingToken).ConfigureAwait(false);
    }

    private async Task VerifySchemaAsync(
        string connectionString,
        string schemaName,
        string outboxTableName,
        string inboxTableName,
        CancellationToken stoppingToken)
    {
        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(stoppingToken).ConfigureAwait(false);

            await VerifyTableColumnsAsync(connection, schemaName, outboxTableName, OutboxColumns, stoppingToken)
                .ConfigureAwait(false);
            await VerifyTableColumnsAsync(connection, schemaName, inboxTableName, InboxColumns, stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task VerifyTableColumnsAsync(
        SqlConnection connection,
        string schemaName,
        string tableName,
        IEnumerable<ColumnSpec> expectedColumns,
        CancellationToken stoppingToken)
    {
        var columns = await GetTableColumnsAsync(connection, schemaName, tableName, stoppingToken)
            .ConfigureAwait(false);

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Expected table {schemaName}.{tableName} to exist for schema verification.");
        }

        var errors = new List<string>();
        foreach (var column in expectedColumns)
        {
            if (!columns.TryGetValue(column.Name, out var actual))
            {
                errors.Add($"Missing column {schemaName}.{tableName}.{column.Name}.");
                continue;
            }

            if (!actual.DataType.StartsWith(column.DataType, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Column {schemaName}.{tableName}.{column.Name} expected type {column.DataType} but was {actual.DataType}.");
            }

            if (actual.IsNullable != column.IsNullable)
            {
                errors.Add($"Column {schemaName}.{tableName}.{column.Name} expected nullable={column.IsNullable} but was nullable={actual.IsNullable}.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Schema verification failed for {schemaName}.{tableName}:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }
    }

    private static async Task<Dictionary<string, ColumnMetadata>> GetTableColumnsAsync(
        SqlConnection connection,
        string schemaName,
        string tableName,
        CancellationToken stoppingToken)
    {
        const string sql = """
            SELECT COLUMN_NAME AS ColumnName,
                   DATA_TYPE AS DataType,
                   IS_NULLABLE AS IsNullable
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @SchemaName AND TABLE_NAME = @TableName
            """;

        var results = await connection.QueryAsync<ColumnRecord>(
            new CommandDefinition(sql, new { SchemaName = schemaName, TableName = tableName }, cancellationToken: stoppingToken))
            .ConfigureAwait(false);

        var columns = new Dictionary<string, ColumnMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in results)
        {
            columns[column.ColumnName] = new ColumnMetadata(
                column.DataType,
                string.Equals(column.IsNullable, "YES", StringComparison.OrdinalIgnoreCase));
        }

        return columns;
    }

    private sealed record ColumnSpec(string Name, string DataType, bool IsNullable);
    private sealed record ColumnRecord(string ColumnName, string DataType, string IsNullable);
    private sealed record ColumnMetadata(string DataType, bool IsNullable);
}
