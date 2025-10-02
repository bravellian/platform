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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background service that periodically polls and processes outbox messages.
/// Uses monotonic clock for consistent polling intervals regardless of system time changes.
/// Waits for database schema deployment to complete before starting.
/// </summary>
public sealed class OutboxPollingService : BackgroundService
{
    private readonly OutboxDispatcher _dispatcher;
    private readonly IMonotonicClock _mono;
    private readonly IDatabaseSchemaCompletion? _schemaCompletion;
    private readonly double _intervalSeconds;
    private readonly int _batchSize;
    private readonly ILogger<OutboxPollingService> _logger;

    public OutboxPollingService(
        OutboxDispatcher dispatcher, 
        IMonotonicClock mono,
        ILogger<OutboxPollingService> logger,
        double intervalSeconds = 0.25, // 250ms default
        int batchSize = 50,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        _dispatcher = dispatcher;
        _mono = mono;
        _logger = logger;
        _schemaCompletion = schemaCompletion;
        _intervalSeconds = intervalSeconds;
        _batchSize = batchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting outbox polling service with {IntervalMs}ms interval and batch size {BatchSize}", 
            _intervalSeconds * 1000, _batchSize);
        
        // Wait for schema deployment to complete if available
        if (_schemaCompletion != null)
        {
            _logger.LogDebug("Waiting for database schema deployment to complete");
            try
            {
                await _schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
                _logger.LogInformation("Database schema deployment completed successfully");
            }
            catch (Exception ex)
            {
                // Log and continue - schema deployment errors should not prevent outbox processing
                _logger.LogWarning(ex, "Schema deployment failed, but continuing with outbox processing");
            }
        }
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = _mono.Seconds + _intervalSeconds;
            
            try
            {
                var processedCount = await _dispatcher.RunOnceAsync(_batchSize, stoppingToken).ConfigureAwait(false);
                if (processedCount > 0)
                {
                    _logger.LogDebug("Outbox polling iteration completed: {ProcessedCount} messages processed", processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("Outbox polling service stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let processing errors stop the service
                _logger.LogError(ex, "Error in outbox polling iteration - continuing with next iteration");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - _mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }
        
        _logger.LogInformation("Outbox polling service stopped");
    }
}