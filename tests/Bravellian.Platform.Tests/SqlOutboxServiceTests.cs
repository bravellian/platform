namespace Bravellian.Platform.Tests;

using System.Data;
using NSubstitute;

public class SqlOutboxServiceTests
{
    private readonly SqlOutboxService outboxService;

    public SqlOutboxServiceTests()
    {
        this.outboxService = new SqlOutboxService();
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        var service = new SqlOutboxService();

        // Assert
        service.ShouldNotBeNull();
        service.ShouldBeAssignableTo<IOutbox>();
    }

    [Fact]
    public async Task EnqueueAsync_WithNullTopic_ThrowsArgumentException()
    {
        // Arrange
        var mockTransaction = Substitute.For<IDbTransaction>();
        string nullTopic = null!;
        string validPayload = "test payload";

        // Act & Assert
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => this.outboxService.EnqueueAsync(nullTopic, validPayload, mockTransaction));
        exception.ParamName.ShouldBe("topic");
    }

    [Fact]
    public async Task EnqueueAsync_WithEmptyTopic_ThrowsArgumentException()
    {
        // Arrange
        var mockTransaction = Substitute.For<IDbTransaction>();
        string emptyTopic = string.Empty;
        string validPayload = "test payload";

        // Act & Assert
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => this.outboxService.EnqueueAsync(emptyTopic, validPayload, mockTransaction));
        exception.ParamName.ShouldBe("topic");
    }

    [Fact]
    public async Task EnqueueAsync_WithNullPayload_ThrowsArgumentException()
    {
        // Arrange
        var mockTransaction = Substitute.For<IDbTransaction>();
        string validTopic = "test-topic";
        string nullPayload = null!;

        // Act & Assert
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => this.outboxService.EnqueueAsync(validTopic, nullPayload, mockTransaction));
        exception.ParamName.ShouldBe("payload");
    }

    [Fact]
    public async Task EnqueueAsync_WithNullTransaction_ThrowsArgumentNullException()
    {
        // Arrange
        IDbTransaction nullTransaction = null!;
        string validTopic = "test-topic";
        string validPayload = "test payload";

        // Act & Assert
        var exception = await Should.ThrowAsync<ArgumentNullException>(
            () => this.outboxService.EnqueueAsync(validTopic, validPayload, nullTransaction));
        exception.ParamName.ShouldBe("transaction");
    }

    [Fact]
    public async Task EnqueueAsync_WithValidParameters_DoesNotThrowFromValidation()
    {
        // Arrange
        var mockTransaction = Substitute.For<IDbTransaction>();
        string validTopic = "test-topic";
        string validPayload = "test payload";
        string? correlationId = "test-correlation-id";

        // Act & Assert
        // We expect this to fail with a database-related exception since we're using mocked transactions
        // but it should pass parameter validation
        var exception = await Should.ThrowAsync<Exception>(
            () => this.outboxService.EnqueueAsync(validTopic, validPayload, mockTransaction, correlationId));
        
        // The exception should not be an ArgumentException since parameters are valid
        exception.ShouldNotBeOfType<ArgumentException>();
        exception.ShouldNotBeOfType<ArgumentNullException>();
    }
}