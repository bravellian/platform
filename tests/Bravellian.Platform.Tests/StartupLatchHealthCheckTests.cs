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

using Bravellian.Platform.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;

namespace Bravellian.Platform.Tests;

public class StartupLatchHealthCheckTests
{
    /// <summary>When check Health Async Returns Healthy When Latch Is Ready, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for check Health Async Returns Healthy When Latch Is Ready.</intent>
    /// <scenario>Given check Health Async Returns Healthy When Latch Is Ready.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenLatchIsReady()
    {
        var latch = new FakeStartupLatch { IsReady = true };
        var healthCheck = new StartupLatchHealthCheck(latch);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Description.ShouldBe("Startup complete");
    }

    /// <summary>When check Health Async Returns Unhealthy When Latch Is Not Ready, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for check Health Async Returns Unhealthy When Latch Is Not Ready.</intent>
    /// <scenario>Given check Health Async Returns Unhealthy When Latch Is Not Ready.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenLatchIsNotReady()
    {
        var latch = new FakeStartupLatch { IsReady = false };
        var healthCheck = new StartupLatchHealthCheck(latch);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("Starting");
    }

    private sealed class FakeStartupLatch : IStartupLatch
    {
        public bool IsReady { get; set; }

        public IDisposable Register(string stepName)
        {
            return new NoopDisposable();
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
