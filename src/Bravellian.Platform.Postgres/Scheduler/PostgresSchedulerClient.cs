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

using System.Linq;
using Cronos;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

internal sealed class PostgresSchedulerClient : ISchedulerClient
{
    private readonly string connectionString;
    private readonly PostgresSchedulerOptions options;
    private readonly TimeProvider timeProvider;

    private readonly string jobsTable;
    private readonly string jobRunsTable;
    private readonly string timersTable;

    private readonly string insertTimerSql;
    private readonly string cancelTimerSql;
    private readonly string upsertJobSql;
    private readonly string deleteJobRunsSql;
    private readonly string deleteJobSql;
    private readonly string triggerJobSql;

    public PostgresSchedulerClient(IOptions<PostgresSchedulerOptions> options, TimeProvider timeProvider)
    {
        this.options = options.Value;
        connectionString = this.options.ConnectionString;
        this.timeProvider = timeProvider;

        jobsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.JobsTableName);
        jobRunsTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.JobRunsTableName);
        timersTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.TimersTableName);

        insertTimerSql = $"""
            INSERT INTO {timersTable} ("Id", "Topic", "Payload", "DueTime")
            VALUES (@Id, @Topic, @Payload, @DueTime);
            """;

        cancelTimerSql = $"""
            UPDATE {timersTable}
            SET "Status" = 'Cancelled'
            WHERE "Id" = @TimerId AND "Status" = 'Pending';
            """;

        upsertJobSql = $"""
            INSERT INTO {jobsTable} ("Id", "JobName", "Topic", "CronSchedule", "Payload", "NextDueTime")
            VALUES (@Id, @JobName, @Topic, @CronSchedule, @Payload, @NextDueTime)
            ON CONFLICT ("JobName") DO UPDATE
            SET "Topic" = EXCLUDED."Topic",
                "CronSchedule" = EXCLUDED."CronSchedule",
                "Payload" = EXCLUDED."Payload",
                "NextDueTime" = EXCLUDED."NextDueTime";
            """;

        deleteJobRunsSql = $"""
            DELETE FROM {jobRunsTable}
            WHERE "JobId" = (SELECT "Id" FROM {jobsTable} WHERE "JobName" = @JobName);
            """;

        deleteJobSql = $"""
            DELETE FROM {jobsTable}
            WHERE "JobName" = @JobName;
            """;

        triggerJobSql = $"""
            INSERT INTO {jobRunsTable} ("Id", "JobId", "ScheduledTime")
            SELECT @RunId, "Id", CURRENT_TIMESTAMP
            FROM {jobsTable}
            WHERE "JobName" = @JobName;
            """;
    }

    public async Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime, CancellationToken cancellationToken)
    {
        var timerId = Guid.NewGuid();
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = new CommandDefinition(
            insertTimerSql,
            new { Id = timerId, Topic = topic, Payload = payload, DueTime = dueTime.UtcDateTime },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command).ConfigureAwait(false);
        return timerId.ToString();
    }

    public async Task<bool> CancelTimerAsync(string timerId, CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = new CommandDefinition(
            cancelTimerSql,
            new { TimerId = Guid.Parse(timerId) },
            cancellationToken: cancellationToken);

        var rowsAffected = await connection.ExecuteAsync(command).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, CancellationToken cancellationToken)
    {
        return CreateOrUpdateJobAsync(jobName, topic, cronSchedule, null, cancellationToken);
    }

    public async Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload, CancellationToken cancellationToken)
    {
        var format = cronSchedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;

        var cronExpression = CronExpression.Parse(cronSchedule, format);
        var nextDueTime = cronExpression.GetNextOccurrence(timeProvider.GetUtcNow().UtcDateTime);

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = new CommandDefinition(
            upsertJobSql,
            new
            {
                Id = Guid.NewGuid(),
                JobName = jobName,
                Topic = topic,
                CronSchedule = cronSchedule,
                Payload = payload,
                NextDueTime = nextDueTime,
            },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    public async Task DeleteJobAsync(string jobName, CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var deleteRunsCommand = new CommandDefinition(
                deleteJobRunsSql,
                new { JobName = jobName },
                transaction: transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(deleteRunsCommand).ConfigureAwait(false);

            var deleteJobCommand = new CommandDefinition(
                deleteJobSql,
                new { JobName = jobName },
                transaction: transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(deleteJobCommand).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task TriggerJobAsync(string jobName, CancellationToken cancellationToken)
    {
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = new CommandDefinition(
            triggerJobSql,
            new { JobName = jobName, RunId = Guid.NewGuid() },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> ClaimTimersAsync(
        OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            WITH cte AS (
                SELECT "Id"
                FROM {timersTable}
                WHERE "StatusCode" = 0
                    AND "DueTime" <= CURRENT_TIMESTAMP
                    AND ("LockedUntil" IS NULL OR "LockedUntil" <= CURRENT_TIMESTAMP)
                ORDER BY "DueTime", "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize
            )
            UPDATE {timersTable} AS t
            SET "StatusCode" = 1,
                "OwnerToken" = @OwnerToken,
                "LockedUntil" = CURRENT_TIMESTAMP + (@LeaseSeconds || ' seconds')::interval
            FROM cte
            WHERE t."Id" = cte."Id"
            RETURNING t."Id";
            """;

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var result = await connection.QueryAsync<Guid>(sql, new
        {
            OwnerToken = ownerToken.Value,
            LeaseSeconds = leaseSeconds,
            BatchSize = batchSize,
        }).ConfigureAwait(false);

        return result.ToList();
    }

    public async Task<IReadOnlyList<Guid>> ClaimJobRunsAsync(
        OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            WITH cte AS (
                SELECT "Id"
                FROM {jobRunsTable}
                WHERE "StatusCode" = 0
                    AND "ScheduledTime" <= CURRENT_TIMESTAMP
                    AND ("LockedUntil" IS NULL OR "LockedUntil" <= CURRENT_TIMESTAMP)
                ORDER BY "ScheduledTime", "Id"
                FOR UPDATE SKIP LOCKED
                LIMIT @BatchSize
            )
            UPDATE {jobRunsTable} AS jr
            SET "StatusCode" = 1,
                "OwnerToken" = @OwnerToken,
                "LockedUntil" = CURRENT_TIMESTAMP + (@LeaseSeconds || ' seconds')::interval,
                "Status" = 'Running',
                "ClaimedAt" = CURRENT_TIMESTAMP,
                "ClaimedBy" = @ClaimedBy
            FROM cte
            WHERE jr."Id" = cte."Id"
            RETURNING jr."Id";
            """;

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var result = await connection.QueryAsync<Guid>(sql, new
        {
            OwnerToken = ownerToken.Value,
            LeaseSeconds = leaseSeconds,
            BatchSize = batchSize,
            ClaimedBy = ownerToken.Value.ToString(),
        }).ConfigureAwait(false);

        return result.ToList();
    }

    public Task AckTimersAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        return ExecuteWithIdsAsync(ownerToken, ids, cancellationToken, (table, idCondition) => $"""
            UPDATE {table}
            SET "StatusCode" = 2,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "ProcessedAt" = CURRENT_TIMESTAMP,
                "Status" = 'Processed'
            WHERE "OwnerToken" = @OwnerToken
                AND "StatusCode" = 1
                AND {idCondition};
            """, timersTable);
    }

    public Task AckJobRunsAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        return ExecuteWithIdsAsync(ownerToken, ids, cancellationToken, (table, idCondition) => $"""
            UPDATE {table}
            SET "StatusCode" = 2,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "Status" = 'Succeeded',
                "EndTime" = COALESCE("EndTime", CURRENT_TIMESTAMP)
            WHERE "OwnerToken" = @OwnerToken
                AND "StatusCode" = 1
                AND {idCondition};
            """, jobRunsTable);
    }

    public Task AbandonTimersAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        return ExecuteWithIdsAsync(ownerToken, ids, cancellationToken, (table, idCondition) => $"""
            UPDATE {table}
            SET "StatusCode" = 0,
                "OwnerToken" = NULL,
                "LockedUntil" = CASE
                    WHEN @RetryDelaySeconds IS NULL THEN NULL
                    ELSE CURRENT_TIMESTAMP + (@RetryDelaySeconds || ' seconds')::interval
                END,
                "RetryCount" = "RetryCount" + 1,
                "LastError" = COALESCE(@LastError, "LastError"),
                "Status" = 'Pending'
            WHERE "OwnerToken" = @OwnerToken
                AND "StatusCode" = 1
                AND {idCondition};
            """, timersTable);
    }

    public Task AbandonJobRunsAsync(OwnerToken ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        return ExecuteWithIdsAsync(ownerToken, ids, cancellationToken, (table, idCondition) => $"""
            UPDATE {table}
            SET "StatusCode" = 0,
                "OwnerToken" = NULL,
                "LockedUntil" = CASE
                    WHEN @RetryDelaySeconds IS NULL THEN NULL
                    ELSE CURRENT_TIMESTAMP + (@RetryDelaySeconds || ' seconds')::interval
                END,
                "RetryCount" = "RetryCount" + 1,
                "LastError" = COALESCE(@LastError, "LastError"),
                "Status" = 'Pending'
            WHERE "OwnerToken" = @OwnerToken
                AND "StatusCode" = 1
                AND {idCondition};
            """, jobRunsTable);
    }

    public async Task ReapExpiredTimersAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE {timersTable}
            SET "StatusCode" = 0,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "Status" = 'Pending'
            WHERE "StatusCode" = 1
                AND "LockedUntil" IS NOT NULL
                AND "LockedUntil" <= CURRENT_TIMESTAMP;
            """;

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    public async Task ReapExpiredJobRunsAsync(CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE {jobRunsTable}
            SET "StatusCode" = 0,
                "OwnerToken" = NULL,
                "LockedUntil" = NULL,
                "Status" = 'Pending'
            WHERE "StatusCode" = 1
                AND "LockedUntil" IS NOT NULL
                AND "LockedUntil" <= CURRENT_TIMESTAMP;
            """;

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private async Task ExecuteWithIdsAsync(
        OwnerToken ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken,
        Func<string, string, string> sqlBuilder,
        string table)
    {
        var idList = ids.ToArray();
        if (idList.Length == 0)
        {
            return;
        }

        var sql = sqlBuilder(table, "\"Id\" = ANY(@Ids)");

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync(sql, new
        {
            OwnerToken = ownerToken.Value,
            Ids = idList,
            LastError = (string?)null,
            RetryDelaySeconds = (int?)null,
        }).ConfigureAwait(false);
    }
}
