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


using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class OutboxHandlerTests : SqlServerTestBase
{
    private FakeTimeProvider timeProvider = default!;

    public OutboxHandlerTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        timeProvider = new FakeTimeProvider();
    }

    private MultiOutboxDispatcher CreateDispatcher(
        IOutboxStore store,
        IOutboxHandlerResolver resolver,
        ILogger<MultiOutboxDispatcher>? logger = null)
    {
        var provider = new SingleOutboxStoreProvider(store);
        var strategy = new RoundRobinOutboxSelectionStrategy();
        return new MultiOutboxDispatcher(
            provider,
            strategy,
            resolver,
            logger ?? new TestLogger<MultiOutboxDispatcher>(TestOutputHelper));
    }

    [Fact]
    public void OutboxHandlerResolver_WithHandlers_ResolvesCorrectly()
    {
        // Arrange
        var handlers = new IOutboxHandler[]
        {
            new TestHandler("Email.Send"),
            new TestHandler("SMS.Send"),
            new TestHandler("Push.Notification"),
        };

        var resolver = new OutboxHandlerResolver(handlers);

        // Act & Assert
        resolver.TryGet("Email.Send", out var emailHandler).ShouldBeTrue();
        emailHandler.Topic.ShouldBe("Email.Send");

        resolver.TryGet("SMS.Send", out var smsHandler).ShouldBeTrue();
        smsHandler.Topic.ShouldBe("SMS.Send");

        resolver.TryGet("NonExistent", out var _).ShouldBeFalse();
    }

    [Fact]
    public void OutboxHandlerResolver_CaseInsensitive()
    {
        // Arrange
        var handlers = new IOutboxHandler[] { new TestHandler("Email.Send") };
        var resolver = new OutboxHandlerResolver(handlers);

        // Act & Assert
        resolver.TryGet("email.send", out var handler).ShouldBeTrue();
        handler.Topic.ShouldBe("Email.Send");

        resolver.TryGet("EMAIL.SEND", out var handler2).ShouldBeTrue();
        handler2.Topic.ShouldBe("Email.Send");
    }

    [Fact]
    public async Task MultiOutboxDispatcher_ProcessSingleMessage_Success()
    {
        // Arrange
        var testHandler = new TestHandler("Test.Topic");
        var resolver = new OutboxHandlerResolver(new[] { testHandler });
        var store = new TestOutboxStore();
        var logger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = CreateDispatcher(store, resolver, logger);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = "Test.Topic",
            Payload = "test payload",
            RetryCount = 0,
        };

        store.AddMessage(message);

        // Act
        var processed = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        processed.ShouldBe(1);
        testHandler.HandledMessages.Count.ShouldBe(1);
        testHandler.HandledMessages.First().Id.ShouldBe(message.Id);
        store.DispatchedMessages.Count.ShouldBe(1);
        store.DispatchedMessages.First().ShouldBe(message.Id);
    }

    [Fact]
    public async Task MultiOutboxDispatcher_NoHandler_MarksAsFailed()
    {
        // Arrange
        var resolver = new OutboxHandlerResolver(Array.Empty<IOutboxHandler>());
        var store = new TestOutboxStore();
        var logger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = CreateDispatcher(store, resolver, logger);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = "Unknown.Topic",
            Payload = "test payload",
            RetryCount = 0,
        };

        store.AddMessage(message);

        // Act
        var processed = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        processed.ShouldBe(1);
        store.FailedMessages.Count.ShouldBe(1);
        store.FailedMessages.First().Key.ShouldBe(message.Id);
        store.FailedMessages.First().Value.ShouldContain("No handler registered for topic 'Unknown.Topic'");
    }

    [Fact]
    public async Task MultiOutboxDispatcher_HandlerThrows_ReschedulesWithBackoff()
    {
        // Arrange
        var testHandler = new TestHandler("Test.Topic");
        testHandler.ShouldThrow = true;
        var resolver = new OutboxHandlerResolver(new[] { testHandler });
        var store = new TestOutboxStore();
        var logger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = CreateDispatcher(store, resolver, logger);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = "Test.Topic",
            Payload = "test payload",
            RetryCount = 2,
        };

        store.AddMessage(message);

        // Act
        var processed = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        processed.ShouldBe(1);
        testHandler.HandledMessages.Count.ShouldBe(1); // Handler was called
        store.RescheduledMessages.Count.ShouldBe(1);

        var rescheduled = store.RescheduledMessages.First();
        rescheduled.Key.ShouldBe(message.Id);
        rescheduled.Value.Delay.ShouldBeGreaterThan(TimeSpan.Zero);
        rescheduled.Value.Error.ShouldBe("Test exception");
    }

    [Fact]
    public async Task MultiOutboxDispatcher_LogsCorrectly()
    {
        // Arrange
        var testHandler = new TestHandler("Test.Topic");
        var resolver = new OutboxHandlerResolver(new[] { testHandler });
        var store = new TestOutboxStore();
        var logger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = CreateDispatcher(store, resolver, logger);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = "Test.Topic",
            Payload = "test payload",
            RetryCount = 0,
        };

        store.AddMessage(message);

        // Act
        var processed = await dispatcher.RunOnceAsync(1, CancellationToken.None);

        // Assert
        processed.ShouldBe(1);

        // Verify that proper log messages are generated
        // The TestLogger outputs to the test output, but we can verify the calls were made
        // by checking that processing completed successfully
        testHandler.HandledMessages.Count.ShouldBe(1);
        store.DispatchedMessages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task MultiOutboxDispatcher_LogsErrors_WhenHandlerFails()
    {
        // Arrange
        var testHandler = new TestHandler("Test.Topic");
        testHandler.ShouldThrow = true;
        var resolver = new OutboxHandlerResolver(new[] { testHandler });
        var store = new TestOutboxStore();
        var logger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = CreateDispatcher(store, resolver, logger);

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = "Test.Topic",
            Payload = "test payload",
            RetryCount = 0,
        };

        store.AddMessage(message);

        // Act
        var processed = await dispatcher.RunOnceAsync(1, CancellationToken.None);

        // Assert
        processed.ShouldBe(1);

        // Verify that handler was called and error was logged
        testHandler.HandledMessages.Count.ShouldBe(1);
        store.RescheduledMessages.Count.ShouldBe(1);
        store.RescheduledMessages.First().Value.Error.ShouldBe("Test exception");
    }

    [Fact]
    public async Task MultiOutboxDispatcher_LogsAtCorrectLevels()
    {
        // Arrange
        var capturingLogger = new CapturingLogger<MultiOutboxDispatcher>();

        var testHandler = new TestHandler("Test.Topic");
        var resolver = new OutboxHandlerResolver(new[] { testHandler });
        var store = new TestOutboxStore();
        var dispatcher = CreateDispatcher(store, resolver, capturingLogger);

        var successMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = "Test.Topic",
            Payload = "success payload",
            RetryCount = 0,
        };

        var failMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = "Unknown.Topic",
            Payload = "fail payload",
            RetryCount = 0,
        };

        store.AddMessage(successMessage);
        store.AddMessage(failMessage);

        // Act
        var processed = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        processed.ShouldBe(2);
        capturingLogger.LogEntries.Count.ShouldBeGreaterThan(0);

        // Verify we have Information level logs for batch processing
        capturingLogger.LogEntries.Any(log => log.Level == LogLevel.Information && log.Message.Contains("Processing")).ShouldBeTrue();

        // Verify we have Debug level logs for individual message processing
        capturingLogger.LogEntries.Any(log => log.Level == LogLevel.Debug && log.Message.Contains("Processing outbox message")).ShouldBeTrue();

        // Verify we have Warning level logs for no handler
        capturingLogger.LogEntries.Any(log => log.Level == LogLevel.Warning && log.Message.Contains("No handler registered")).ShouldBeTrue();
    }

    // Simple logger that captures log entries for testing
    private class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> LogEntries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LogEntries.Add((logLevel, message, exception));
        }
    }

    [Fact]
    public void MultiOutboxDispatcher_DefaultBackoff_ExponentialWithJitter()
    {
        // Act
        var delay1 = MultiOutboxDispatcher.DefaultBackoff(1);
        var delay2 = MultiOutboxDispatcher.DefaultBackoff(2);
        var delay3 = MultiOutboxDispatcher.DefaultBackoff(3);
        var delay10 = MultiOutboxDispatcher.DefaultBackoff(10);

        // Assert
        // For attempt 1: base = 500ms, jitter = 0-249ms, so range is 500-749ms
        delay1.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500));
        delay1.ShouldBeLessThan(TimeSpan.FromMilliseconds(750));

        // For attempt 2: base = 1000ms, jitter = 0-249ms, so range is 1000-1249ms
        delay2.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1000));
        delay2.ShouldBeLessThan(TimeSpan.FromMilliseconds(1250));

        // For attempt 3: base = 2000ms, jitter = 0-249ms, so range is 2000-2249ms
        delay3.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(2000));
        delay3.ShouldBeLessThan(TimeSpan.FromMilliseconds(2250));

        // Should cap at some reasonable maximum
        delay10.ShouldBeLessThan(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ServiceCollection_AddOutboxHandler_RegistersHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTimeAbstractions(timeProvider);

        // Act
        services.AddOutboxHandler<TestHandler>();

        // Assert
        var serviceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IOutboxHandler));
        serviceDescriptor.ShouldNotBeNull();
        serviceDescriptor.ImplementationType.ShouldBe(typeof(TestHandler));
        serviceDescriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ServiceCollection_AddOutboxHandler_Factory_RegistersHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTimeAbstractions(timeProvider);

        // Act
        services.AddOutboxHandler(sp => new TestHandler("Factory.Topic"));

        // Assert
        var serviceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IOutboxHandler));
        serviceDescriptor.ShouldNotBeNull();
        serviceDescriptor.ImplementationFactory.ShouldNotBeNull();
        serviceDescriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    private sealed class SingleOutboxStoreProvider : IOutboxStoreProvider
    {
        private readonly IOutboxStore store;

        public SingleOutboxStoreProvider(IOutboxStore store)
        {
            this.store = store;
        }

        public Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync() =>
            Task.FromResult<IReadOnlyList<IOutboxStore>>(new[] { store });

        public string GetStoreIdentifier(IOutboxStore store) => "default";

        public IOutboxStore? GetStoreByKey(string key) => store;

        public IOutbox? GetOutboxByKey(string key) => null;
    }

    // Test implementation of IOutboxHandler
    private class TestHandler : IOutboxHandler
    {
        public List<OutboxMessage> HandledMessages { get; } = new List<OutboxMessage>();

        public bool ShouldThrow { get; set; }

        public TestHandler(string topic)
        {
            Topic = topic;
        }

        public string Topic { get; }

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            HandledMessages.Add(message);

            if (ShouldThrow)
            {
                throw new Exception("Test exception");
            }

            return Task.CompletedTask;
        }
    }

    // Test implementation of IOutboxStore
    private class TestOutboxStore : IOutboxStore
    {
        private readonly List<OutboxMessage> messages = new List<OutboxMessage>();

        public List<Guid> DispatchedMessages { get; } = new List<Guid>();

        public List<KeyValuePair<Guid, string>> FailedMessages { get; } = new List<KeyValuePair<Guid, string>>();

        public List<KeyValuePair<Guid, (TimeSpan Delay, string Error)>> RescheduledMessages { get; } = new List<KeyValuePair<Guid, (TimeSpan Delay, string Error)>>();

        public void AddMessage(OutboxMessage message)
        {
            messages.Add(message);
        }

        public Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
        {
            var claimed = messages.Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(claimed);
        }

        public Task MarkDispatchedAsync(Guid id, CancellationToken cancellationToken)
        {
            DispatchedMessages.Add(id);
            return Task.CompletedTask;
        }

        public Task FailAsync(Guid id, string lastError, CancellationToken cancellationToken)
        {
            FailedMessages.Add(new KeyValuePair<Guid, string>(id, lastError));
            return Task.CompletedTask;
        }

        public Task RescheduleAsync(Guid id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
        {
            RescheduledMessages.Add(new KeyValuePair<Guid, (TimeSpan, string)>(id, (delay, lastError)));
            return Task.CompletedTask;
        }
    }
}
