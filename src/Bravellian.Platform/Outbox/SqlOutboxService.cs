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
using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

internal class SqlOutboxService : IOutbox
{
    private readonly SqlOutboxOptions options;
    private readonly string connectionString;
    private readonly string enqueueSql;
    private readonly ILogger<SqlOutboxService> logger;
    private readonly IOutboxJoinStore? joinStore;

    public SqlOutboxService(
        IOptions<SqlOutboxOptions> options,
        ILogger<SqlOutboxService> logger,
        IOutboxJoinStore? joinStore = null)
    {
        this.options = options.Value;
        connectionString = this.options.ConnectionString;
        this.logger = logger;
        this.joinStore = joinStore;

        // Build the SQL query using configured schema and table names
        enqueueSql = $"""

                        INSERT INTO [{this.options.SchemaName}].[{this.options.TableName}] (Topic, Payload, CorrelationId, MessageId, DueTimeUtc)
                        VALUES (@Topic, @Payload, @CorrelationId, NEWID(), @DueTimeUtc);
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

            var transaction = connection.BeginTransaction();
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
        ArgumentNullException.ThrowIfNull(transaction.Connection);

        // Note: We use the connection from the provided transaction.
        await transaction.Connection.ExecuteAsync(enqueueSql, new
        {
            Topic = topic,
            Payload = payload,
            CorrelationId = correlationId,
            DueTimeUtc = dueTimeUtc?.UtcDateTime,
        }, transaction: transaction).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> ClaimAsync(
        Guid ownerToken,
        int leaseSeconds,
        int batchSize,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.claim");
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
                    command.Parameters.AddWithValue("@OwnerToken", ownerToken);
                    command.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);
                    command.Parameters.AddWithValue("@BatchSize", batchSize);

                    using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        result.Add((Guid)reader.GetValue(0));
                    }

                    logger.LogDebug("Claimed {Count} outbox items with owner {OwnerToken}", result.Count, ownerToken);
                    SchedulerMetrics.OutboxItemsClaimed.Add(result.Count);
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to claim outbox items with owner {OwnerToken}", ownerToken);
            throw;
        }
    }

    public async Task AckAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.ack");
        var idList = ids.ToList();

        if (idList.Count == 0)
        {
            return;
        }

        try
        {
            await ExecuteWithIdsAsync($"[{options.SchemaName}].[Outbox_Ack]", ownerToken, idList, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Acknowledged {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            SchedulerMetrics.OutboxItemsAcknowledged.Add(idList.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to acknowledge {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            throw;
        }
    }

    public async Task AbandonAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.abandon");
        var idList = ids.ToList();

        if (idList.Count == 0)
        {
            return;
        }

        try
        {
            await ExecuteWithIdsAsync($"[{options.SchemaName}].[Outbox_Abandon]", ownerToken, idList, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Abandoned {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            SchedulerMetrics.OutboxItemsAbandoned.Add(idList.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to abandon {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            throw;
        }
    }

    public async Task FailAsync(
        Guid ownerToken,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.fail");
        var idList = ids.ToList();

        if (idList.Count == 0)
        {
            return;
        }

        try
        {
            await ExecuteWithIdsAsync($"[{options.SchemaName}].[Outbox_Fail]", ownerToken, idList, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Failed {Count} outbox items with owner {OwnerToken}", idList.Count, ownerToken);
            SchedulerMetrics.OutboxItemsFailed.Add(idList.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark {Count} outbox items as failed with owner {OwnerToken}", idList.Count, ownerToken);
            throw;
        }
    }

    public async Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        using var activity = SchedulerMetrics.StartActivity("outbox.reap_expired");

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
                    SchedulerMetrics.OutboxItemsReaped.Add(count);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reap expired outbox items");
            throw;
        }
    }

    private async Task ExecuteWithIdsAsync(
        string procedure,
        Guid ownerToken,
        IEnumerable<Guid> ids,
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
            tvp.Rows.Add(id);
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
                command.Parameters.AddWithValue("@OwnerToken", ownerToken);
                var parameter = command.Parameters.AddWithValue("@Ids", tvp);
                parameter.SqlDbType = SqlDbType.Structured;
                parameter.TypeName = $"[{options.SchemaName}].[GuidIdList]";

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<Guid> StartJoinAsync(
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
        Guid joinId,
        Guid outboxMessageId,
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
        Guid joinId,
        Guid outboxMessageId,
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
        Guid joinId,
        Guid outboxMessageId,
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
