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
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SqlSchedulerClientTests : SqlServerTestBase
{
    private SqlSchedulerClient? schedulerClient;
    private readonly SqlSchedulerOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "infra", JobsTableName = "Jobs", JobRunsTableName = "JobRuns", TimersTableName = "Timers" };

    public SqlSchedulerClientTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        defaultOptions.ConnectionString = ConnectionString;
        schedulerClient = new SqlSchedulerClient(Options.Create(defaultOptions), TimeProvider.System);
    }

    /// <summary>When constructing the scheduler client, then it provides an ISchedulerClient implementation.</summary>
    /// <intent>Verify SqlSchedulerClient instantiation succeeds.</intent>
    /// <scenario>Create a SqlSchedulerClient with default options and TimeProvider.System.</scenario>
    /// <behavior>The client is not null and is assignable to ISchedulerClient.</behavior>
    [Fact]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        // Arrange & Act
        var client = new SqlSchedulerClient(Options.Create(defaultOptions), TimeProvider.System);

        // Assert
        client.ShouldNotBeNull();
        client.ShouldBeAssignableTo<ISchedulerClient>();
    }

    // Timer Tests
    /// <summary>When scheduling a timer, then a timer row with the topic is inserted.</summary>
    /// <intent>Validate ScheduleTimerAsync persists timers.</intent>
    /// <scenario>Schedule a timer with topic, payload, and due time.</scenario>
    /// <behavior>The returned id is a Guid and one matching timer row exists.</behavior>
    [Fact]
    public async Task ScheduleTimerAsync_WithValidParameters_InsertsTimerToDatabase()
    {
        // Arrange
        string topic = "test-timer-topic";
        string payload = "test timer payload";
        DateTimeOffset dueTime = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act
        var timerId = await schedulerClient!.ScheduleTimerAsync(topic, payload, dueTime, CancellationToken.None);

        // Assert
        timerId.ShouldNotBeNull();
        Guid.TryParse(timerId, out var timerGuid).ShouldBeTrue();

        // Verify the timer was inserted
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = "SELECT COUNT(*) FROM infra.Timers WHERE Id = @Id AND Topic = @Topic";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", timerGuid);
        command.Parameters.AddWithValue("@Topic", topic);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);
    }

    /// <summary>When custom scheduler table names are used, then timers are stored in the custom table.</summary>
    /// <intent>Ensure ScheduleTimerAsync honors custom table options.</intent>
    /// <scenario>Create custom schema/tables and schedule a timer with the custom client.</scenario>
    /// <behavior>The custom timers table contains one matching row.</behavior>
    [Fact]
    public async Task ScheduleTimerAsync_WithCustomTableNames_InsertsToCorrectTable()
    {
        // Arrange - Use custom table names
        var customOptions = new SqlSchedulerOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "custom",
            TimersTableName = "CustomTimers",
            JobsTableName = "CustomJobs",
            JobRunsTableName = "CustomJobRuns",
        };

        // Create the custom schema and tables for this test
        await using var setupConnection = new SqlConnection(ConnectionString);
        await setupConnection.OpenAsync(TestContext.Current.CancellationToken);

        // Create custom schema if it doesn't exist
        await setupConnection.ExecuteAsync("IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'custom') EXEC('CREATE SCHEMA custom')");

        // Create custom tables using DatabaseSchemaManager
        await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(ConnectionString, "custom", "CustomJobs", "CustomJobRuns", "CustomTimers");

        var customSchedulerClient = new SqlSchedulerClient(Options.Create(customOptions), TimeProvider.System);

        string topic = "test-timer-custom";
        string payload = "test timer custom payload";
        DateTimeOffset dueTime = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act
        var timerId = await customSchedulerClient.ScheduleTimerAsync(topic, payload, dueTime, CancellationToken.None);

        // Assert
        timerId.ShouldNotBeNull();
        Guid.TryParse(timerId, out var timerGuid).ShouldBeTrue();

        // Verify the timer was inserted into the custom table
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = "SELECT COUNT(*) FROM custom.CustomTimers WHERE Id = @Id AND Topic = @Topic";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", timerGuid);
        command.Parameters.AddWithValue("@Topic", topic);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);
    }

    /// <summary>When scheduling a timer, then default timer columns are initialized.</summary>
    /// <intent>Verify default status and metadata values on insert.</intent>
    /// <scenario>Schedule a timer and read the stored row.</scenario>
    /// <behavior>Status is Pending, ClaimedBy/ClaimedAt are null, RetryCount is 0, and CreatedAt is recent.</behavior>
    [Fact]
    public async Task ScheduleTimerAsync_WithValidParameters_SetsCorrectDefaults()
    {
        // Arrange
        string topic = "test-timer-defaults";
        string payload = "test timer defaults payload";
        DateTimeOffset dueTime = DateTimeOffset.UtcNow.AddMinutes(10);

        // Act
        var timerId = await schedulerClient!.ScheduleTimerAsync(topic, payload, dueTime, CancellationToken.None);

        // Verify the timer has correct default values
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = @"SELECT Status, ClaimedBy, ClaimedAt, RetryCount, CreatedAt 
                   FROM infra.Timers 
                   WHERE Id = @Id";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", Guid.Parse(timerId));

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Read().ShouldBeTrue();

        // Assert default values
        reader.GetString(0).ShouldBe("Pending"); // Status
        reader.IsDBNull(1).ShouldBeTrue(); // ClaimedBy
        reader.IsDBNull(2).ShouldBeTrue(); // ClaimedAt
        reader.GetInt32(3).ShouldBe(0); // RetryCount
        reader.GetDateTimeOffset(4).ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1)); // CreatedAt
    }

    /// <summary>When cancelling an existing timer, then CancelTimerAsync returns true and status is Cancelled.</summary>
    /// <intent>Verify cancellation updates stored timer status.</intent>
    /// <scenario>Schedule a timer, then cancel it by id.</scenario>
    /// <behavior>CancelTimerAsync returns true and the timer status is Cancelled.</behavior>
    [Fact]
    public async Task CancelTimerAsync_WithValidTimerId_UpdatesTimerStatus()
    {
        // Arrange
        string topic = "test-timer-cancel";
        string payload = "test timer cancel payload";
        DateTimeOffset dueTime = DateTimeOffset.UtcNow.AddMinutes(15);

        var timerId = await schedulerClient!.ScheduleTimerAsync(topic, payload, dueTime, CancellationToken.None);

        // Act
        var result = await schedulerClient!.CancelTimerAsync(timerId, CancellationToken.None);

        // Assert
        result.ShouldBeTrue();

        // Verify the timer status was updated
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = "SELECT Status FROM infra.Timers WHERE Id = @Id";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", Guid.Parse(timerId));

        var status = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        status.ShouldBe("Cancelled");
    }

    /// <summary>When cancelling a missing timer id, then CancelTimerAsync returns false.</summary>
    /// <intent>Confirm cancel is a no-op for unknown timers.</intent>
    /// <scenario>Call CancelTimerAsync with a random Guid string.</scenario>
    /// <behavior>The result is false.</behavior>
    [Fact]
    public async Task CancelTimerAsync_WithNonexistentTimerId_ReturnsFalse()
    {
        // Arrange
        string nonexistentTimerId = Guid.NewGuid().ToString();

        // Act
        var result = await schedulerClient!.CancelTimerAsync(nonexistentTimerId, CancellationToken.None);

        // Assert
        result.ShouldBeFalse();
    }

    // Job Tests
    /// <summary>When creating a job, then a job row is inserted with name, topic, and cron schedule.</summary>
    /// <intent>Validate CreateOrUpdateJobAsync persists new jobs.</intent>
    /// <scenario>Create a job with name, topic, cron, and payload.</scenario>
    /// <behavior>The jobs table contains one matching row.</behavior>
    [Fact]
    public async Task CreateOrUpdateJobAsync_WithValidParameters_InsertsJobToDatabase()
    {
        // Arrange
        string jobName = "test-job";
        string topic = "test-job-topic";
        string cronSchedule = "0 0 * * * *"; // Every hour
        string payload = "test job payload";

        // Act
        await schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, payload, CancellationToken.None);

        // Verify the job was inserted
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = @"SELECT COUNT(*) FROM infra.Jobs 
                   WHERE JobName = @JobName AND Topic = @Topic AND CronSchedule = @CronSchedule";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@CronSchedule", cronSchedule);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);
    }

    /// <summary>When creating a job with a null payload, then the stored payload remains null.</summary>
    /// <intent>Allow null payloads when defining jobs.</intent>
    /// <scenario>Create a job with payload set to null.</scenario>
    /// <behavior>The payload column is DBNull for the job.</behavior>
    [Fact]
    public async Task CreateOrUpdateJobAsync_WithNullPayload_InsertsJobSuccessfully()
    {
        // Arrange
        string jobName = "test-job-null-payload";
        string topic = "test-job-null-payload-topic";
        string cronSchedule = "0 */5 * * * *"; // Every 5 minutes

        // Act
        await schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, payload: null, CancellationToken.None);

        // Verify the job was inserted with null payload
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = @"SELECT Payload FROM infra.Jobs WHERE JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        result.ShouldBe(DBNull.Value);
    }

    /// <summary>When a job already exists, then CreateOrUpdateJobAsync updates it without duplication.</summary>
    /// <intent>Verify upsert behavior for existing jobs.</intent>
    /// <scenario>Create a job, then call CreateOrUpdateJobAsync again with a new topic.</scenario>
    /// <behavior>The job count remains one and the topic is updated.</behavior>
    [Fact]
    public async Task CreateOrUpdateJobAsync_ExistingJob_UpdatesJob()
    {
        // Arrange
        string jobName = "test-job-update";
        string originalTopic = "original-topic";
        string updatedTopic = "updated-topic";
        string cronSchedule = "0 0 * * * *";

        // Create initial job
        await schedulerClient!.CreateOrUpdateJobAsync(jobName, originalTopic, cronSchedule, CancellationToken.None);

        // Act - Update the job
        await schedulerClient!.CreateOrUpdateJobAsync(jobName, updatedTopic, cronSchedule, CancellationToken.None);

        // Verify the job was updated, not duplicated
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var countSql = "SELECT COUNT(*) FROM infra.Jobs WHERE JobName = @JobName";
        await using var countCommand = new SqlCommand(countSql, connection);
        countCommand.Parameters.AddWithValue("@JobName", jobName);

        var count = (int)await countCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);

        // Verify the topic was updated
        var topicSql = "SELECT Topic FROM infra.Jobs WHERE JobName = @JobName";
        await using var topicCommand = new SqlCommand(topicSql, connection);
        topicCommand.Parameters.AddWithValue("@JobName", jobName);

        var topic = (string)await topicCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        topic.ShouldBe(updatedTopic);
    }

    /// <summary>When deleting an existing job, then the job row is removed.</summary>
    /// <intent>Verify DeleteJobAsync removes jobs by name.</intent>
    /// <scenario>Create a job and then delete it by name.</scenario>
    /// <behavior>The jobs table contains zero matching rows.</behavior>
    [Fact]
    public async Task DeleteJobAsync_WithValidJobName_RemovesJob()
    {
        // Arrange
        string jobName = "test-job-delete";
        string topic = "test-job-delete-topic";
        string cronSchedule = "0 0 * * * *";

        await schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, CancellationToken.None);

        // Act
        await schedulerClient!.DeleteJobAsync(jobName, CancellationToken.None);

        // Verify the job was deleted
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = "SELECT COUNT(*) FROM infra.Jobs WHERE JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(0);
    }

    /// <summary>When triggering a job, then a job run is created for that job.</summary>
    /// <intent>Ensure TriggerJobAsync records a job run.</intent>
    /// <scenario>Create a job and trigger it by name.</scenario>
    /// <behavior>The job runs table contains at least one row for the job.</behavior>
    [Fact]
    public async Task TriggerJobAsync_WithValidJobName_CreatesJobRun()
    {
        // Arrange
        string jobName = "test-job-trigger";
        string topic = "test-job-trigger-topic";
        string cronSchedule = "0 0 * * * *";

        await schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, CancellationToken.None);

        // Act
        await schedulerClient!.TriggerJobAsync(jobName, CancellationToken.None);

        // Verify a job run was created
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var sql = @"SELECT COUNT(*) FROM infra.JobRuns jr
                   INNER JOIN infra.Jobs j ON jr.JobId = j.Id 
                   WHERE j.JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBeGreaterThan(0);
    }
}
