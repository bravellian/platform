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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

internal class OutboxProcessor : IHostedService
{
    private readonly string connectionString;
    private readonly SqlOutboxOptions options;
    private readonly ISystemLeaseFactory leaseFactory;
    private readonly TimeProvider timeProvider;
    private readonly string instanceId = $"{Environment.MachineName}:{Guid.NewGuid()}"; // Unique ID for this processor instance
    private readonly string selectSql;
    private readonly string successSql;
    private readonly string failureSql;
    private readonly string fencingStateUpdateSql;

    public OutboxProcessor(IOptions<SqlOutboxOptions> options, ISystemLeaseFactory leaseFactory, TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.connectionString = this.options.ConnectionString;
        this.leaseFactory = leaseFactory;
        this.timeProvider = timeProvider;

        // Build SQL queries using configured schema and table names
        this.selectSql = $"SELECT TOP 10 * FROM [{this.options.SchemaName}].[{this.options.TableName}] WHERE IsProcessed = 0 AND NextAttemptAt <= SYSDATETIMEOFFSET() ORDER BY CreatedAt;";
        
        this.successSql = $@"
            UPDATE [{this.options.SchemaName}].[{this.options.TableName}]
            SET IsProcessed = 1, ProcessedAt = SYSDATETIMEOFFSET(), ProcessedBy = @InstanceId
            WHERE Id = @Id AND @FencingToken >= (SELECT ISNULL(CurrentFencingToken, 0) FROM [{this.options.SchemaName}].[OutboxState] WHERE Id = 1);";
        
        this.failureSql = $@"
            UPDATE [{this.options.SchemaName}].[{this.options.TableName}]
            SET RetryCount = @RetryCount, LastError = @Error, NextAttemptAt = @NextAttempt
            WHERE Id = @Id;";

        // SQL to update the fencing token state for outbox processing
        this.fencingStateUpdateSql = $@"
            MERGE [{this.options.SchemaName}].[OutboxState] AS target
            USING (VALUES (1, @FencingToken, @LastDispatchAt)) AS source (Id, FencingToken, LastDispatchAt)
            ON target.Id = source.Id
            WHEN MATCHED AND @FencingToken >= target.CurrentFencingToken THEN
                UPDATE SET CurrentFencingToken = @FencingToken, LastDispatchAt = @LastDispatchAt
            WHEN NOT MATCHED THEN
                INSERT (Id, CurrentFencingToken, LastDispatchAt) VALUES (1, @FencingToken, @LastDispatchAt);";
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(
            async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await this.ProcessOutboxMessagesAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(5000, cancellationToken).ConfigureAwait(false); // Poll every 5 seconds
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        // Try to acquire a lease for outbox processing
        var lease = await this.leaseFactory.AcquireAsync(
            "outbox:dispatch", 
            TimeSpan.FromSeconds(30), 
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (lease == null)
        {
            // Lock not acquired, another instance is processing.
            return;
        }

        await using (lease.ConfigureAwait(false))
        {
            try
            {
                // Process messages while we hold the lease
                await this.ProcessMessagesWithLease(lease, cancellationToken).ConfigureAwait(false);
            }
            catch (LostLeaseException)
            {
                // Lease was lost during processing - stop immediately
                return;
            }
        }
    }

    private async Task ProcessMessagesWithLease(ISystemLease lease, CancellationToken cancellationToken)
    {
        // Use the lease's cancellation token which will be canceled if we lose the lease
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.CancellationToken);
        var combinedToken = combinedCts.Token;

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(this.connectionString);
        await connection.OpenAsync(combinedToken).ConfigureAwait(false);

        // Update the fencing state to indicate we're the current processor
        await connection.ExecuteAsync(this.fencingStateUpdateSql, new 
        { 
            FencingToken = lease.FencingToken, 
            LastDispatchAt = this.timeProvider.GetUtcNow() 
        }).ConfigureAwait(false);

        // Fetch messages that are ready to be processed
        var messages = await connection.QueryAsync<OutboxMessage>(this.selectSql)
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            // Check if we still hold the lease before processing each message
            lease.ThrowIfLost();

            try
            {
                // 1. Attempt to send the message
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await this.SendMessageToBrokerAsync(message).ConfigureAwait(false);
                    SchedulerMetrics.OutboxMessagesSent.Add(1);
                }
                catch
                {
                    SchedulerMetrics.OutboxMessagesFailed.Add(1);
                    throw;
                }
                finally
                {
                    stopwatch.Stop();
                    SchedulerMetrics.OutboxSendDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
                }

                // 2. If successful, mark as processed with fencing token validation
                var rowsAffected = await connection.ExecuteAsync(this.successSql, new 
                { 
                    message.Id, 
                    InstanceId = this.instanceId,
                    FencingToken = lease.FencingToken
                }).ConfigureAwait(false);

                if (rowsAffected == 0)
                {
                    // Fencing check failed - we may have lost the lease
                    throw new LostLeaseException(lease.ResourceName, lease.OwnerToken);
                }
            }
            catch (LostLeaseException)
            {
                throw; // Re-throw lease lost exceptions
            }
            catch (Exception ex)
            {
                // 3. If it fails, update for a later retry
                var retryCount = message.RetryCount + 1;
                var nextAttempt = this.timeProvider.GetUtcNow().AddSeconds(Math.Pow(2, retryCount)); // Exponential backoff

                await connection.ExecuteAsync(this.failureSql, new
                {
                    message.Id,
                    RetryCount = retryCount,
                    Error = ex.Message,
                    NextAttempt = nextAttempt,
                }).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> SendMessageToBrokerAsync(OutboxMessage message)
    {
        // Simulate sending the message
        // In a real implementation, you would have your message broker client code here.
        System.Console.WriteLine($"Sending message {message.Id} to topic {message.Topic}");
        await Task.Delay(100).ConfigureAwait(false); // Simulate network latency
        return true; // Assume it was sent successfully
    }
}
