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
using System.Text.Json.Serialization;
using Bravellian.Platform.Observability;

namespace Bravellian.Platform.Email;

/// <summary>
/// Default implementation of <see cref="IEmailOutbox"/>.
/// </summary>
public sealed class EmailOutbox : IEmailOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IOutbox outbox;
    private readonly EmailMessageValidator validator;
    private readonly IEmailDeliverySink deliverySink;
    private readonly IPlatformEventEmitter? eventEmitter;
    private readonly EmailOutboxOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailOutbox"/> class.
    /// </summary>
    /// <param name="outbox">Outbox instance.</param>
    /// <param name="deliverySink">Delivery sink.</param>
    /// <param name="validator">Message validator.</param>
    /// <param name="options">Outbox options.</param>
    public EmailOutbox(
        IOutbox outbox,
        IEmailDeliverySink deliverySink,
        EmailMessageValidator? validator = null,
        EmailOutboxOptions? options = null)
    {
        this.outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        this.deliverySink = deliverySink ?? throw new ArgumentNullException(nameof(deliverySink));
        this.validator = validator ?? new EmailMessageValidator();
        this.options = options ?? new EmailOutboxOptions();
        eventEmitter = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailOutbox"/> class with observability support.
    /// </summary>
    /// <param name="outbox">Outbox instance.</param>
    /// <param name="deliverySink">Delivery sink.</param>
    /// <param name="eventEmitter">Optional platform event emitter.</param>
    /// <param name="validator">Message validator.</param>
    /// <param name="options">Outbox options.</param>
    public EmailOutbox(
        IOutbox outbox,
        IEmailDeliverySink deliverySink,
        IPlatformEventEmitter? eventEmitter,
        EmailMessageValidator? validator = null,
        EmailOutboxOptions? options = null)
        : this(outbox, deliverySink, validator, options)
    {
        this.eventEmitter = eventEmitter;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var validation = validator.Validate(message);
        if (!validation.Succeeded)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors), nameof(message));
        }

        var payload = JsonSerializer.Serialize(message, SerializerOptions);
        await outbox.EnqueueAsync(
            options.Topic,
            payload,
            message.MessageKey,
            message.RequestedSendAtUtc,
            cancellationToken).ConfigureAwait(false);

        await deliverySink.RecordQueuedAsync(message, cancellationToken).ConfigureAwait(false);

        EmailMetrics.RecordQueued(message, provider: null);
        await EmailAuditEvents.EmitQueuedAsync(eventEmitter, message, provider: null, cancellationToken).ConfigureAwait(false);
    }
}
