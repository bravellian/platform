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

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// SQL Server-based implementation of a system lease factory.
/// </summary>
internal sealed class SqlLeaseFactory : ISystemLeaseFactory
{
    private readonly SystemLeaseOptions options;
    private readonly ILogger<SqlLeaseFactory> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlLeaseFactory"/> class.
    /// </summary>
    /// <param name="options">The lease options.</param>
    /// <param name="logger">The logger.</param>
    public SqlLeaseFactory(IOptions<SystemLeaseOptions> options, ILogger<SqlLeaseFactory> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ISystemLease?> AcquireAsync(
        string resourceName,
        TimeSpan leaseDuration,
        string? contextJson = null,
        Guid? ownerToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        // Create context with host information if none provided
        var finalContextJson = contextJson ?? CreateDefaultContext();

        var lease = await SqlLease.AcquireAsync(
            this.options.ConnectionString,
            this.options.SchemaName,
            resourceName,
            leaseDuration,
            this.options.RenewPercent,
            this.options.UseGate,
            this.options.GateTimeoutMs,
            finalContextJson,
            ownerToken,
            cancellationToken,
            this.logger).ConfigureAwait(false);

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
