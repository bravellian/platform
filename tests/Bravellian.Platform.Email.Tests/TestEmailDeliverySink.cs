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

namespace Bravellian.Platform.Email.Tests;

internal sealed class TestEmailDeliverySink : IEmailDeliverySink
{
    public List<OutboundEmailMessage> Queued { get; } = new();
    public List<EmailDeliveryAttempt> Attempts { get; } = new();
    public List<EmailDeliveryUpdate> ExternalUpdates { get; } = new();
    public List<(EmailDeliveryStatus Status, string? ErrorMessage)> Final { get; } = new();

    public Task RecordQueuedAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
    {
        Queued.Add(message);
        return Task.CompletedTask;
    }

    public Task RecordAttemptAsync(OutboundEmailMessage message, EmailDeliveryAttempt attempt, CancellationToken cancellationToken)
    {
        Attempts.Add(attempt);
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
        Final.Add((status, errorMessage));
        return Task.CompletedTask;
    }
    public Task RecordExternalAsync(EmailDeliveryUpdate update, CancellationToken cancellationToken)
    {
        ExternalUpdates.Add(update);
        return Task.CompletedTask;
    }
}

