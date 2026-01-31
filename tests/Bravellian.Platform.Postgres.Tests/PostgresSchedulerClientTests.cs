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
using System.Globalization;
using Microsoft.Extensions.Options;
using Npgsql;

#pragma warning disable CA2100
namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class PostgresSchedulerClientTests : PostgresTestBase
{
    private PostgresSchedulerClient? schedulerClient;
    private readonly PostgresSchedulerOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "infra", JobsTableName = "Jobs", JobRunsTableName = "JobRuns", TimersTableName = "Timers" };
    private string qualifiedJobsTable = string.Empty;
    private string qualifiedJobRunsTable = string.Empty;
    private string qualifiedTimersTable = string.Empty;

    public PostgresSchedulerClientTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;
        schedulerClient = new PostgresSchedulerClient(Options.Create(defaultOptions), TimeProvider.System);
        qualifiedJobsTable = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.JobsTableName);
        qualifiedJobRunsTable = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.JobRunsTableName);
        qualifiedTimersTable = PostgresSqlHelper.Qualify(defaultOptions.SchemaName, defaultOptions.TimersTableName);
    }

    /// <summary>When constructed with valid options, then the client is created and implements ISchedulerClient.</summary>
    /// <intent>Verify the scheduler client can be instantiated with valid options.</intent>
    /// <scenario>Given PostgresSchedulerOptions with a valid connection string.</scenario>
    /// <behavior>The instance is non-null and assignable to ISchedulerClient.</behavior>
    [Fact]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        var client = new PostgresSchedulerClient(Options.Create(defaultOptions), TimeProvider.System);

        client.ShouldNotBeNull();
        client.ShouldBeAssignableTo<ISchedulerClient>();
    }

    /// <summary>When scheduling a timer, then a timer row is inserted into the database.</summary>
    /// <intent>Verify ScheduleTimerAsync persists timers to the Timers table.</intent>
    /// <scenario>Given a topic, payload, and due time in the near future.</scenario>
    /// <behavior>The Timers table contains one row for the returned timer id and topic.</behavior>
    [Fact]
    public async Task ScheduleTimerAsync_WithValidParameters_InsertsTimerToDatabase()
    {
        string topic = "test-timer-topic";
        string payload = "test timer payload";
        DateTimeOffset dueTime = DateTimeOffset.UtcNow.AddMinutes(5);

        var timerId = await schedulerClient!.ScheduleTimerAsync(topic, payload, dueTime, CancellationToken.None);

        timerId.ShouldNotBeNull();
        Guid.TryParse(timerId, out var timerGuid).ShouldBeTrue();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT COUNT(*) FROM {qualifiedTimersTable} WHERE \"Id\" = @Id AND \"Topic\" = @Topic";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", timerGuid);
        command.Parameters.AddWithValue("@Topic", topic);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
        count.ShouldBe(1);
    }

    /// <summary>When scheduling with custom table names, then the timer is inserted into the custom table.</summary>
    /// <intent>Verify custom schema/table names are honored for timers.</intent>
    /// <scenario>Given a scheduler client configured with custom schema and table names.</scenario>
    /// <behavior>The custom timers table contains one row for the scheduled timer.</behavior>
    [Fact]
    public async Task ScheduleTimerAsync_WithCustomTableNames_InsertsToCorrectTable()
    {
        var customOptions = new PostgresSchedulerOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "custom",
            TimersTableName = "CustomTimers",
            JobsTableName = "CustomJobs",
            JobRunsTableName = "CustomJobRuns",
        };

        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
            ConnectionString,
            customOptions.SchemaName,
            customOptions.JobsTableName,
            customOptions.JobRunsTableName,
            customOptions.TimersTableName);

        var customClient = new PostgresSchedulerClient(Options.Create(customOptions), TimeProvider.System);
        var customTimersTable = PostgresSqlHelper.Qualify(customOptions.SchemaName, customOptions.TimersTableName);

        string topic = "test-custom-timer";
        string payload = "custom payload";
        DateTimeOffset dueTime = DateTimeOffset.UtcNow.AddMinutes(10);

        var timerId = await customClient.ScheduleTimerAsync(topic, payload, dueTime, CancellationToken.None);

        Guid.TryParse(timerId, out var timerGuid).ShouldBeTrue();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT COUNT(*) FROM {customTimersTable} WHERE \"Id\" = @Id AND \"Topic\" = @Topic";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", timerGuid);
        command.Parameters.AddWithValue("@Topic", topic);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
        count.ShouldBe(1);
    }

    /// <summary>When creating a new job, then a job row is inserted.</summary>
    /// <intent>Verify CreateOrUpdateJobAsync inserts new jobs.</intent>
    /// <scenario>Given a job name, topic, and cron schedule that do not yet exist.</scenario>
    /// <behavior>The Jobs table contains one row for the job name.</behavior>
    [Fact]
    public async Task CreateOrUpdateJobAsync_NewJob_InsertsJob()
    {
        string jobName = "test-job";
        string topic = "test-job-topic";
        string cronSchedule = "0 0 * * * *";

        await schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT COUNT(*) FROM {qualifiedJobsTable} WHERE \"JobName\" = @JobName";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
        count.ShouldBe(1);
    }

    /// <summary>When a job is created without a payload, then the payload column is null.</summary>
    /// <intent>Verify null payloads are persisted as NULL.</intent>
    /// <scenario>Given CreateOrUpdateJobAsync called without a payload for a new job.</scenario>
    /// <behavior>The Jobs row payload column is NULL.</behavior>
    [Fact]
    public async Task CreateOrUpdateJobAsync_WithNullPayload_SetsPayloadToNull()
    {
        string jobName = "test-job-null-payload";
        string topic = "test-job-topic";
        string cronSchedule = "0 0 * * * *";

        await schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT \"Payload\" FROM {qualifiedJobsTable} WHERE \"JobName\" = @JobName";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        result.ShouldBe(DBNull.Value);
    }

    /// <summary>When updating an existing job, then the job is updated without duplication.</summary>
    /// <intent>Verify CreateOrUpdateJobAsync updates existing jobs.</intent>
    /// <scenario>Given a job created once and then updated with a new topic.</scenario>
    /// <behavior>The Jobs table has one row and the topic matches the update.</behavior>
    [Fact]
    public async Task CreateOrUpdateJobAsync_ExistingJob_UpdatesJob()
    {
        string jobName = "test-job-update";
        string originalTopic = "original-topic";
        string updatedTopic = "updated-topic";
        string cronSchedule = "0 0 * * * *";

        await schedulerClient!.CreateOrUpdateJobAsync(jobName, originalTopic, cronSchedule, CancellationToken.None);

        await schedulerClient!.CreateOrUpdateJobAsync(jobName, updatedTopic, cronSchedule, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var countSql = $"SELECT COUNT(*) FROM {qualifiedJobsTable} WHERE \"JobName\" = @JobName";
        await using var countCommand = new NpgsqlCommand(countSql, connection);
        countCommand.Parameters.AddWithValue("@JobName", jobName);

        var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
        count.ShouldBe(1);

        var topicSql = $"SELECT \"Topic\" FROM {qualifiedJobsTable} WHERE \"JobName\" = @JobName";
        await using var topicCommand = new NpgsqlCommand(topicSql, connection);
        topicCommand.Parameters.AddWithValue("@JobName", jobName);

        var topic = await topicCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string;
        Assert.NotNull(topic);
        topic.ShouldBe(updatedTopic);
    }

    /// <summary>When deleting a job by name, then the job row is removed.</summary>
    /// <intent>Verify DeleteJobAsync removes jobs.</intent>
    /// <scenario>Given a job created and then deleted by name.</scenario>
    /// <behavior>The Jobs table contains zero rows for the job name.</behavior>
    [Fact]
    public async Task DeleteJobAsync_WithValidJobName_RemovesJob()
    {
        string jobName = "test-job-delete";
        string topic = "test-job-delete-topic";
        string cronSchedule = "0 0 * * * *";

        await schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, CancellationToken.None);

        await schedulerClient!.DeleteJobAsync(jobName, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT COUNT(*) FROM {qualifiedJobsTable} WHERE \"JobName\" = @JobName";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
        count.ShouldBe(0);
    }

    /// <summary>When triggering a job by name, then a job run row is created.</summary>
    /// <intent>Verify TriggerJobAsync creates job runs.</intent>
    /// <scenario>Given an existing job and a trigger call.</scenario>
    /// <behavior>The JobRuns table contains at least one row for the job.</behavior>
    [Fact]
    public async Task TriggerJobAsync_WithValidJobName_CreatesJobRun()
    {
        string jobName = "test-job-trigger";
        string topic = "test-job-trigger-topic";
        string cronSchedule = "0 0 * * * *";

        await schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, CancellationToken.None);

        await schedulerClient!.TriggerJobAsync(jobName, CancellationToken.None);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = $"SELECT COUNT(*) FROM {qualifiedJobRunsTable} jr INNER JOIN {qualifiedJobsTable} j ON jr.\"JobId\" = j.\"Id\" WHERE j.\"JobName\" = @JobName";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), CultureInfo.InvariantCulture);
        count.ShouldBeGreaterThan(0);
    }
}
#pragma warning restore CA2100

