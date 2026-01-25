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


using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;
/// <summary>
/// PostgreSQL-based implementation of a system lease factory.
/// </summary>
internal sealed class PostgresLeaseFactory : ISystemLeaseFactory
{
    private readonly LeaseFactoryConfig config;
    private readonly ILogger<PostgresLeaseFactory> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresLeaseFactory"/> class.
    /// </summary>
    /// <param name="config">The lease configuration.</param>
    /// <param name="logger">The logger.</param>
    public PostgresLeaseFactory(LeaseFactoryConfig config, ILogger<PostgresLeaseFactory> logger)
    {
        this.config = config;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ISystemLease?> AcquireAsync(
        string resourceName,
        TimeSpan leaseDuration,
        string? contextJson = null,
        OwnerToken? ownerToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        // Create context with host information if none provided
        var finalContextJson = contextJson ?? CreateDefaultContext();

        var lease = await PostgresLease.AcquireAsync(
            config.ConnectionString,
            config.SchemaName,
            resourceName,
            leaseDuration,
            config.RenewPercent,
            config.UseGate,
            config.GateTimeoutMs,
            finalContextJson,
            ownerToken,
            cancellationToken,
            logger).ConfigureAwait(false);

        if (lease != null)
        {
            SchedulerMetrics.LeasesAcquired.Add(1, [new("resource", resourceName)]);
        }

        return lease;
    }

    private static string CreateDefaultContext()
    {
        var context = new
        {
            Host = Environment.MachineName,
            ProcessId = Environment.ProcessId,
            AppVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown",
            InstanceId = Guid.NewGuid().ToString(),
            AcquiredAt = DateTimeOffset.UtcNow,
        };

        return JsonSerializer.Serialize(context);
    }
}





