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

/*
 CREATE TABLE dbo.Outbox (
    -- Core Fields
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Payload NVARCHAR(MAX) NOT NULL,
    Topic NVARCHAR(255) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),

    -- Processing Status & Auditing (Your suggestions)
    IsProcessed BIT NOT NULL DEFAULT 0,
    ProcessedAt DATETIMEOFFSET NULL,
    ProcessedBy NVARCHAR(100) NULL, -- e.g., machine name or instance ID

    -- For Robustness & Error Handling
    RetryCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(), -- For backoff strategies

    -- For Idempotency & Tracing
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(), -- A stable ID for the message consumer
    CorrelationId UNIQUEIDENTIFIER NULL -- To trace a message through multiple systems
);
GO

-- An index to efficiently query for unprocessed messages, now including the next attempt time.
CREATE INDEX IX_Outbox_GetNext ON dbo.Outbox(IsProcessed, NextAttemptAt)
    INCLUDE(Id, Payload, Topic, RetryCount) -- Include columns needed for processing
    WHERE IsProcessed = 0;
GO
 */
internal class OutboxProcessor : IHostedService // Example for a hosted service in ASP.NET Core
{
    private readonly string connectionString;
    private readonly ISqlDistributedLock distributedLock;
    private readonly string instanceId = $"{Environment.MachineName}:{Guid.NewGuid()}"; // Unique ID for this processor instance

    public OutboxProcessor(IOptions<SqlOutboxOptions> options, ISqlDistributedLock distributedLock)
    {
        this.connectionString = options.Value.ConnectionString;
        this.distributedLock = distributedLock;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(
            async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await this.ProcessOutboxMessagesAsync().ConfigureAwait(false);
                await Task.Delay(5000, cancellationToken).ConfigureAwait(false); // Poll every 5 seconds
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task ProcessOutboxMessagesAsync()
    {
        // Use your locking abstraction. We try to acquire the lock for a very short
        // time (0s) because we don't want to wait if another instance is already running.
        var handle = await this.distributedLock.AcquireAsync("OutboxProcessorLock", TimeSpan.Zero).ConfigureAwait(false);

        await using (handle.ConfigureAwait(false))
        {
            if (handle == null)
            {
                // Lock not acquired, another instance is processing.
                return;
            }

            // Lock acquired, proceed with processing.
            using (var connection = new Microsoft.Data.SqlClient.SqlConnection(this.connectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);

                // Fetch messages that are ready to be processed
                var messages = await connection.QueryAsync<OutboxMessage>(
                    "SELECT TOP 10 * FROM Outbox WHERE IsProcessed = 0 AND NextAttemptAt <= SYSDATETIMEOFFSET() ORDER BY CreatedAt;")
                .ConfigureAwait(false);

                foreach (var message in messages)
                {
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

                        // 2. If successful, mark as processed
                        var successSql = @"
                            UPDATE Outbox
                            SET IsProcessed = 1, ProcessedAt = SYSDATETIMEOFFSET(), ProcessedBy = @InstanceId
                            WHERE Id = @Id;";
                        await connection.ExecuteAsync(successSql, new { message.Id, InstanceId = this.instanceId }).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // 3. If it fails, update for a later retry
                        var retryCount = message.RetryCount + 1;
                        var nextAttempt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, retryCount)); // Exponential backoff

                        var failureSql = @"
                            UPDATE Outbox
                            SET RetryCount = @RetryCount, LastError = @Error, NextAttemptAt = @NextAttempt
                            WHERE Id = @Id;";
                        await connection.ExecuteAsync(failureSql, new
                        {
                            message.Id,
                            RetryCount = retryCount,
                            Error = ex.Message,
                            NextAttempt = nextAttempt,
                        }).ConfigureAwait(false);
                    }
                }
            }
        } // The lock is released here when 'handle' is disposed.
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
