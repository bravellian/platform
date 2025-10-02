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

namespace Bravellian.Platform;

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// SQL Server implementation of IInboxWorkStore using work queue stored procedures.
/// </summary>
internal class SqlInboxWorkStore : IInboxWorkStore
{
    private readonly string connectionString;
    private readonly string schemaName;
    private readonly string tableName;
    private readonly ILogger<SqlInboxWorkStore> logger;

    public SqlInboxWorkStore(IOptions<SqlInboxOptions> options, ILogger<SqlInboxWorkStore> logger)
    {
        var opts = options.Value;
        this.connectionString = opts.ConnectionString;
        this.schemaName = opts.SchemaName;
        this.tableName = opts.TableName;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<string>> ClaimAsync(
        Guid ownerToken, 
        int leaseSeconds, 
        int batchSize, 
        CancellationToken cancellationToken)
    {
        this.logger.LogDebug(
            "Claiming up to {BatchSize} inbox messages with {LeaseSeconds}s lease for owner {OwnerToken}", 
            batchSize, leaseSeconds, ownerToken);

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var messageIds = await connection.QueryAsync<string>(
                $"[{this.schemaName}].[{this.tableName}_Claim]",
                new
                {
                    OwnerToken = ownerToken,
                    LeaseSeconds = leaseSeconds,
                    BatchSize = batchSize
                },
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

            var result = messageIds.ToList();
            this.logger.LogDebug(
                "Successfully claimed {ClaimedCount} inbox messages for owner {OwnerToken}", 
                result.Count, ownerToken);

            return result;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, 
                "Failed to claim inbox messages for owner {OwnerToken}", 
                ownerToken);
            throw;
        }
    }

    public async Task AckAsync(
        Guid ownerToken, 
        IEnumerable<string> messageIds, 
        CancellationToken cancellationToken)
    {
        var messageIdList = messageIds.ToList();
        if (messageIdList.Count == 0)
        {
            return;
        }

        this.logger.LogDebug(
            "Acknowledging {MessageCount} inbox messages for owner {OwnerToken}", 
            messageIdList.Count, ownerToken);

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var idsTable = CreateStringIdTable(messageIdList);
            await connection.ExecuteAsync(
                $"[{this.schemaName}].[{this.tableName}_Ack]",
                new
                {
                    OwnerToken = ownerToken,
                    Ids = idsTable
                },
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

            this.logger.LogDebug(
                "Successfully acknowledged {MessageCount} inbox messages for owner {OwnerToken}", 
                messageIdList.Count, ownerToken);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, 
                "Failed to acknowledge inbox messages for owner {OwnerToken}", 
                ownerToken);
            throw;
        }
    }

    public async Task AbandonAsync(
        Guid ownerToken, 
        IEnumerable<string> messageIds, 
        CancellationToken cancellationToken)
    {
        var messageIdList = messageIds.ToList();
        if (messageIdList.Count == 0)
        {
            return;
        }

        this.logger.LogDebug(
            "Abandoning {MessageCount} inbox messages for owner {OwnerToken}", 
            messageIdList.Count, ownerToken);

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var idsTable = CreateStringIdTable(messageIdList);
            await connection.ExecuteAsync(
                $"[{this.schemaName}].[{this.tableName}_Abandon]",
                new
                {
                    OwnerToken = ownerToken,
                    Ids = idsTable
                },
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

            this.logger.LogDebug(
                "Successfully abandoned {MessageCount} inbox messages for owner {OwnerToken}", 
                messageIdList.Count, ownerToken);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, 
                "Failed to abandon inbox messages for owner {OwnerToken}", 
                ownerToken);
            throw;
        }
    }

    public async Task FailAsync(
        Guid ownerToken, 
        IEnumerable<string> messageIds, 
        string error, 
        CancellationToken cancellationToken)
    {
        var messageIdList = messageIds.ToList();
        if (messageIdList.Count == 0)
        {
            return;
        }

        this.logger.LogDebug(
            "Failing {MessageCount} inbox messages for owner {OwnerToken}: {Error}", 
            messageIdList.Count, ownerToken, error);

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var idsTable = CreateStringIdTable(messageIdList);
            await connection.ExecuteAsync(
                $"[{this.schemaName}].[{this.tableName}_Fail]",
                new
                {
                    OwnerToken = ownerToken,
                    Ids = idsTable,
                    Reason = error
                },
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

            this.logger.LogWarning(
                "Failed {MessageCount} inbox messages for owner {OwnerToken}: {Error}", 
                messageIdList.Count, ownerToken, error);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, 
                "Failed to mark inbox messages as failed for owner {OwnerToken}", 
                ownerToken);
            throw;
        }
    }

    public async Task ReapExpiredAsync(CancellationToken cancellationToken)
    {
        this.logger.LogDebug("Reaping expired inbox leases");

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var result = await connection.QuerySingleAsync<int>(
                $"[{this.schemaName}].[{this.tableName}_ReapExpired]",
                commandType: System.Data.CommandType.StoredProcedure).ConfigureAwait(false);

            if (result > 0)
            {
                this.logger.LogInformation(
                    "Reaped {ReapedCount} expired inbox leases", 
                    result);
            }
            else
            {
                this.logger.LogDebug("No expired inbox leases found to reap");
            }
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to reap expired inbox leases");
            throw;
        }
    }

    public async Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            throw new ArgumentException("MessageId cannot be null or empty", nameof(messageId));
        }

        this.logger.LogDebug("Getting inbox message {MessageId}", messageId);

        try
        {
            using var connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sql = $"""

                                SELECT MessageId, Source, Topic, Payload, Hash, Attempts, FirstSeenUtc, LastSeenUtc
                                FROM [{this.schemaName}].[{this.tableName}]
                                WHERE MessageId = @MessageId
                """;

            var row = await connection.QuerySingleOrDefaultAsync(sql, new { MessageId = messageId }).ConfigureAwait(false);
            
            if (row == null)
            {
                throw new InvalidOperationException($"Inbox message '{messageId}' not found");
            }

            return new InboxMessage
            {
                MessageId = row.MessageId,
                Source = row.Source ?? string.Empty,
                Topic = row.Topic ?? string.Empty,
                Payload = row.Payload ?? string.Empty,
                Hash = row.Hash,
                Attempt = row.Attempts,
                FirstSeenUtc = row.FirstSeenUtc,
                LastSeenUtc = row.LastSeenUtc
            };
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to get inbox message {MessageId}", messageId);
            throw;
        }
    }

    private static System.Data.DataTable CreateStringIdTable(IList<string> ids)
    {
        var table = new System.Data.DataTable();
        table.Columns.Add("Id", typeof(string));
        
        // Pre-size the table to avoid dynamic resizing
        table.MinimumCapacity = ids.Count;

        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }

        return table;
    }
}