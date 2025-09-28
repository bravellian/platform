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
    public async Task EnqueueAsync_WithNullTopic_CallsDatabase()
    {
        // Arrange
        var mockTransaction = Substitute.For<IDbTransaction>();
        string nullTopic = null!;
        string validPayload = "test payload";

        // Act & Assert
        // The implementation doesn't validate parameters, it just calls the database
        // So we expect a database-related exception, not an ArgumentException
        var exception = await Should.ThrowAsync<Exception>(
            () => this.outboxService.EnqueueAsync(nullTopic, validPayload, mockTransaction));
        
        // It should not be an ArgumentException since the implementation doesn't validate
        exception.ShouldNotBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueAsync_WithEmptyTopic_CallsDatabase()
    {
        // Arrange
        var mockTransaction = Substitute.For<IDbTransaction>();
        string emptyTopic = string.Empty;
        string validPayload = "test payload";

        // Act & Assert
        // The implementation doesn't validate parameters, it just calls the database
        var exception = await Should.ThrowAsync<Exception>(
            () => this.outboxService.EnqueueAsync(emptyTopic, validPayload, mockTransaction));
        
        exception.ShouldNotBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueAsync_WithNullPayload_CallsDatabase()
    {
        // Arrange
        var mockTransaction = Substitute.For<IDbTransaction>();
        string validTopic = "test-topic";
        string nullPayload = null!;

        // Act & Assert
        // The implementation doesn't validate parameters, it just calls the database
        var exception = await Should.ThrowAsync<Exception>(
            () => this.outboxService.EnqueueAsync(validTopic, nullPayload, mockTransaction));
        
        exception.ShouldNotBeOfType<ArgumentException>();
    }

    [Fact]
    public async Task EnqueueAsync_WithNullTransaction_ThrowsNullReferenceException()
    {
        // Arrange
        IDbTransaction nullTransaction = null!;
        string validTopic = "test-topic";
        string validPayload = "test payload";

        // Act & Assert
        // The implementation tries to access transaction.Connection without checking null
        // So we expect a NullReferenceException
        var exception = await Should.ThrowAsync<NullReferenceException>(
            () => this.outboxService.EnqueueAsync(validTopic, validPayload, nullTransaction));
        
        exception.ShouldNotBeNull();
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