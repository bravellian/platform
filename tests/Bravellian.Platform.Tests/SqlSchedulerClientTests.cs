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
    private readonly SqlSchedulerOptions defaultOptions = new() { ConnectionString = string.Empty, SchemaName = "dbo", JobsTableName = "Jobs", JobRunsTableName = "JobRuns", TimersTableName = "Timers" };

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
        var sql = "SELECT COUNT(*) FROM dbo.Timers WHERE Id = @Id AND Topic = @Topic";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", timerGuid);
        command.Parameters.AddWithValue("@Topic", topic);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);
    }

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
                   FROM dbo.Timers 
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
        reader.GetDateTimeOffset(4).ShouldBeGreaterThan(DateTimeOffset.Now.AddMinutes(-1)); // CreatedAt
    }

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
        var sql = "SELECT Status FROM dbo.Timers WHERE Id = @Id";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", Guid.Parse(timerId));

        var status = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        status.ShouldBe("Cancelled");
    }

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
        var sql = @"SELECT COUNT(*) FROM dbo.Jobs 
                   WHERE JobName = @JobName AND Topic = @Topic AND CronSchedule = @CronSchedule";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@CronSchedule", cronSchedule);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);
    }

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
        var sql = @"SELECT Payload FROM dbo.Jobs WHERE JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        result.ShouldBe(DBNull.Value);
    }

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
        var countSql = "SELECT COUNT(*) FROM dbo.Jobs WHERE JobName = @JobName";
        await using var countCommand = new SqlCommand(countSql, connection);
        countCommand.Parameters.AddWithValue("@JobName", jobName);

        var count = (int)await countCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(1);

        // Verify the topic was updated
        var topicSql = "SELECT Topic FROM dbo.Jobs WHERE JobName = @JobName";
        await using var topicCommand = new SqlCommand(topicSql, connection);
        topicCommand.Parameters.AddWithValue("@JobName", jobName);

        var topic = (string)await topicCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        topic.ShouldBe(updatedTopic);
    }

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
        var sql = "SELECT COUNT(*) FROM dbo.Jobs WHERE JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBe(0);
    }

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
        var sql = @"SELECT COUNT(*) FROM dbo.JobRuns jr
                   INNER JOIN dbo.Jobs j ON jr.JobId = j.Id 
                   WHERE j.JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);

        var count = (int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        count.ShouldBeGreaterThan(0);
    }
}
