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
using Bravellian.Platform.Correlation;
using Bravellian.Platform.Email;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;

/// <summary>
/// PostgreSQL implementation of <see cref="IEmailDeliverySink"/>.
/// </summary>
public sealed class PostgresEmailDeliverySink : IEmailDeliverySink
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly PostgresEmailDeliveryOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ICorrelationContextAccessor? correlationAccessor;
    private readonly ILogger<PostgresEmailDeliverySink> logger;
    private readonly string qualifiedTable;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresEmailDeliverySink"/> class.
    /// </summary>
    /// <param name="options">Postgres email delivery options.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="correlationAccessor">Correlation context accessor.</param>
    /// <param name="logger">Logger instance.</param>
    public PostgresEmailDeliverySink(
        IOptions<PostgresEmailDeliveryOptions> options,
        TimeProvider timeProvider,
        ICorrelationContextAccessor? correlationAccessor = null,
        ILogger<PostgresEmailDeliverySink> logger)
    {
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.correlationAccessor = correlationAccessor;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        qualifiedTable = PostgresSqlHelper.Qualify(this.options.SchemaName, this.options.TableName);
    }

    /// <inheritdoc />
    public Task RecordQueuedAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return RecordAsync(
            EmailDeliveryEventType.Queued,
            EmailDeliveryStatus.Queued,
            timeProvider.GetUtcNow(),
            message.MessageKey,
            providerMessageId: null,
            providerEventId: null,
            attemptNumber: null,
            errorCode: null,
            errorMessage: null,
            message,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordAttemptAsync(
        OutboundEmailMessage message,
        EmailDeliveryAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(attempt);

        return RecordAsync(
            EmailDeliveryEventType.Attempt,
            attempt.Status,
            attempt.TimestampUtc,
            message.MessageKey,
            attempt.ProviderMessageId,
            providerEventId: null,
            attempt.AttemptNumber,
            attempt.ErrorCode,
            attempt.ErrorMessage,
            message,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordFinalAsync(
        OutboundEmailMessage message,
        EmailDeliveryStatus status,
        string? providerMessageId,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return RecordAsync(
            EmailDeliveryEventType.Final,
            status,
            timeProvider.GetUtcNow(),
            message.MessageKey,
            providerMessageId,
            providerEventId: null,
            attemptNumber: null,
            errorCode,
            errorMessage,
            message,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordExternalAsync(EmailDeliveryUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        return RecordAsync(
            EmailDeliveryEventType.External,
            update.Status,
            timeProvider.GetUtcNow(),
            update.MessageKey,
            update.ProviderMessageId,
            update.ProviderEventId,
            attemptNumber: null,
            update.ErrorCode,
            update.ErrorMessage,
            message: null,
            cancellationToken);
    }

    private async Task RecordAsync(
        EmailDeliveryEventType eventType,
        EmailDeliveryStatus status,
        DateTimeOffset occurredAtUtc,
        string? messageKey,
        string? providerMessageId,
        string? providerEventId,
        int? attemptNumber,
        string? errorCode,
        string? errorMessage,
        OutboundEmailMessage? message,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            INSERT INTO {qualifiedTable} (
                "EmailDeliveryEventId",
                "EventType",
                "Status",
                "OccurredAtUtc",
                "MessageKey",
                "ProviderMessageId",
                "ProviderEventId",
                "AttemptNumber",
                "ErrorCode",
                "ErrorMessage",
                "MessagePayload",
                "CorrelationId",
                "CausationId",
                "TraceId",
                "SpanId",
                "CorrelationCreatedAtUtc",
                "CorrelationTagsJson"
            )
            VALUES
            (
                @EmailDeliveryEventId,
                @EventType,
                @Status,
                @OccurredAtUtc,
                @MessageKey,
                @ProviderMessageId,
                @ProviderEventId,
                @AttemptNumber,
                @ErrorCode,
                @ErrorMessage,
                @MessagePayload,
                @CorrelationId,
                @CausationId,
                @TraceId,
                @SpanId,
                @CorrelationCreatedAtUtc,
                @CorrelationTagsJson
            )
            """;

        var correlation = correlationAccessor?.Current;
        var payload = message == null ? null : JsonSerializer.Serialize(message, SerializerOptions);

        using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(
                sql,
                new
                {
                    EmailDeliveryEventId = Guid.NewGuid(),
                    EventType = (short)eventType,
                    Status = (short)status,
                    OccurredAtUtc = occurredAtUtc,
                    MessageKey = messageKey,
                    ProviderMessageId = providerMessageId,
                    ProviderEventId = providerEventId,
                    AttemptNumber = attemptNumber,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage,
                    MessagePayload = payload,
                    CorrelationId = correlation?.CorrelationId.Value,
                    CausationId = correlation?.CausationId?.Value,
                    TraceId = correlation?.TraceId,
                    SpanId = correlation?.SpanId,
                    CorrelationCreatedAtUtc = correlation?.CreatedAtUtc,
                    CorrelationTagsJson = SerializeTags(correlation?.Tags),
                }).ConfigureAwait(false);

            EmailMetrics.RecordDeliveryEvent(eventType.ToString(), status, provider: null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record email delivery event {EventType} for message {MessageKey}.", eventType, messageKey);
            throw;
        }
    }

    private static string? SerializeTags(IReadOnlyDictionary<string, string>? tags)
    {
        return tags is null ? null : JsonSerializer.Serialize(tags, SerializerOptions);
    }
}
