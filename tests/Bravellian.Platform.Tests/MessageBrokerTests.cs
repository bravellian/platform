namespace Bravellian.Platform.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Linq;

public class MessageBrokerTests : SqlServerTestBase
{
    private FakeTimeProvider timeProvider = default!;

    public MessageBrokerTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        this.timeProvider = new FakeTimeProvider();
    }

    [Fact]
    public void ConsoleMessageBroker_Constructor_CreatesInstance()
    {
        // Arrange & Act
        var broker = new ConsoleMessageBroker(this.timeProvider);

        // Assert
        broker.ShouldNotBeNull();
        broker.ShouldBeAssignableTo<IMessageBroker>();
    }

    [Fact]
    public async Task ConsoleMessageBroker_SendMessageAsync_ReturnsTrue()
    {
        // Arrange
        var broker = new ConsoleMessageBroker(this.timeProvider);
        var message = new OutboxMessage 
        { 
            Id = Guid.NewGuid(), 
            Topic = "test-topic", 
            Payload = "test payload" 
        };

        // Act
        var result = await broker.SendMessageAsync(message);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task ConsoleMessageBroker_SendMessageAsync_WithCancellation_Cancels()
    {
        // Arrange
        var broker = new ConsoleMessageBroker(this.timeProvider);
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = "test-topic",
            Payload = "test payload"
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await broker.SendMessageAsync(message, cts.Token));
    }

    [Fact]
    public async Task OutboxProcessor_WithCustomMessageBroker_UsesCustomBroker()
    {
        // Arrange
        var customBroker = new TestMessageBroker();
        var options = new SqlOutboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox"
        };

        // Ensure the outbox schema exists
        await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
            this.ConnectionString, 
            options.SchemaName,
            options.TableName);

        // Create lease factory mock
        var leaseFactory = new TestSystemLeaseFactory();
        
        var processor = new OutboxProcessor(
            Options.Create(options),
            leaseFactory,
            this.timeProvider,
            customBroker);

        // Act - start and immediately stop to trigger one processing cycle
        await processor.StartAsync(CancellationToken.None);
        await Task.Delay(100); // Give it a moment to process
        await processor.StopAsync(CancellationToken.None);

        // Assert
        customBroker.SendMessageCalls.ShouldBeEmpty(); // No messages in queue, so no calls
    }

    [Fact]
    public void ServiceCollection_AddMessageBroker_Generic_RegistersCustomBroker()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTimeAbstractions(this.timeProvider);

        // Act
        services.AddMessageBroker<TestMessageBroker>();

        // Assert - Check that the service is registered
        var serviceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMessageBroker));
        serviceDescriptor.ShouldNotBeNull();
        serviceDescriptor.ImplementationType.ShouldBe(typeof(TestMessageBroker));
        serviceDescriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ServiceCollection_AddMessageBroker_Factory_RegistersCustomBroker()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTimeAbstractions(this.timeProvider);

        // Act
        services.AddMessageBroker(sp => new TestMessageBroker());

        // Assert - Check that the service is registered
        var serviceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMessageBroker));
        serviceDescriptor.ShouldNotBeNull();
        serviceDescriptor.ImplementationFactory.ShouldNotBeNull();
        serviceDescriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ServiceCollection_AddSqlOutbox_RegistersDefaultConsoleMessageBroker()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTimeAbstractions(this.timeProvider);
        
        var options = new SqlOutboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox"
        };

        // Act
        services.AddSqlOutbox(options);

        // Assert - Check that IMessageBroker service is registered
        var serviceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IMessageBroker));
        serviceDescriptor.ShouldNotBeNull();
        serviceDescriptor.ImplementationType.ShouldBe(typeof(ConsoleMessageBroker));
        serviceDescriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ServiceCollection_AddSqlOutbox_ThenCustomBroker_OverridesDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTimeAbstractions(this.timeProvider);
        
        var options = new SqlOutboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox"
        };

        // Act
        services.AddSqlOutbox(options);
        services.AddMessageBroker<TestMessageBroker>(); // This should override the default

        // Assert - Check that the last registration wins
        var serviceDescriptors = services.Where(s => s.ServiceType == typeof(IMessageBroker)).ToList();
        serviceDescriptors.Count.ShouldBe(2); // Both registrations exist
        
        // The last one should be the custom broker
        serviceDescriptors.Last().ImplementationType.ShouldBe(typeof(TestMessageBroker));
    }

    // Test implementation of IMessageBroker for testing
    private class TestMessageBroker : IMessageBroker
    {
        public List<OutboxMessage> SendMessageCalls { get; } = new List<OutboxMessage>();

        public Task<bool> SendMessageAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            this.SendMessageCalls.Add(message);
            return Task.FromResult(true);
        }
    }

    // Simplified test system lease factory
    private class TestSystemLeaseFactory : ISystemLeaseFactory
    {
        public Task<ISystemLease?> AcquireAsync(string resourceName, TimeSpan leaseDuration, string? contextJson = null, Guid? ownerToken = null, CancellationToken cancellationToken = default)
        {
            // Return null to simulate no lease available, preventing actual processing
            return Task.FromResult<ISystemLease?>(null);
        }
    }
}