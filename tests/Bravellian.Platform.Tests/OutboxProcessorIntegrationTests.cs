namespace Bravellian.Platform.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;

public class OutboxProcessorIntegrationTests : SqlServerTestBase
{
    public OutboxProcessorIntegrationTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public void OutboxProcessor_ConstructsWithCustomMessageBroker()
    {
        // Arrange  
        var testBroker = new TestMessageBroker();
        var options = Options.Create(new SqlOutboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox"
        });
        
        var leaseFactory = new SqlLeaseFactory(Options.Create(new SystemLeaseOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo"
        }), Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlLeaseFactory>.Instance);
        
        var timeProvider = TimeProvider.System;
        
        // Act & Assert - Should not throw, demonstrating successful DI
        var processor = new OutboxProcessor(options, leaseFactory, timeProvider, testBroker);
        processor.ShouldNotBeNull();
    }

    [Fact]
    public void ServiceProvider_ResolvesCorrectMessageBrokerWhenOverridden()
    {
        // Arrange
        var services = new ServiceCollection();
        var testBroker = new TestMessageBroker();
        
        services.AddSqlOutbox(new SqlOutboxOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            TableName = "Outbox"
        });
        
        // Override with custom broker
        services.AddMessageBroker(sp => testBroker);

        // Act - Check that the service is registered correctly
        var brokerDescriptor = services.Last(s => s.ServiceType == typeof(IMessageBroker));
        
        // Assert
        brokerDescriptor.ShouldNotBeNull();
        brokerDescriptor.ImplementationFactory.ShouldNotBeNull();
        brokerDescriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    // Test implementation that captures sent messages
    private class TestMessageBroker : IMessageBroker
    {
        public List<OutboxMessage> SentMessages { get; } = new List<OutboxMessage>();

        public Task<bool> SendMessageAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        {
            this.SentMessages.Add(message);
            return Task.FromResult(true);
        }
    }
}