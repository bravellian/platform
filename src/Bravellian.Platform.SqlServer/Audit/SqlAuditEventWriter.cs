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
using Bravellian.Platform.Correlation;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// SQL Server implementation of <see cref="IAuditEventWriter"/>.
/// </summary>
public sealed class SqlAuditEventWriter : IAuditEventWriter
{
    private readonly SqlAuditOptions options;
    private readonly ILogger<SqlAuditEventWriter> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlAuditEventWriter"/> class.
    /// </summary>
    /// <param name="options">SQL Server options.</param>
    /// <param name="logger">Logger instance.</param>
    public SqlAuditEventWriter(IOptions<SqlAuditOptions> options, ILogger<SqlAuditEventWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var validation = AuditEventValidator.Validate(auditEvent, options.ValidationOptions);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors), nameof(auditEvent));
        }

        var sql = $"""
            INSERT INTO [{options.SchemaName}].[{options.AuditEventsTable}] (
                AuditEventId,
                OccurredAtUtc,
                Name,
                DisplayMessage,
                Outcome,
                DataJson,
                ActorType,
                ActorId,
                ActorDisplay,
                CorrelationId,
                CausationId,
                TraceId,
                SpanId,
                CorrelationCreatedAtUtc,
                CorrelationTagsJson
            )
            VALUES
            (
                @AuditEventId,
                @OccurredAtUtc,
                @Name,
                @DisplayMessage,
                @Outcome,
                @DataJson,
                @ActorType,
                @ActorId,
                @ActorDisplay,
                @CorrelationId,
                @CausationId,
                @TraceId,
                @SpanId,
                @CorrelationCreatedAtUtc,
                @CorrelationTagsJson
            )
            """;

        var anchorSql = $"""
            INSERT INTO [{options.SchemaName}].[{options.AuditAnchorsTable}]
            (
                AuditEventId,
                AnchorType,
                AnchorId,
                Role
            )
            VALUES
            (
                @AuditEventId,
                @AnchorType,
                @AnchorId,
                @Role
            )
            """;

        var connection = new SqlConnection(options.ConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {

                try
                {
                    await connection.ExecuteAsync(
                        sql,
                        new
                        {
                            AuditEventId = auditEvent.EventId.Value,
                            auditEvent.OccurredAtUtc,
                            auditEvent.Name,
                            auditEvent.DisplayMessage,
                            Outcome = (byte)auditEvent.Outcome,
                            auditEvent.DataJson,
                            ActorType = auditEvent.Actor?.ActorType,
                            ActorId = auditEvent.Actor?.ActorId,
                            ActorDisplay = auditEvent.Actor?.ActorDisplay,
                            CorrelationId = auditEvent.Correlation?.CorrelationId.Value,
                            CausationId = auditEvent.Correlation?.CausationId?.Value,
                            TraceId = auditEvent.Correlation?.TraceId,
                            SpanId = auditEvent.Correlation?.SpanId,
                            CorrelationCreatedAtUtc = auditEvent.Correlation?.CreatedAtUtc,
                            CorrelationTagsJson = SerializeTags(auditEvent.Correlation?.Tags),
                        },
                        transaction).ConfigureAwait(false);

                    var anchors = auditEvent.Anchors.Select(anchor => new
                    {
                        AuditEventId = auditEvent.EventId.Value,
                        anchor.AnchorType,
                        anchor.AnchorId,
                        anchor.Role,
                    });

                    await connection.ExecuteAsync(anchorSql, anchors, transaction).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    SqlAuditMetrics.RecordWritten(auditEvent.Outcome.ToString());
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to write audit event {EventId}.", auditEvent.EventId);
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    private static string? SerializeTags(IReadOnlyDictionary<string, string>? tags)
    {
        return tags is null ? null : JsonSerializer.Serialize(tags);
    }
}
