namespace Bravellian.Platform.Tests;

public class SqlSchedulerClientTests
{
    private const string TestConnectionString = "Server=localhost;Database=Test;Integrated Security=true;TrustServerCertificate=true";
    private readonly SqlSchedulerClient schedulerClient;

    public SqlSchedulerClientTests()
    {
        this.schedulerClient = new SqlSchedulerClient(TestConnectionString);
    }

    [Fact]
    public void Constructor_WithValidConnectionString_CreatesInstance()
    {
        // Arrange & Act
        var client = new SqlSchedulerClient(TestConnectionString);

        // Assert
        client.ShouldNotBeNull();
        client.ShouldBeAssignableTo<ISchedulerClient>();
    }

    // Timer Tests
    [Fact]
    public async Task ScheduleTimerAsync_WithValidParameters_DoesNotThrowFromValidation()
    {
        // Arrange
        string validTopic = "test-topic";
        string validPayload = "test payload";
        DateTimeOffset validDueTime = DateTimeOffset.UtcNow.AddMinutes(5);

        // Act & Assert
        // We expect this to fail with a database-related exception
        // but it should pass parameter validation
        var exception = await Should.ThrowAsync<Exception>(
            () => this.schedulerClient.ScheduleTimerAsync(validTopic, validPayload, validDueTime));
        
        // The exception should not be an ArgumentException since parameters are valid
        exception.ShouldNotBeOfType<ArgumentException>();
        exception.ShouldNotBeOfType<ArgumentNullException>();
    }

    [Fact]
    public async Task CancelTimerAsync_WithValidTimerId_DoesNotThrowFromValidation()
    {
        // Arrange
        string validTimerId = Guid.NewGuid().ToString();

        // Act & Assert
        // We expect this to fail with a database-related exception
        // but it should pass parameter validation
        var exception = await Should.ThrowAsync<Exception>(
            () => this.schedulerClient.CancelTimerAsync(validTimerId));
        
        // The exception should not be an ArgumentException since parameters are valid
        exception.ShouldNotBeOfType<ArgumentException>();
        exception.ShouldNotBeOfType<ArgumentNullException>();
    }

    // Job Tests
    [Fact]
    public async Task CreateOrUpdateJobAsync_WithValidParameters_DoesNotThrowFromValidation()
    {
        // Arrange
        string validJobName = "test-job";
        string validTopic = "test-topic";
        string validCronSchedule = "0 0 * * * *";
        string? payload = "test payload";

        // Act & Assert
        // We expect this to fail with a database-related exception
        // but it should pass parameter validation
        var exception = await Should.ThrowAsync<Exception>(
            () => this.schedulerClient.CreateOrUpdateJobAsync(validJobName, validTopic, validCronSchedule, payload));
        
        // The exception should not be an ArgumentException since parameters are valid
        exception.ShouldNotBeOfType<ArgumentException>();
        exception.ShouldNotBeOfType<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteJobAsync_WithValidJobName_DoesNotThrowFromValidation()
    {
        // Arrange
        string validJobName = "test-job";

        // Act & Assert
        // We expect this to fail with a database-related exception
        // but it should pass parameter validation
        var exception = await Should.ThrowAsync<Exception>(
            () => this.schedulerClient.DeleteJobAsync(validJobName));
        
        // The exception should not be an ArgumentException since parameters are valid
        exception.ShouldNotBeOfType<ArgumentException>();
        exception.ShouldNotBeOfType<ArgumentNullException>();
    }

    [Fact]
    public async Task TriggerJobAsync_WithValidJobName_DoesNotThrowFromValidation()
    {
        // Arrange
        string validJobName = "test-job";

        // Act & Assert
        // We expect this to fail with a database-related exception
        // but it should pass parameter validation
        var exception = await Should.ThrowAsync<Exception>(
            () => this.schedulerClient.TriggerJobAsync(validJobName));
        
        // The exception should not be an ArgumentException since parameters are valid
        exception.ShouldNotBeOfType<ArgumentException>();
        exception.ShouldNotBeOfType<ArgumentNullException>();
    }
}