namespace Bravellian.Platform;

using Cronos;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;

internal class SqlSchedulerClient : ISchedulerClient
{
    private readonly string connectionString;

    public SqlSchedulerClient(string connectionString)
    {
        this.connectionString = connectionString;
    }

    public async Task<string> ScheduleTimerAsync(string topic, string payload, DateTimeOffset dueTime)
    {
        var timerId = Guid.NewGuid();
        var sql = "INSERT INTO dbo.Timers (Id, Topic, Payload, DueTime) VALUES (@Id, @Topic, @Payload, @DueTime);";
        using (var connection = new SqlConnection(this.connectionString))
        {
            await connection.ExecuteAsync(sql, new { Id = timerId, Topic = topic, Payload = payload, DueTime = dueTime }).ConfigureAwait(false);
        }
        return timerId.ToString();
    }

    public async Task<bool> CancelTimerAsync(string timerId)
    {
        var sql = "UPDATE dbo.Timers SET Status = 'Cancelled' WHERE Id = @TimerId AND Status = 'Pending';";
        using (var connection = new SqlConnection(this.connectionString))
        {
            var rowsAffected = await connection.ExecuteAsync(sql, new { TimerId = timerId }).ConfigureAwait(false);
            return rowsAffected > 0;
        }
    }

    public async Task CreateOrUpdateJobAsync(string jobName, string topic, string cronSchedule, string? payload = null)
    {
        var cronExpression = CronExpression.Parse(cronSchedule, CronFormat.IncludeSeconds);
        var nextDueTime = cronExpression.GetNextOccurrence(DateTime.UtcNow);

        // MERGE is a great way to handle "UPSERT" logic atomically in SQL Server.
        var sql = @"
            MERGE dbo.Jobs AS target
            USING (SELECT @JobName AS JobName) AS source
            ON (target.JobName = source.JobName)
            WHEN MATCHED THEN
                UPDATE SET Topic = @Topic, CronSchedule = @CronSchedule, Payload = @Payload, NextDueTime = @NextDueTime
            WHEN NOT MATCHED THEN
                INSERT (Id, JobName, Topic, CronSchedule, Payload, NextDueTime)
                VALUES (NEWID(), @JobName, @Topic, @CronSchedule, @Payload, @NextDueTime);";

        using (var connection = new SqlConnection(this.connectionString))
        {
            await connection.ExecuteAsync(sql, new { JobName = jobName, Topic = topic, CronSchedule = cronSchedule, Payload = payload, NextDueTime = nextDueTime }).ConfigureAwait(false);
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
                var deleteRunsSql = "DELETE FROM dbo.JobRuns WHERE JobId = (SELECT Id FROM dbo.Jobs WHERE JobName = @JobName);";
                await connection.ExecuteAsync(deleteRunsSql, new { JobName = jobName }, transaction).ConfigureAwait(false);

                var deleteJobSql = "DELETE FROM dbo.Jobs WHERE JobName = @JobName;";
                await connection.ExecuteAsync(deleteJobSql, new { JobName = jobName }, transaction).ConfigureAwait(false);

                transaction.Commit();
            }
        }
    }

    public async Task TriggerJobAsync(string jobName)
    {
        // Creates a new run that is due immediately.
        var sql = @"
            INSERT INTO dbo.JobRuns (Id, JobId, ScheduledTime)
            SELECT NEWID(), Id, SYSDATETIMEOFFSET() FROM dbo.Jobs WHERE JobName = @JobName;";

        using (var connection = new SqlConnection(this.connectionString))
        {
            await connection.ExecuteAsync(sql, new { JobName = jobName }).ConfigureAwait(false);
        }
    }
}