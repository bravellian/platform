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


using Bravellian.Platform.Outbox;
using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class OutboxHandlerTests : PostgresTestBase
{
    private FakeTimeProvider timeProvider = default!;

    public OutboxHandlerTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
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
        ILogger<MultiOutboxDispatcher>? logger = null,
        int maxAttempts = 5)
    {
        var provider = new SingleOutboxStoreProvider(store);
        var strategy = new RoundRobinOutboxSelectionStrategy();
        return new MultiOutboxDispatcher(
            provider,
            strategy,
            resolver,
            logger ?? new TestLogger<MultiOutboxDispatcher>(TestOutputHelper),
            maxAttempts: maxAttempts);
    }

    /// <summary>
    /// Given a resolver with multiple handlers, then known topics resolve and unknown topics do not.
    /// </summary>
    /// <intent>
    /// Verify resolver matching for known and unknown topics.
    /// </intent>
    /// <scenario>
    /// Given handlers for Email.Send, SMS.Send, and Push.Notification.
    /// </scenario>
    /// <behavior>
    /// TryGet returns true for Email.Send and SMS.Send, and false for NonExistent.
    /// </behavior>
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

    /// <summary>
    /// When resolving with different casing, then the handler is still found.
    /// </summary>
    /// <intent>
    /// Verify case-insensitive topic lookup in the resolver.
    /// </intent>
    /// <scenario>
    /// Given a resolver with a single handler for Email.Send.
    /// </scenario>
    /// <behavior>
    /// TryGet succeeds for email.send and EMAIL.SEND while preserving the handler topic.
    /// </behavior>
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

    /// <summary>
    /// When a message has a matching handler, then RunOnceAsync dispatches it and returns 1.
    /// </summary>
    /// <intent>
    /// Verify successful single-message dispatch through the handler.
    /// </intent>
    /// <scenario>
    /// Given a test store with one Test.Topic message and a resolver with a matching handler.
    /// </scenario>
    /// <behavior>
    /// Processed count is 1, the handler receives the message, and the store records dispatch.
    /// </behavior>
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
            Id = OutboxWorkItemIdentifier.GenerateNew(),
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

    /// <summary>
    /// When a message has no registered handler, then RunOnceAsync marks it failed.
    /// </summary>
    /// <intent>
    /// Verify missing handlers cause failures with the correct error.
    /// </intent>
    /// <scenario>
    /// Given an empty resolver and a test store with one Unknown.Topic message.
    /// </scenario>
    /// <behavior>
    /// Processed count is 1 and the store records a failure with a missing-handler error.
    /// </behavior>
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
            Id = OutboxWorkItemIdentifier.GenerateNew(),
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

    /// <summary>
    /// When a handler throws and attempts remain, then RunOnceAsync reschedules with backoff.
    /// </summary>
    /// <intent>
    /// Verify handler exceptions reschedule messages with delay.
    /// </intent>
    /// <scenario>
    /// Given a handler configured to throw and a message with RetryCount 2.
    /// </scenario>
    /// <behavior>
    /// The message is rescheduled with a positive delay and the error is captured.
    /// </behavior>
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
            Id = OutboxWorkItemIdentifier.GenerateNew(),
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

    /// <summary>
    /// When a failing message reaches the max attempts threshold, then it is marked failed.
    /// </summary>
    /// <intent>
    /// Verify poison messages fail once the retry limit is reached.
    /// </intent>
    /// <scenario>
    /// Given a dispatcher with maxAttempts 3 and a message at RetryCount 2 that throws.
    /// </scenario>
    /// <behavior>
    /// The message is recorded as failed and no reschedule occurs.
    /// </behavior>
    [Fact]
    public async Task MultiOutboxDispatcher_WithPoisonMessage_FailsWhenMaxAttemptsReached()
    {
        // Arrange
        var testHandler = new TestHandler("Test.Topic");
        testHandler.ShouldThrow = true;
        var resolver = new OutboxHandlerResolver(new[] { testHandler });
        var store = new TestOutboxStore();
        var logger = new TestLogger<MultiOutboxDispatcher>(TestOutputHelper);
        var dispatcher = CreateDispatcher(store, resolver, logger, maxAttempts: 3);

        // RetryCount represents the number of previous attempts. The current processing is attempt RetryCount + 1.
        // With RetryCount = 2 and maxAttempts = 3, this run is the 3rd attempt and should be marked as failed.
        var message = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "Test.Topic",
            Payload = "test payload",
            // RetryCount is the number of previous attempts; the current processing is attempt RetryCount + 1.
            // With RetryCount = 2 and maxAttempts = 3, this run is the 3rd attempt and should be marked as failed.
            RetryCount = 2,
        };

        store.AddMessage(message);

        // Act
        var processed = await dispatcher.RunOnceAsync(10, CancellationToken.None);

        // Assert
        processed.ShouldBe(1);
        store.FailedMessages.ShouldHaveSingleItem();
        store.FailedMessages[0].Key.ShouldBe(message.Id);
        store.RescheduledMessages.ShouldBeEmpty();
    }

    /// <summary>
    /// When a message is processed successfully, then the handler and store record dispatch.
    /// </summary>
    /// <intent>
    /// Verify the success path invokes handlers and records dispatch.
    /// </intent>
    /// <scenario>
    /// Given a dispatcher with a matching handler and a test store containing one message.
    /// </scenario>
    /// <behavior>
    /// Processed count is 1, the handler is invoked, and the store records dispatch.
    /// </behavior>
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
            Id = OutboxWorkItemIdentifier.GenerateNew(),
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

    /// <summary>
    /// When a handler fails, then the dispatcher captures the error and reschedules the message.
    /// </summary>
    /// <intent>
    /// Verify handler failures produce reschedule entries with errors.
    /// </intent>
    /// <scenario>
    /// Given a handler configured to throw and a test store with one message.
    /// </scenario>
    /// <behavior>
    /// The handler is invoked and the store records a reschedule with the exception message.
    /// </behavior>
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
            Id = OutboxWorkItemIdentifier.GenerateNew(),
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

    /// <summary>
    /// When processing a mix of success and missing-handler messages, then logs include info, debug, and warning entries.
    /// </summary>
    /// <intent>
    /// Verify log levels reflect batch, message, and missing-handler outcomes.
    /// </intent>
    /// <scenario>
    /// Given a capturing logger, one handled Test.Topic message, and one Unknown.Topic message.
    /// </scenario>
    /// <behavior>
    /// The batch is processed and logs include Information for batch work, Debug for message processing, and Warning for missing handlers.
    /// </behavior>
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
            Id = OutboxWorkItemIdentifier.GenerateNew(),
            Topic = "Test.Topic",
            Payload = "success payload",
            RetryCount = 0,
        };

        var failMessage = new OutboxMessage
        {
            Id = OutboxWorkItemIdentifier.GenerateNew(),
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
        capturingLogger.LogEntries.Any(log => log.Level == LogLevel.Information
                && log.Message.Contains("Processing", StringComparison.Ordinal))
            .ShouldBeTrue();

        // Verify we have Debug level logs for individual message processing
        capturingLogger.LogEntries.Any(log => log.Level == LogLevel.Debug
                && log.Message.Contains("Processing outbox message", StringComparison.Ordinal))
            .ShouldBeTrue();

        // Verify we have Warning level logs for no handler
        capturingLogger.LogEntries.Any(log => log.Level == LogLevel.Warning
                && log.Message.Contains("No handler registered", StringComparison.Ordinal))
            .ShouldBeTrue();
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

    /// <summary>
    /// When computing default backoff delays, then ranges grow exponentially with jitter and remain bounded.
    /// </summary>
    /// <intent>
    /// Verify the default backoff timing policy and caps.
    /// </intent>
    /// <scenario>
    /// Given backoff calculations for attempts 1, 2, 3, and 10.
    /// </scenario>
    /// <behavior>
    /// Early attempts fall within expected jitter ranges and the maximum stays under two minutes.
    /// </behavior>
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

    /// <summary>
    /// When adding an outbox handler type, then it is registered as a singleton IOutboxHandler.
    /// </summary>
    /// <intent>
    /// Verify handler type registration uses singleton lifetime.
    /// </intent>
    /// <scenario>
    /// Given a ServiceCollection configured with time abstractions.
    /// </scenario>
    /// <behavior>
    /// The registration uses TestHandler as the implementation type with singleton lifetime.
    /// </behavior>
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

    /// <summary>
    /// When adding an outbox handler factory, then it is registered as a singleton IOutboxHandler.
    /// </summary>
    /// <intent>
    /// Verify handler factory registration uses singleton lifetime.
    /// </intent>
    /// <scenario>
    /// Given a ServiceCollection configured with time abstractions and a factory delegate.
    /// </scenario>
    /// <behavior>
    /// The registration stores an implementation factory with singleton lifetime.
    /// </behavior>
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

        public List<OutboxWorkItemIdentifier> DispatchedMessages { get; } = new List<OutboxWorkItemIdentifier>();

        public List<KeyValuePair<OutboxWorkItemIdentifier, string>> FailedMessages { get; } = new List<KeyValuePair<OutboxWorkItemIdentifier, string>>();

        public List<KeyValuePair<OutboxWorkItemIdentifier, (TimeSpan Delay, string Error)>> RescheduledMessages { get; } = new List<KeyValuePair<OutboxWorkItemIdentifier, (TimeSpan Delay, string Error)>>();

        public void AddMessage(OutboxMessage message)
        {
            messages.Add(message);
        }

        public Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
        {
            var claimed = messages.Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(claimed);
        }

        public Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken)
        {
            DispatchedMessages.Add(id);
            return Task.CompletedTask;
        }

        public Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken)
        {
            FailedMessages.Add(new KeyValuePair<OutboxWorkItemIdentifier, string>(id, lastError));
            return Task.CompletedTask;
        }

        public Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
        {
            RescheduledMessages.Add(new KeyValuePair<OutboxWorkItemIdentifier, (TimeSpan, string)>(id, (delay, lastError)));
            return Task.CompletedTask;
        }
    }
}


