namespace Bravellian.Platform.Tests;

using Microsoft.Extensions.Options;

public class SqlDistributedLockTests : SqlServerTestBase
{
    private SqlDistributedLockOptions? options;
    private IOptions<SqlDistributedLockOptions>? mockOptions;
    private SqlDistributedLock? distributedLock;

    public SqlDistributedLockTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        
        // Now that the container is started, we can set up the lock
        this.options = new SqlDistributedLockOptions { ConnectionString = this.ConnectionString };
        this.mockOptions = Options.Create(this.options);
        this.distributedLock = new SqlDistributedLock(this.mockOptions);
    }

    [Fact]
    public void Constructor_WithValidOptions_CreatesInstance()
    {
        // Arrange
        var testOptions = Options.Create(new SqlDistributedLockOptions { ConnectionString = this.ConnectionString });

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
        var exception = Should.Throw<ArgumentException>(() => this.distributedLock!.AcquireAsync(nullResource, TimeSpan.FromSeconds(10)));
        exception.ParamName.ShouldBe("resource");
        exception.Message.ShouldContain("Resource cannot be null or whitespace");
    }

    [Fact]
    public void SanitizeResource_WithEmptyResource_ThrowsArgumentException()
    {
        // Arrange
        string emptyResource = string.Empty;

        // Act & Assert
        var exception = Should.Throw<ArgumentException>(() => this.distributedLock!.AcquireAsync(emptyResource, TimeSpan.FromSeconds(10)));
        exception.ParamName.ShouldBe("resource");
        exception.Message.ShouldContain("Resource cannot be null or whitespace");
    }

    [Fact]
    public void SanitizeResource_WithWhitespaceResource_ThrowsArgumentException()
    {
        // Arrange
        string whitespaceResource = "   ";

        // Act & Assert
        var exception = Should.Throw<ArgumentException>(() => this.distributedLock!.AcquireAsync(whitespaceResource, TimeSpan.FromSeconds(10)));
        exception.ParamName.ShouldBe("resource");
        exception.Message.ShouldContain("Resource cannot be null or whitespace");
    }

    [Fact]
    public async Task AcquireAsync_WithValidResource_CanAcquireLock()
    {
        // Arrange
        string validResource = "test-resource-123";
        var timeout = TimeSpan.FromSeconds(30); // Increased timeout

        // Act
        var lockHandle = await this.distributedLock!.AcquireAsync(validResource, timeout);

        // Assert
        lockHandle.ShouldNotBeNull();

        // Cleanup
        if (lockHandle != null)
        {
            await lockHandle.DisposeAsync();
        }
    }

    [Fact]
    public async Task AcquireAsync_SameResourceTwice_SecondCallReturnsNull()
    {
        // Arrange
        string validResource = "test-resource-456";
        var timeout = TimeSpan.FromSeconds(1);

        // Act
        var firstLock = await this.distributedLock!.AcquireAsync(validResource, timeout);
        var secondLock = await this.distributedLock.AcquireAsync(validResource, timeout);

        // Assert
        firstLock.ShouldNotBeNull();
        secondLock.ShouldBeNull(); // Should be null because first lock is still held

        // Cleanup
        await firstLock.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_AfterLockReleased_CanAcquireAgain()
    {
        // Arrange
        string validResource = "test-resource-789";
        var timeout = TimeSpan.FromSeconds(10);

        // Act & Assert - First acquisition
        var firstLock = await this.distributedLock!.AcquireAsync(validResource, timeout);
        firstLock.ShouldNotBeNull();

        // Release the first lock
        await firstLock.DisposeAsync();

        // Second acquisition should succeed
        var secondLock = await this.distributedLock.AcquireAsync(validResource, timeout);
        secondLock.ShouldNotBeNull();

        // Cleanup
        await secondLock.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_WithDifferentResources_BothSucceed()
    {
        // Arrange
        string resource1 = "test-resource-1";
        string resource2 = "test-resource-2";
        var timeout = TimeSpan.FromSeconds(10);

        // Act
        var lock1 = await this.distributedLock!.AcquireAsync(resource1, timeout);
        var lock2 = await this.distributedLock.AcquireAsync(resource2, timeout);

        // Assert
        lock1.ShouldNotBeNull();
        lock2.ShouldNotBeNull();

        // Cleanup
        await lock1.DisposeAsync();
        await lock2.DisposeAsync();
    }
}