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

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bravellian.Platform.HealthChecks;

/// <summary>
/// Health check that reports readiness based on the platform startup latch.
/// </summary>
public sealed class StartupLatchHealthCheck : IHealthCheck
{
    private readonly IStartupLatch latch;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupLatchHealthCheck"/> class.
    /// </summary>
    /// <param name="latch">The startup latch.</param>
    public StartupLatchHealthCheck(IStartupLatch latch)
    {
        this.latch = latch ?? throw new ArgumentNullException(nameof(latch));
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(latch.IsReady
            ? HealthCheckResult.Healthy("Startup complete")
            : HealthCheckResult.Unhealthy("Starting"));
    }
}
