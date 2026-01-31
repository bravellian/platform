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


using System.Data;
using Cronos;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

#pragma warning disable CA2100 // SQL command text uses validated schema/table names with parameters.

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
        connectionString = this.options.ConnectionString;
        this.timeProvider = timeProvider;

        // Build SQL queries using configured schema and table names
        insertTimerSql = $"INSERT INTO [{this.options.SchemaName}].[{this.options.TimersTableName}] (Id, Topic, Payload, DueTime) VALUES (@Id, @Topic, @Payload, @DueTime);";

        cancelTimerSql = $"UPDATE [{this.options.SchemaName}].[{this.options.TimersTableName}] SET Status = 'Cancelled' WHERE Id = @TimerId AND Status = 'Pending';";

        mergeJobSql = $"""

                        MERGE [{this.options.SchemaName}].[{this.options.JobsTableName}] AS target
                        USING (SELECT @JobName AS JobName) AS source
                        ON (target.JobName = source.JobName)
                        WHEN MATCHED THEN
                            UPDATE SET Topic = @Topic, CronSchedule = @CronSchedule, Payload = @Payload, NextDueTime = @NextDueTime
                        WHEN NOT MATCHED THEN
                            INSERT (Id, JobName, Topic, CronSchedule, Payload, NextDueTime)
                            VALUES (NEWID(), @JobName, @Topic, @CronSchedule, @Payload, @NextDueTime);
            """;

        deleteJobRunsSql = $"DELETE FROM [{this.options.SchemaName}].[{this.options.JobRunsTableName}] WHERE JobId = (SELECT Id FROM [{this.options.SchemaName}].[{this.options.JobsTableName}] WHERE JobName = @JobName);";

        deleteJobSql = $"DELETE FROM [{this.options.SchemaName}].[{this.options.JobsTableName}] WHERE JobName = @JobName;";

        triggerJobSql = $"""

                        INSERT INTO [{this.options.SchemaName}].[{this.options.JobRunsTableName}] (Id, JobId, ScheduledTime)
                        SELECT NEWID(), Id, SYSDATETIMEOFFSET() FROM [{this.options.SchemaName}].[{this.options.JobsTableName}] WHERE JobName = @JobName;
            """;
    }

    public async Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime, CancellationToken cancellationToken)
    {
        var timerId = Guid.NewGuid();
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new CommandDefinition(insertTimerSql, new { Id = timerId, Topic = topic, Payload = payload, DueTime = dueTime }, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command).ConfigureAwait(false);
        }

        return timerId.ToString();
    }

    public async Task<bool> CancelTimerAsync(string timerId, CancellationToken cancellationToken)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new CommandDefinition(cancelTimerSql, new { TimerId = timerId }, cancellationToken: cancellationToken);
            var rowsAffected = await connection.ExecuteAsync(command).ConfigureAwait(false);
            return rowsAffected > 0;
        }
    }

    public async Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, CancellationToken cancellationToken)
    {
        await CreateOrUpdateJobAsync(jobName, topic, cronSchedule, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload, CancellationToken cancellationToken)
    {
        // Determine cron format based on the number of parts.
        var format = cronSchedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
            ? CronFormat.IncludeSeconds
            : CronFormat.Standard;

        var cronExpression = CronExpression.Parse(cronSchedule, format);
        var nextDueTime = cronExpression.GetNextOccurrence(timeProvider.GetUtcNow().UtcDateTime);

        // MERGE is a great way to handle "UPSERT" logic atomically in SQL Server.
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new CommandDefinition(mergeJobSql, new { JobName = jobName, Topic = topic, CronSchedule = cronSchedule, Payload = payload, NextDueTime = nextDueTime }, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command).ConfigureAwait(false);
        }
    }

    public async Task DeleteJobAsync(string jobName, CancellationToken cancellationToken)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Must delete runs before the job definition due to foreign key
                var deleteRunsCommand = new CommandDefinition(deleteJobRunsSql, new { JobName = jobName }, transaction, cancellationToken: cancellationToken);
                await connection.ExecuteAsync(deleteRunsCommand).ConfigureAwait(false);

                var deleteJobCommand = new CommandDefinition(deleteJobSql, new { JobName = jobName }, transaction, cancellationToken: cancellationToken);
                await connection.ExecuteAsync(deleteJobCommand).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task TriggerJobAsync(string jobName, CancellationToken cancellationToken)
    {
        // Creates a new run that is due immediately.
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = new CommandDefinition(triggerJobSql, new { JobName = jobName }, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Guid>> ClaimTimersAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var result = new List<Guid>(batchSize);

        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand($"[{options.SchemaName}].[Timers_Claim]", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("@OwnerToken", ownerToken.Value);
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
    }

    public async Task<IReadOnlyList<Guid>> ClaimJobRunsAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var result = new List<Guid>(batchSize);

        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand($"[{options.SchemaName}].[JobRuns_Claim]", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("@OwnerToken", ownerToken.Value);
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
    }

    public async Task AckTimersAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        await ExecuteWithIdsAsync($"[{options.SchemaName}].[Timers_Ack]", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task AckJobRunsAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        await ExecuteWithIdsAsync($"[{options.SchemaName}].[JobRuns_Ack]", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbandonTimersAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        await ExecuteWithIdsAsync($"[{options.SchemaName}].[Timers_Abandon]", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task AbandonJobRunsAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        await ExecuteWithIdsAsync($"[{options.SchemaName}].[JobRuns_Abandon]", ownerToken, ids, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReapExpiredTimersAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand($"[{options.SchemaName}].[Timers_ReapExpired]", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ReapExpiredJobRunsAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand($"[{options.SchemaName}].[JobRuns_ReapExpired]", connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteWithIdsAsync(
        string procedure,
        Bravellian.Platform.OwnerToken ownerToken,
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

        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(procedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("@OwnerToken", ownerToken.Value);
                var parameter = command.Parameters.AddWithValue("@Ids", tvp);
                parameter.SqlDbType = SqlDbType.Structured;
                parameter.TypeName = $"[{options.SchemaName}].[GuidIdList]";

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
