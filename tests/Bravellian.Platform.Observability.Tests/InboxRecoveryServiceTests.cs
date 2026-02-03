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

using System.Text.Json;
using Bravellian.Platform.Audit;
using Bravellian.Platform.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Bravellian.Platform.Tests;

public sealed class InboxRecoveryServiceTests
{
    /// <summary>When revive Async Emits Audit Event, then it behaves as expected.</summary>
    /// <intent>Verify inbox revive emits audit data with the prior error.</intent>
    /// <scenario>Given a work store returning a message with LastError.</scenario>
    /// <behavior>Then the audit event includes the previous error and revive reason.</behavior>
    [Fact]
    public async Task ReviveAsync_EmitsAuditEventWithPriorError()
    {
        var store = new FakeInboxWorkStore();
        var emitter = new RecordingPlatformEventEmitter();
        var service = new InboxRecoveryService(store, NullLogger<InboxRecoveryService>.Instance, emitter);

        await service.ReviveAsync(
            new[] { "msg-1" },
            reason: "manual retry",
            delay: TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);

        emitter.AuditEvents.Count.ShouldBe(1);
        var auditEvent = emitter.AuditEvents[0];
        auditEvent.Name.ShouldBe(PlatformEventNames.InboxMessageRevived);

        using var doc = JsonDocument.Parse(auditEvent.DataJson ?? "{}");
        var root = doc.RootElement;
        root.GetProperty("lastError").GetString().ShouldBe("boom");
        root.GetProperty("reviveReason").GetString().ShouldBe("manual retry");
        root.GetProperty("reviveDelayMs").GetDouble().ShouldBe(5000);
    }

    private sealed class FakeInboxWorkStore : IInboxWorkStore
    {
        private readonly Dictionary<string, InboxMessage> messages = new(StringComparer.Ordinal)
        {
            ["msg-1"] = CreateMessage(
                messageId: "msg-1",
                source: "webhooks",
                topic: "test.topic",
                payload: "{}",
                attempt: 2,
                lastError: "boom"),
        };

        public Task<IReadOnlyList<string>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task AckAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task AbandonAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, string? lastError = null, TimeSpan? delay = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task FailAsync(OwnerToken ownerToken, IEnumerable<string> messageIds, string errorMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ReviveAsync(IEnumerable<string> messageIds, string? reason = null, TimeSpan? delay = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReapExpiredAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken)
        {
            return Task.FromResult(messages[messageId]);
        }

        private static InboxMessage CreateMessage(
            string messageId,
            string source,
            string topic,
            string payload,
            int attempt,
            string lastError)
        {
            var message = new InboxMessage();
            SetProperty(message, nameof(InboxMessage.MessageId), messageId);
            SetProperty(message, nameof(InboxMessage.Source), source);
            SetProperty(message, nameof(InboxMessage.Topic), topic);
            SetProperty(message, nameof(InboxMessage.Payload), payload);
            SetProperty(message, nameof(InboxMessage.Attempt), attempt);
            SetProperty(message, nameof(InboxMessage.LastError), lastError);
            return message;
        }

        private static void SetProperty<T>(InboxMessage instance, string name, T value)
        {
            var prop = typeof(InboxMessage).GetProperty(name);
            if (prop == null)
            {
                throw new InvalidOperationException($"Property '{name}' not found.");
            }

            prop.SetValue(instance, value);
        }
    }

    private sealed class RecordingPlatformEventEmitter : IPlatformEventEmitter
    {
        public List<AuditEvent> AuditEvents { get; } = new();

        public Task<Operations.OperationId> EmitOperationStartedAsync(
            string name,
            Correlation.CorrelationContext? correlationContext,
            Operations.OperationId? parentOperationId,
            IReadOnlyDictionary<string, string>? tags,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Operations.OperationId.NewId());
        }

        public Task EmitOperationCompletedAsync(
            Operations.OperationId operationId,
            Operations.OperationStatus status,
            string? message,
            Correlation.CorrelationContext? correlationContext,
            IReadOnlyDictionary<string, string>? tags,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task EmitAuditEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            AuditEvents.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
