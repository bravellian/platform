namespace Bravellian.Platform.Tests;

using Microsoft.Data.SqlClient;

public class SqlSchedulerClientTests : SqlServerTestBase
{
    private SqlSchedulerClient? schedulerClient;

    public SqlSchedulerClientTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        this.schedulerClient = new SqlSchedulerClient(this.ConnectionString);
    }

    [Fact]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        // Arrange & Act
        var client = new SqlSchedulerClient(this.ConnectionString);

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
        var timerId = await this.schedulerClient!.ScheduleTimerAsync(topic, payload, dueTime);

        // Assert
        timerId.ShouldNotBeNull();
        Guid.TryParse(timerId, out var timerGuid).ShouldBeTrue();

        // Verify the timer was inserted
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        var sql = "SELECT COUNT(*) FROM dbo.Timers WHERE Id = @Id AND Topic = @Topic";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", timerGuid);
        command.Parameters.AddWithValue("@Topic", topic);
        
        var count = (int)await command.ExecuteScalarAsync();
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
        var timerId = await this.schedulerClient!.ScheduleTimerAsync(topic, payload, dueTime);

        // Verify the timer has correct default values
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        var sql = @"SELECT Status, ClaimedBy, ClaimedAt, RetryCount, CreatedAt 
                   FROM dbo.Timers 
                   WHERE Id = @Id";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", Guid.Parse(timerId));
        
        await using var reader = await command.ExecuteReaderAsync();
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
        
        var timerId = await this.schedulerClient!.ScheduleTimerAsync(topic, payload, dueTime);

        // Act
        var result = await this.schedulerClient!.CancelTimerAsync(timerId);

        // Assert
        result.ShouldBeTrue();

        // Verify the timer status was updated
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        var sql = "SELECT Status FROM dbo.Timers WHERE Id = @Id";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", Guid.Parse(timerId));
        
        var status = (string?)await command.ExecuteScalarAsync();
        status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task CancelTimerAsync_WithNonexistentTimerId_ReturnsFalse()
    {
        // Arrange
        string nonexistentTimerId = Guid.NewGuid().ToString();

        // Act
        var result = await this.schedulerClient!.CancelTimerAsync(nonexistentTimerId);

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
        await this.schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, payload);

        // Verify the job was inserted
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        var sql = @"SELECT COUNT(*) FROM dbo.Jobs 
                   WHERE JobName = @JobName AND Topic = @Topic AND CronSchedule = @CronSchedule";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);
        command.Parameters.AddWithValue("@Topic", topic);
        command.Parameters.AddWithValue("@CronSchedule", cronSchedule);
        
        var count = (int)await command.ExecuteScalarAsync();
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
        await this.schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule, payload: null);

        // Verify the job was inserted with null payload
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        var sql = @"SELECT Payload FROM dbo.Jobs WHERE JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);
        
        var result = await command.ExecuteScalarAsync();
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
        await this.schedulerClient!.CreateOrUpdateJobAsync(jobName, originalTopic, cronSchedule);

        // Act - Update the job
        await this.schedulerClient!.CreateOrUpdateJobAsync(jobName, updatedTopic, cronSchedule);

        // Verify the job was updated, not duplicated
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        var countSql = "SELECT COUNT(*) FROM dbo.Jobs WHERE JobName = @JobName";
        await using var countCommand = new SqlCommand(countSql, connection);
        countCommand.Parameters.AddWithValue("@JobName", jobName);
        
        var count = (int)await countCommand.ExecuteScalarAsync();
        count.ShouldBe(1);

        // Verify the topic was updated
        var topicSql = "SELECT Topic FROM dbo.Jobs WHERE JobName = @JobName";
        await using var topicCommand = new SqlCommand(topicSql, connection);
        topicCommand.Parameters.AddWithValue("@JobName", jobName);
        
        var topic = (string)await topicCommand.ExecuteScalarAsync();
        topic.ShouldBe(updatedTopic);
    }

    [Fact]
    public async Task DeleteJobAsync_WithValidJobName_RemovesJob()
    {
        // Arrange
        string jobName = "test-job-delete";
        string topic = "test-job-delete-topic";
        string cronSchedule = "0 0 * * * *";

        await this.schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule);

        // Act
        await this.schedulerClient!.DeleteJobAsync(jobName);

        // Verify the job was deleted
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        var sql = "SELECT COUNT(*) FROM dbo.Jobs WHERE JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);
        
        var count = (int)await command.ExecuteScalarAsync();
        count.ShouldBe(0);
    }

    [Fact]
    public async Task TriggerJobAsync_WithValidJobName_CreatesJobRun()
    {
        // Arrange
        string jobName = "test-job-trigger";
        string topic = "test-job-trigger-topic";
        string cronSchedule = "0 0 * * * *";

        await this.schedulerClient!.CreateOrUpdateJobAsync(jobName, topic, cronSchedule);

        // Act
        await this.schedulerClient!.TriggerJobAsync(jobName);

        // Verify a job run was created
        await using var connection = new SqlConnection(this.ConnectionString);
        await connection.OpenAsync();
        var sql = @"SELECT COUNT(*) FROM dbo.JobRuns jr
                   INNER JOIN dbo.Jobs j ON jr.JobId = j.Id 
                   WHERE j.JobName = @JobName";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobName", jobName);
        
        var count = (int)await command.ExecuteScalarAsync();
        count.ShouldBeGreaterThan(0);
    }
}