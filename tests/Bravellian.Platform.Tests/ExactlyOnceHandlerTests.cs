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

using Bravellian.Platform.ExactlyOnce;
using Bravellian.Platform.Outbox;
using Bravellian.Platform.Tests.TestUtilities;
using Shouldly;
using Xunit;

namespace Bravellian.Platform.Tests;

public sealed class ExactlyOnceHandlerTests
{
    /// <summary>When outbox Handler Suppressed Does Not Execute, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for outbox Handler Suppressed Does Not Execute.</intent>
    /// <scenario>Given outbox Handler Suppressed Does Not Execute.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task OutboxHandler_Suppressed_DoesNotExecute()
    {
        var store = new InMemoryIdempotencyStore();
        var messageId = OutboxMessageIdentifier.GenerateNew();
        await store.CompleteAsync(messageId.ToString(), CancellationToken.None);
        var handler = new TestOutboxHandler(store, ExactlyOnceExecutionResult.Success());

        await handler.HandleAsync(CreateOutboxMessage(messageId), CancellationToken.None);

        handler.Executed.ShouldBeFalse();
    }

    /// <summary>When outbox Handler Transient Failure Throws For Retry, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for outbox Handler Transient Failure Throws For Retry.</intent>
    /// <scenario>Given outbox Handler Transient Failure Throws For Retry.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task OutboxHandler_TransientFailure_ThrowsForRetry()
    {
        var store = new InMemoryIdempotencyStore();
        var handler = new TestOutboxHandler(store, ExactlyOnceExecutionResult.TransientFailure(errorMessage: "boom"));

        await Should.ThrowAsync<Exception>(() => handler.HandleAsync(CreateOutboxMessage(), CancellationToken.None));
    }

    /// <summary>When outbox Handler Permanent Failure Throws Permanent Exception, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for outbox Handler Permanent Failure Throws Permanent Exception.</intent>
    /// <scenario>Given outbox Handler Permanent Failure Throws Permanent Exception.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task OutboxHandler_PermanentFailure_ThrowsPermanentException()
    {
        var store = new InMemoryIdempotencyStore();
        var handler = new TestOutboxHandler(store, ExactlyOnceExecutionResult.PermanentFailure(errorMessage: "boom"));

        await Should.ThrowAsync<OutboxPermanentFailureException>(
            () => handler.HandleAsync(CreateOutboxMessage(), CancellationToken.None));
    }

    /// <summary>When inbox Handler Transient Failure Throws For Retry, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for inbox Handler Transient Failure Throws For Retry.</intent>
    /// <scenario>Given inbox Handler Transient Failure Throws For Retry.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task InboxHandler_TransientFailure_ThrowsForRetry()
    {
        var store = new InMemoryIdempotencyStore();
        var handler = new TestInboxHandler(store, ExactlyOnceExecutionResult.TransientFailure(errorMessage: "boom"));

        await Should.ThrowAsync<Exception>(() => handler.HandleAsync(CreateInboxMessage(), CancellationToken.None));
    }

    /// <summary>When inbox Handler Permanent Failure Throws Permanent Exception, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for inbox Handler Permanent Failure Throws Permanent Exception.</intent>
    /// <scenario>Given inbox Handler Permanent Failure Throws Permanent Exception.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task InboxHandler_PermanentFailure_ThrowsPermanentException()
    {
        var store = new InMemoryIdempotencyStore();
        var handler = new TestInboxHandler(store, ExactlyOnceExecutionResult.PermanentFailure(errorMessage: "boom"));

        await Should.ThrowAsync<InboxPermanentFailureException>(
            () => handler.HandleAsync(CreateInboxMessage(), CancellationToken.None));
    }

    /// <summary>When inbox Handler Suppressed Does Not Execute, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for inbox Handler Suppressed Does Not Execute.</intent>
    /// <scenario>Given inbox Handler Suppressed Does Not Execute.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task InboxHandler_Suppressed_DoesNotExecute()
    {
        var store = new InMemoryIdempotencyStore();
        var message = CreateInboxMessage("source", "message-1");
        await store.CompleteAsync("source:message-1", CancellationToken.None);
        var handler = new TestInboxHandler(store, ExactlyOnceExecutionResult.Success());

        await handler.HandleAsync(message, CancellationToken.None);

        handler.Executed.ShouldBeFalse();
    }

    private static OutboxMessage CreateOutboxMessage(OutboxMessageIdentifier? messageId = null)
    {
        return new OutboxMessage
        {
            MessageId = messageId ?? OutboxMessageIdentifier.GenerateNew(),
            Payload = "{}",
            Topic = "exactly-once"
        };
    }

    private static InboxMessage CreateInboxMessage(string source = "source", string messageId = "message-1")
    {
        return new InboxMessage
        {
            Source = source,
            MessageId = InboxMessageIdentifier.From(messageId),
            Topic = "exactly-once",
            Payload = "{}"
        };
    }

    private sealed class TestOutboxHandler : ExactlyOnceOutboxHandler
    {
        private readonly ExactlyOnceExecutionResult result;

        public TestOutboxHandler(InMemoryIdempotencyStore store, ExactlyOnceExecutionResult result)
            : base(store)
        {
            this.result = result;
        }

        public bool Executed { get; private set; }

        public override string Topic => "exactly-once";

        protected override Task<ExactlyOnceExecutionResult> HandleExactlyOnceAsync(
            OutboxMessage message,
            CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(result);
        }
    }

    private sealed class TestInboxHandler : ExactlyOnceInboxHandler
    {
        private readonly ExactlyOnceExecutionResult result;

        public TestInboxHandler(InMemoryIdempotencyStore store, ExactlyOnceExecutionResult result)
            : base(store)
        {
            this.result = result;
        }

        public bool Executed { get; private set; }

        public override string Topic => "exactly-once";

        protected override Task<ExactlyOnceExecutionResult> HandleExactlyOnceAsync(
            InboxMessage message,
            CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(result);
        }
    }
}
