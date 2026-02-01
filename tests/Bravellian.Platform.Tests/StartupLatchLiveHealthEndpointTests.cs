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

using System.Net;
using Bravellian.Platform.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.Tests;

public sealed class StartupLatchLiveHealthEndpointTests
{
    /// <summary>When live Endpoint Returns503 Until Startup Completes, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for live Endpoint Returns503 Until Startup Completes.</intent>
    /// <scenario>Given live Endpoint Returns503 Until Startup Completes.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task LiveEndpoint_Returns503UntilStartupCompletes()
    {
        await using var app = await BuildAppAsync();
        var client = app.GetTestClient();

        var latch = app.Services.GetRequiredService<IStartupLatch>();
        using var step = latch.Register("platform-migrations");

        var starting = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        starting.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        step.Dispose();

        var ready = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<WebApplication> BuildAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddPlatformObservability()
            .AddPlatformHealthChecks();

        var app = builder.Build();
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live", StringComparer.Ordinal),
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
