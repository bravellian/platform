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


using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Bravellian.Platform.Audit;
using Bravellian.Platform.Observability;
using Bravellian.Platform.Outbox;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

#pragma warning disable CA2100 // SQL command text uses validated schema/table names with parameters.

internal class SqlOutboxService : IOutbox
{
    private readonly SqlOutboxOptions options;
    private readonly string connectionString;
    private readonly string enqueueSql;
    private readonly ILogger<SqlOutboxService> logger;
    private readonly IOutboxJoinStore? joinStore;
    private readonly IPlatformEventEmitter? eventEmitter;

    public SqlOutboxService(
        IOptions<SqlOutboxOptions> options,
        ILogger<SqlOutboxService> logger,
        IOutboxJoinStore? joinStore = null,
        IPlatformEventEmitter? eventEmitter = null)
    {
        this.options = options.Value;
        connectionString = this.options.ConnectionString;
        this.logger = logger;
        this.joinStore = joinStore;
        this.eventEmitter = eventEmitter;

        // Build the SQL query using configured schema and table names
        enqueueSql = $"""

                        INSERT INTO [{this.options.SchemaName}].[{this.options.TableName}] (Topic, Payload, CorrelationId, MessageId, DueTimeUtc, DueOn)
                        VALUES (@Topic, @Payload, @CorrelationId, NEWID(), @DueTimeUtc, @DueTimeUtc);
            """;
    }


    public async Task EnqueueAsync(
        string topic,
        string payload,
        CancellationToken cancellationToken)
    {
        await EnqueueAsync(topic, payload, (string?)null, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        await EnqueueAsync(topic, payload, correlationId, (DateTimeOffset?)null, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        string? correlationId,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken)
    {
        // Ensure outbox table exists before attempting to enqueue (if enabled)
        if (options.EnableSchemaDeployment)
        {
            await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
                connectionString,
                options.SchemaName,
                options.TableName).ConfigureAwait(false);
        }

        // Create our own connection and transaction for reliability
        var connection = new SqlConnection(connectionString);

        // Create our own connection and transaction for reliability
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await connection.ExecuteAsync(enqueueSql, new
                    {
                        Topic = topic,
                        Payload = payload,
                        CorrelationId = correlationId,
                        DueTimeUtc = dueTimeUtc?.UtcDateTime,
                    }, transaction: transaction).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await EnqueueAsync(topic, payload, transaction, null, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        await EnqueueAsync(topic, payload, transaction, correlationId, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId,
        DateTimeOffset? dueTimeUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Connection is null)
        {
            throw new ArgumentException("Transaction must have a connection.", nameof(transaction));
        }

        // Note: We use the connection from the provided transaction.
        await transaction.Connection.ExecuteAsync(enqueueSql, new
        {
            Topic = topic,
            Payload = payload,
            CorrelationId = correlationId,
            DueTimeUtc = dueTimeUtc?.UtcDateTime,
        }, transaction: transaction).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.claim");
        var stopwatch = Stopwatch.StartNew();
        var result = new List<Guid>(batchSize);

        try
        {
            var connection = new SqlConnection(connectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var command = new SqlCommand($"[{options.SchemaName}].[Outbox_Claim]", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };

                await using (command.ConfigureAwait(false))
                {
                    command.Parameters.AddWithValue("@OwnerToken", ownerToken.Value);
                    command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
                    command.Parameters.AddWithValue("@BatchSize", batchSize);

                    using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        result.Add((Guid)reader.GetValue(0));
                    }

                    logger.LogDebug("Claimed {Count} outbox items with owner {OwnerToken}", result.Count, ownerToken);
                    SchedulerMetrics.OutboxItemsClaimed.Add(
                        result.Count,
                        new KeyValuePair<string, object?>("queue", options.TableName),
                        new KeyValuePair<string, object?>("store", options.SchemaName));
                    SchedulerMetrics.WorkQueueBatchSize.Record(
                        result.Count,
                        new KeyValuePair<string, object?>("queue", "outbox"),
                        new KeyValuePair<string, object?>("store", options.SchemaName));
                    return result.Select(id => OutboxWorkItemIdentifier.From(id)).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to claim outbox items with owner {OwnerToken}", ownerToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueClaimDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task AckAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.ack");
        var stopwatch = Stopwatch.StartNew();
        var idList = ids.ToList();

        if (idList.Count == 0)
        {
            return;
        }

        try
        {
            await ExecuteWithIdsAsync($"[{options.SchemaName}].[Outbox_Ack]", ownerToken, idList, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Acknowledged {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            SchedulerMetrics.OutboxItemsAcknowledged.Add(
                idList.Count,
                new KeyValuePair<string, object?>("queue", options.TableName),
                new KeyValuePair<string, object?>("store", options.SchemaName));
            await EmitOutboxProcessedAsync(idList.Count, ownerToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to acknowledge {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueAckDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                idList.Count,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task AbandonAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.abandon");
        var stopwatch = Stopwatch.StartNew();
        var idList = ids.ToList();

        if (idList.Count == 0)
        {
            return;
        }

        try
        {
            await ExecuteWithIdsAsync($"[{options.SchemaName}].[Outbox_Abandon]", ownerToken, idList, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Abandoned {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            SchedulerMetrics.OutboxItemsAbandoned.Add(
                idList.Count,
                new KeyValuePair<string, object?>("queue", options.TableName),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to abandon {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueAbandonDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                idList.Count,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task FailAsync(
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.fail");
        var stopwatch = Stopwatch.StartNew();
        var idList = ids.ToList();

        if (idList.Count == 0)
        {
            return;
        }

        try
        {
            await ExecuteWithIdsAsync($"[{options.SchemaName}].[Outbox_Fail]", ownerToken, idList, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Failed {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            SchedulerMetrics.OutboxItemsFailed.Add(
                idList.Count,
                new KeyValuePair<string, object?>("queue", options.TableName),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark {Count} outbox items as failed with owner {OwnerToken}", idList.Count, ownerToken);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueFailDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
            SchedulerMetrics.WorkQueueBatchSize.Record(
                idList.Count,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    public async Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.reap_expired");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var connection = new SqlConnection(connectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var command = new SqlCommand($"[{options.SchemaName}].[Outbox_ReapExpired]", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };
                await using (command.ConfigureAwait(false))
                {
                    var reapedCount = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    var count = Convert.ToInt32(reapedCount ?? 0, CultureInfo.InvariantCulture);

                    logger.LogDebug("Reaped {Count} expired outbox items", count);
                    SchedulerMetrics.OutboxItemsReaped.Add(
                        count,
                        new KeyValuePair<string, object?>("queue", options.TableName),
                        new KeyValuePair<string, object?>("store", options.SchemaName));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reap expired outbox items");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            SchedulerMetrics.WorkQueueReapDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("queue", "outbox"),
                new KeyValuePair<string, object?>("store", options.SchemaName));
        }
    }

    private async Task ExecuteWithIdsAsync(
        string procedure,
        Bravellian.Platform.OwnerToken ownerToken,
        IEnumerable<OutboxWorkItemIdentifier> ids,
        CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return; // Nothing to do
        }

        var tvp = new DataTable();
        tvp.Columns.Add("Id", typeof(Guid));
        foreach (var id in idList)
        {
            tvp.Rows.Add(id.Value);
        }

        var connection = new SqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(procedure, connection)
            {
                CommandType = CommandType.StoredProcedure,
            };

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("@OwnerToken", ownerToken.Value);
                var parameter = command.Parameters.AddWithValue("@Ids", tvp);
                parameter.SqlDbType = SqlDbType.Structured;
                parameter.TypeName = $"[{options.SchemaName}].[GuidIdList]";

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task EmitOutboxProcessedAsync(int count, Bravellian.Platform.OwnerToken ownerToken, CancellationToken cancellationToken)
    {
        if (eventEmitter is null)
        {
            return Task.CompletedTask;
        }

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["count"] = count,
            ["schema"] = options.SchemaName,
            ["table"] = options.TableName,
            ["ownerToken"] = ownerToken.Value,
        };

        var auditEvent = new AuditEvent(
            AuditEventId.NewId(),
            DateTimeOffset.UtcNow,
            PlatformEventNames.OutboxMessageProcessed,
            $"Outbox processed {count} item(s)",
            EventOutcome.Success,
            new[] { new EventAnchor("Outbox", options.TableName, "Subject") },
            JsonSerializer.Serialize(data));

        return eventEmitter.EmitAuditEventAsync(auditEvent, cancellationToken);
    }

    public async Task<JoinIdentifier> StartJoinAsync(
        long tenantId,
        int expectedSteps,
        string? metadata,
        CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException(
                "Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        // Ensure join schema exists before attempting to create join (if enabled)
        if (options.EnableSchemaDeployment)
        {
            await DatabaseSchemaManager.EnsureOutboxJoinSchemaAsync(
                connectionString,
                options.SchemaName).ConfigureAwait(false);
        }

        var join = await joinStore.CreateJoinAsync(
            tenantId,
            expectedSteps,
            metadata,
            cancellationToken).ConfigureAwait(false);

        return join.JoinId;
    }

    public async Task AttachMessageToJoinAsync(
        Bravellian.Platform.Outbox.JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException(
                "Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        await joinStore.AttachMessageToJoinAsync(
            joinId,
            outboxMessageId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportStepCompletedAsync(
        Bravellian.Platform.Outbox.JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException(
                "Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        await joinStore.IncrementCompletedAsync(
            joinId,
            outboxMessageId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ReportStepFailedAsync(
        Bravellian.Platform.Outbox.JoinIdentifier joinId,
        OutboxMessageIdentifier outboxMessageId,
        CancellationToken cancellationToken)
    {
        if (joinStore == null)
        {
            throw new InvalidOperationException(
                "Join functionality is not available. Ensure IOutboxJoinStore is registered in the service collection.");
        }

        await joinStore.IncrementFailedAsync(
            joinId,
            outboxMessageId,
            cancellationToken).ConfigureAwait(false);
    }
}
