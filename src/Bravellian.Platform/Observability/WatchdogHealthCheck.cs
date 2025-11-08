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

namespace Bravellian.Platform.Observability;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

/// <summary>
/// Health check that evaluates the overall health of the watchdog system.
/// </summary>
internal sealed class WatchdogHealthCheck : IHealthCheck
{
    private readonly IWatchdog watchdog;
    private readonly TimeProvider timeProvider;
    private readonly IOptions<ObservabilityOptions> options;

    public WatchdogHealthCheck(
        IWatchdog watchdog,
        TimeProvider timeProvider,
        IOptions<ObservabilityOptions> options)
    {
        this.watchdog = watchdog;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = this.watchdog.GetSnapshot();
            var now = this.timeProvider.GetUtcNow();
            var opts = this.options.Value;

            // Check heartbeat staleness
            var heartbeatAge = now - snapshot.LastHeartbeatAt;
            if (heartbeatAge > opts.Watchdog.HeartbeatTimeout)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"Watchdog heartbeat is stale (last: {heartbeatAge.TotalSeconds:F0}s ago)."));
            }

            // Check for critical alerts (using OverdueJob and ProcessorNotRunning as critical examples)
            var criticalAlerts = snapshot.ActiveAlerts
                .Where(a => a.Kind == WatchdogAlertKind.OverdueJob || a.Kind == WatchdogAlertKind.ProcessorNotRunning)
                .ToList();

            if (criticalAlerts.Count > 0)
            {
                var messages = string.Join("; ", criticalAlerts.Select(a => a.Message));
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"{criticalAlerts.Count} critical alert(s): {messages}"));
            }

            // Check for warning alerts (using stuck messages)
            var warningAlerts = snapshot.ActiveAlerts
                .Where(a => a.Kind == WatchdogAlertKind.StuckInbox || a.Kind == WatchdogAlertKind.StuckOutbox)
                .ToList();

            if (warningAlerts.Count > 0)
            {
                var messages = string.Join("; ", warningAlerts.Select(a => a.Message));
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"{warningAlerts.Count} warning alert(s): {messages}"));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Watchdog is healthy. Last scan: {(now - snapshot.LastScanAt).TotalSeconds:F0}s ago."));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Failed to check watchdog health.", ex));
        }
    }
}
