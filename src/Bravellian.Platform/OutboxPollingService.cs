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

    public OutboxPollingService(
        OutboxDispatcher dispatcher, 
        IMonotonicClock mono,
        double intervalSeconds = 0.25, // 250ms default
        int batchSize = 50,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        _dispatcher = dispatcher;
        _mono = mono;
        _schemaCompletion = schemaCompletion;
        _intervalSeconds = intervalSeconds;
        _batchSize = batchSize;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for schema deployment to complete if available
        if (_schemaCompletion != null)
        {
            try
            {
                await _schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Log and continue - schema deployment errors should not prevent outbox processing
                System.Diagnostics.Debug.WriteLine($"Schema deployment failed, but continuing with outbox processing: {ex}");
            }
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            var next = _mono.Seconds + _intervalSeconds;
            
            try
            {
                await _dispatcher.RunOnceAsync(_batchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log and continue - don't let processing errors stop the service
                System.Diagnostics.Debug.WriteLine($"Error in outbox polling: {ex}");
            }

            // Sleep until next interval, using monotonic clock to avoid time jumps
            var sleep = Math.Max(0, next - _mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}