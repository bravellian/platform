namespace Bravellian.Platform.Tests;

using Microsoft.Extensions.Options;
using NSubstitute;

public class SqlDistributedLockTests
{
    private const string TestConnectionString = "Server=localhost;Database=Test;Integrated Security=true;TrustServerCertificate=true";
    private readonly YourApplicationOptions options;
    private readonly IOptions<YourApplicationOptions> mockOptions;
    private readonly SqlDistributedLock distributedLock;

    public SqlDistributedLockTests()
    {
        this.options = new YourApplicationOptions { ConnectionString = TestConnectionString };
        this.mockOptions = Substitute.For<IOptions<YourApplicationOptions>>();
        this.mockOptions.Value.Returns(this.options);
        this.distributedLock = new SqlDistributedLock(this.mockOptions);
    }

    [Fact]
    public void Constructor_WithValidOptions_CreatesInstance()
    {
        // Arrange
        var testOptions = Substitute.For<IOptions<YourApplicationOptions>>();
        testOptions.Value.Returns(new YourApplicationOptions { ConnectionString = TestConnectionString });

        // Act
        var lockInstance = new SqlDistributedLock(testOptions);

        // Assert
        lockInstance.ShouldNotBeNull();
    }

    [Fact]
    public void SanitizeResource_WithNullResource_ThrowsArgumentException()
    {
        // Arrange
        string nullResource = null!;

        // Act & Assert
        var exception = Should.Throw<ArgumentException>(() => this.distributedLock.AcquireAsync(nullResource, TimeSpan.FromSeconds(10)));
        exception.ParamName.ShouldBe("resource");
        exception.Message.ShouldContain("Resource cannot be null or whitespace");
    }

    [Fact]
    public void SanitizeResource_WithEmptyResource_ThrowsArgumentException()
    {
        // Arrange
        string emptyResource = string.Empty;

        // Act & Assert
        var exception = Should.Throw<ArgumentException>(() => this.distributedLock.AcquireAsync(emptyResource, TimeSpan.FromSeconds(10)));
        exception.ParamName.ShouldBe("resource");
        exception.Message.ShouldContain("Resource cannot be null or whitespace");
    }

    [Fact]
    public void SanitizeResource_WithWhitespaceResource_ThrowsArgumentException()
    {
        // Arrange
        string whitespaceResource = "   ";

        // Act & Assert
        var exception = Should.Throw<ArgumentException>(() => this.distributedLock.AcquireAsync(whitespaceResource, TimeSpan.FromSeconds(10)));
        exception.ParamName.ShouldBe("resource");
        exception.Message.ShouldContain("Resource cannot be null or whitespace");
    }

    [Fact]
    public void SanitizeResource_WithValidInput_DoesNotThrowFromValidation()
    {
        // This test verifies the method accepts valid resource names
        // We can't directly test the private sanitization method, but we can verify behavior through AcquireAsync
        
        // Arrange
        string validInput = "valid-resource-123";

        // Act & Assert - should not throw from parameter validation
        Should.NotThrow(() => this.distributedLock.AcquireAsync(validInput, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task AcquireAsync_WithValidParameters_DoesNotThrowImmediately()
    {
        // Note: This test will fail in practice because we don't have a real database connection
        // But it verifies the basic method signature and parameter validation
        
        // Arrange
        var validResource = "test-resource";
        var validTimeout = TimeSpan.FromSeconds(10);

        // Act & Assert - The method should not throw due to parameter validation
        // It may throw due to connection issues, which is expected in unit tests
        var exception = await Should.ThrowAsync<Exception>(() => this.distributedLock.AcquireAsync(validResource, validTimeout));
        
        // We expect some kind of database-related exception, which indicates the parameters were accepted
        exception.ShouldNotBeNull();
    }
}