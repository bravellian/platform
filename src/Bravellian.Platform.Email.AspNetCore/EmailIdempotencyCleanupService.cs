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

using System.Diagnostics.CodeAnalysis;
using Bravellian.Platform;
using Bravellian.Platform.Idempotency;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Email.AspNetCore;

/// <summary>
/// Background service that periodically cleans up old idempotency records.
/// </summary>
public sealed class EmailIdempotencyCleanupService : BackgroundService
{
    private readonly IIdempotencyStoreProvider storeProvider;
    private readonly TimeSpan retentionPeriod;
    private readonly TimeSpan cleanupInterval;
    private readonly IMonotonicClock mono;
    private readonly IDatabaseSchemaCompletion? schemaCompletion;
    private readonly ILogger<EmailIdempotencyCleanupService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailIdempotencyCleanupService"/> class.
    /// </summary>
    /// <param name="options">Cleanup options.</param>
    /// <param name="storeProvider">Idempotency store provider.</param>
    /// <param name="mono">Monotonic clock.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="schemaCompletion">Optional schema completion notifier.</param>
    public EmailIdempotencyCleanupService(
        IOptions<EmailIdempotencyCleanupOptions> options,
        IIdempotencyStoreProvider storeProvider,
        IMonotonicClock mono,
        ILogger<EmailIdempotencyCleanupService> logger,
        IDatabaseSchemaCompletion? schemaCompletion = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(storeProvider);
        ArgumentNullException.ThrowIfNull(mono);
        ArgumentNullException.ThrowIfNull(logger);

        var opts = options.Value;
        retentionPeriod = opts.RetentionPeriod;
        cleanupInterval = opts.CleanupInterval;
        this.storeProvider = storeProvider;
        this.mono = mono;
        this.logger = logger;
        this.schemaCompletion = schemaCompletion;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Cleanup loop logs failures and continues.")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Starting email idempotency cleanup with retention period {RetentionPeriod} and cleanup interval {CleanupInterval}",
            retentionPeriod,
            cleanupInterval);

        if (schemaCompletion != null)
        {
            logger.LogDebug("Waiting for database schema deployment to complete");
            try
            {
                await schemaCompletion.SchemaDeploymentCompleted.ConfigureAwait(false);
                logger.LogInformation("Database schema deployment completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Schema deployment failed, but continuing with idempotency cleanup");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = mono.Seconds + cleanupInterval.TotalSeconds;

            try
            {
                var deletedCount = await CleanupAllStoresAsync(stoppingToken).ConfigureAwait(false);
                if (deletedCount > 0)
                {
                    logger.LogInformation("Email idempotency cleanup deleted {DeletedCount} records", deletedCount);
                }
                else
                {
                    logger.LogDebug("Email idempotency cleanup completed: no records to delete");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Email idempotency cleanup stopped due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during email idempotency cleanup - continuing with next iteration");
            }

            var sleep = Math.Max(0, next - mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken).ConfigureAwait(false);
            }
        }

        logger.LogInformation("Email idempotency cleanup service stopped");
    }

    private async Task<int> CleanupAllStoresAsync(CancellationToken cancellationToken)
    {
        var stores = await storeProvider.GetAllStoresAsync().ConfigureAwait(false);
        var totalDeleted = 0;

        foreach (var store in stores)
        {
            var identifier = storeProvider.GetStoreIdentifier(store);

            if (store is not IIdempotencyCleanupStore cleanupStore)
            {
                logger.LogDebug("Skipping idempotency cleanup for store without cleanup support: {StoreIdentifier}", identifier);
                continue;
            }

            try
            {
                var deleted = await cleanupStore.CleanupAsync(retentionPeriod, cancellationToken).ConfigureAwait(false);
                totalDeleted += deleted;

                if (deleted > 0)
                {
                    logger.LogDebug("Deleted {DeletedCount} idempotency records from store {StoreIdentifier}", deleted, identifier);
                }
            }
            catch (Exception ex) when (ExceptionFilter.IsCatchable(ex))
            {
                logger.LogError(ex, "Failed to cleanup idempotency records for store {StoreIdentifier}", identifier);
            }
        }

        return totalDeleted;
    }
}
