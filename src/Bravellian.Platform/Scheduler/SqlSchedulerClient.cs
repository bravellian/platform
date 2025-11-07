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

using Cronos;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System;
using System.Data;
using System.Threading.Tasks;

internal class SqlSchedulerClient : ISchedulerClient
{
    private readonly string connectionString;
    private readonly SqlSchedulerOptions options;
    private readonly TimeProvider timeProvider;

    // Pre-built SQL queries using configured table names
    private readonly string insertTimerSql;
    private readonly string cancelTimerSql;
    private readonly string mergeJobSql;
    private readonly string deleteJobRunsSql;
    private readonly string deleteJobSql;
    private readonly string triggerJobSql;

    public SqlSchedulerClient(IOptions<SqlSchedulerOptions> options, TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;
        this.timeProvider = timeProvider;

        // Build SQL queries using configured schema and table names
        this.insertTimerSql = $"INSERT INTO [{this.options.SchemaName}].[{this.options.TimersTableName}] (Id, Topic, Payload, DueTime) VALUES (@Id, @Topic, @Payload, @DueTime);";

        this.cancelTimerSql = $"UPDATE [{this.options.SchemaName}].[{this.options.TimersTableName}] SET Status = 'Cancelled' WHERE Id = @TimerId AND Status = 'Pending';";

        this.mergeJobSql = $"""

                        MERGE [{this.options.SchemaName}].[{this.options.JobsTableName}] AS target
                        USING (SELECT @JobName AS JobName) AS source
                        ON (target.JobName = source.JobName)
                        WHEN MATCHED THEN
                            UPDATE SET Topic = @Topic, CronSchedule = @CronSchedule, Payload = @Payload, NextDueTime = @NextDueTime
                        WHEN NOT MATCHED THEN
                            INSERT (Id, JobName, Topic, CronSchedule, Payload, NextDueTime)
                            VALUES (NEWID(), @JobName, @Topic, @CronSchedule, @Payload, @NextDueTime);
            """;

        this.deleteJobRunsSql = $"DELETE FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}] WHERE JobId = (SELECT Id FROM [{this.options.SchemaName}].[{this.options.JobsTableName}] WHERE JobName = @JobName);";

        this.deleteJobSql = $"DELETE FROM [{this.options.SchemaName}].[{this.options.JobsTableName}] WHERE JobName = @JobName;";

        this.triggerJobSql = $"""

                        INSERT INTO [{this.options.SchemaName}].[{this.options.JobRunsTableName}] (Id, JobId, ScheduledTime)
                        SELECT NEWID(), Id, SYSDATETIMEOFFSET() FROM [{this.options.SchemaName}].[{this.options.JobsTableName}] WHERE JobName = @JobName;
            """;
    }

    public async Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime)
    {
        var timerId = Guid.NewGuid();
        using (var connection = new SqlConnection(this.connectionString))
        {
            await connection.ExecuteAsync(this.insertTimerSql, new { Id = timerId, Topic = topic, Payload = payload, DueTime = dueTime }).ConfigureAwait(false);
        }

        return timerId.ToString();
    }

    public async Task<bool> CancelTimerAsync(string timerId)
    {
        using (var connection = new SqlConnection(this.connectionString))
        {
            var rowsAffected = await connection.ExecuteAsync(this.cancelTimerSql, new { TimerId = timerId }).ConfigureAwait(false);
            return rowsAffected > 0;
        }
    }

    public async Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload = null)
    {
        // Determine cron format based on the number of parts.
        var format = cronSchedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;

        var cronExpression = CronExpression.Parse(cronSchedule, format);
        var nextDueTime = cronExpression.GetNextOccurrence(this.timeProvider.GetUtcNow().UtcDateTime);

        // MERGE is a great way to handle "UPSERT" logic atomically in SQL Server.
        using (var connection = new SqlConnection(this.connectionString))
        {
            await connection.ExecuteAsync(this.mergeJobSql, new { JobName = jobName, Topic = topic, CronSchedule = cronSchedule, Payload = payload, NextDueTime = nextDueTime }).ConfigureAwait(false);
        }
    }

    public async Task DeleteJobAsync(string jobName)
    {
        using (var connection = new SqlConnection(this.connectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            using (var transaction = connection.BeginTransaction())
            {
                // Must delete runs before the job definition due to foreign key
                await connection.ExecuteAsync(this.deleteJobRunsSql, new { JobName = jobName }, transaction).ConfigureAwait(false);

                await connection.ExecuteAsync(this.deleteJobSql, new { JobName = jobName }, transaction).ConfigureAwait(false);

                transaction.Commit();
            }
        }
    }

    public async Task TriggerJobAsync(string jobName)
    {
        // Creates a new run that is due immediately.
        using (var connection = new SqlConnection(this.connectionString))
        {
            await connection.ExecuteAsync(this.triggerJobSql, new { JobName = jobName }).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Guid>> ClaimTimersAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var result = new List<Guid>(batchSize);

        var connection = new SqlConnection(this.connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand("dbo.Timers_Claim", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            command.Parameters.AddWithValue("@OwnerToken", ownerToken);
            command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
            command.Parameters.AddWithValue("@BatchSize", batchSize);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add((Guid)reader.GetValue(0));
            }

            return result;
        }
    }

    public async Task<IReadOnlyList<Guid>> ClaimJobRunsAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var result = new List<Guid>(batchSize);

        var connection = new SqlConnection(this.connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand("dbo.JobRuns_Claim", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            command.Parameters.AddWithValue("@OwnerToken", ownerToken);
            command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
            command.Parameters.AddWithValue("@BatchSize", batchSize);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add((Guid)reader.GetValue(0));
            }

            return result;
        }
    }

    public async Task AckTimersAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync("dbo.Timers_Ack", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task AckJobRunsAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync("dbo.JobRuns_Ack", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbandonTimersAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync("dbo.Timers_Abandon", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbandonJobRunsAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        await this.ExecuteWithIdsAsync("dbo.JobRuns_Abandon", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReapExpiredTimersAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(this.connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand("dbo.Timers_ReapExpired", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ReapExpiredJobRunsAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(this.connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand("dbo.JobRuns_ReapExpired", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteWithIdsAsync(
        string procedure,
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return; // Nothing to do
        }

        var tvp = new DataTable();
        tvp.Columns.Add("Id", typeof(Guid));
        foreach (var id in idList)
        {
            tvp.Rows.Add(id);
        }

        var connection = new SqlConnection(this.connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new SqlCommand(procedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            command.Parameters.AddWithValue("@OwnerToken", ownerToken);
            var parameter = command.Parameters.AddWithValue("@Ids", tvp);
            parameter.SqlDbType = SqlDbType.Structured;
            parameter.TypeName = $"[{this.options.SchemaName}].[GuidIdList]";

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
