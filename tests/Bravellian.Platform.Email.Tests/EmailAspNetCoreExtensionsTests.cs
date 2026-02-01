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

using System.Data;
using Bravellian.Platform;
using Bravellian.Platform.Email.AspNetCore;
using Bravellian.Platform.Idempotency;
using Bravellian.Platform.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Bravellian.Platform.Email.Tests;

public sealed class EmailAspNetCoreExtensionsTests
{
    /// <summary>When add Bravellian Email Core Registers Components, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for add Bravellian Email Core Registers Components.</intent>
    /// <scenario>Given add Bravellian Email Core Registers Components.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void AddBravellianEmailCoreRegistersComponents()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutbox, FakeOutbox>();
        services.AddSingleton<IOutboxStore, FakeOutboxStore>();
        services.AddSingleton<IOutboundEmailSender, FakeEmailSender>();
        services.AddSingleton<IIdempotencyStore, FakeIdempotencyStore>();
        services.AddSingleton<IEmailDeliverySink, FakeDeliverySink>();

        services.AddBravellianEmailCore();

        var provider = services.BuildServiceProvider();
        provider.GetService<IEmailOutbox>().ShouldNotBeNull();
        provider.GetService<IEmailOutboxProcessor>().ShouldNotBeNull();
    }

    /// <summary>When hosted Service Invokes Processor Async, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for hosted Service Invokes Processor Async.</intent>
    /// <scenario>Given hosted Service Invokes Processor Async.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task HostedServiceInvokesProcessorAsync()
    {
        var processor = new RecordingProcessor();
        var options = Options.Create(new EmailProcessingOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
        });
        var service = new EmailProcessingHostedService(processor, options, NullLogger<EmailProcessingHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        var completed = await processor.FirstCall.Task.WaitAsync(TimeSpan.FromSeconds(1), Xunit.TestContext.Current.CancellationToken);
        completed.ShouldBeTrue();

        await service.StopAsync(CancellationToken.None);
        processor.CallCount.ShouldBeGreaterThan(0);
    }

    private sealed class RecordingProcessor : IEmailOutboxProcessor
    {
        private int callCount;
        private int signaled;

        public TaskCompletionSource<bool> FirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => callCount;

        public Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            if (Interlocked.Exchange(ref signaled, 1) == 0)
            {
                FirstCall.TrySetResult(true);
            }

            return Task.FromResult(1);
        }
    }

    private sealed class FakeOutbox : IOutbox
    {
        public Task EnqueueAsync(string topic, string payload, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string topic, string payload, string? correlationId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string topic, string payload, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OutboxWorkItemIdentifier>>(Array.Empty<OutboxWorkItemIdentifier>());
        }

        public Task AckAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task AbandonAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task FailAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ReapExpiredAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<JoinIdentifier> StartJoinAsync(long tenantId, int expectedSteps, string? metadata, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task AttachMessageToJoinAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ReportStepCompletedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ReportStepFailedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeOutboxStore : IOutboxStore
    {
        public Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
        }

        public Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmailSender : IOutboundEmailSender
    {
        public Task<EmailSendResult> SendAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
        {
            return Task.FromResult(EmailSendResult.Success());
        }
    }

    private sealed class FakeIdempotencyStore : IIdempotencyStore
    {
        public Task<bool> TryBeginAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task CompleteAsync(string key, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task FailAsync(string key, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeliverySink : IEmailDeliverySink
    {
        public Task RecordQueuedAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RecordAttemptAsync(OutboundEmailMessage message, EmailDeliveryAttempt attempt, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RecordFinalAsync(
            OutboundEmailMessage message,
            EmailDeliveryStatus status,
            string? providerMessageId,
            string? errorCode,
            string? errorMessage,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task RecordExternalAsync(EmailDeliveryUpdate update, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

