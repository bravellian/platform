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

namespace Bravellian.Platform.Metrics;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Health check for the metrics exporter service.
/// </summary>
internal sealed class MetricsExporterHealthCheck : IHealthCheck
{
    private readonly MetricsExporterService _exporterService;

    public MetricsExporterHealthCheck(MetricsExporterService exporterService)
    {
        _exporterService = exporterService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);

        if (_exporterService.LastFlushUtc.HasValue)
        {
            var timeSinceLastFlush = DateTime.UtcNow - _exporterService.LastFlushUtc.Value;
            data["last_flush_utc"] = _exporterService.LastFlushUtc.Value;
            data["seconds_since_last_flush"] = timeSinceLastFlush.TotalSeconds;

            if (_exporterService.LastError != null)
            {
                data["last_error"] = _exporterService.LastError;
                return Task.FromResult(HealthCheckResult.Degraded("Metrics exporter has errors", data: data));
            }

            if (timeSinceLastFlush > TimeSpan.FromMinutes(2))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Metrics exporter flush is stale", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Metrics exporter is healthy", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Metrics exporter starting", data: data));
    }
}
