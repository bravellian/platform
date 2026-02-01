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
using Microsoft.Extensions.Hosting;

namespace Bravellian.Platform.Tests;

public sealed class StartupCheckLiveHealthEndpointTests
{
    /// <summary>When live Endpoint Returns503 While Startup Checks Run, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for live Endpoint Returns503 While Startup Checks Run.</intent>
    /// <scenario>Given live Endpoint Returns503 While Startup Checks Run.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task LiveEndpoint_Returns503WhileStartupChecksRun()
    {
        await using var app = await BuildAppAsync();
        var client = app.GetTestClient();

        var check = app.Services.GetRequiredService<BlockingStartupCheck>();
        await check.Started.WaitAsync(TestContext.Current.CancellationToken);

        var starting = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
        starting.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        check.Release();
        await check.Completed.WaitAsync(TestContext.Current.CancellationToken);

        var runner = app.Services
            .GetServices<IHostedService>()
            .OfType<StartupCheckRunnerHostedService>()
            .Single();
        await runner.Completion;

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
        builder.Services.AddStartupCheckRunner();
        builder.Services.AddSingleton<BlockingStartupCheck>();
        builder.Services.AddSingleton<IStartupCheck>(sp => sp.GetRequiredService<BlockingStartupCheck>());

        var app = builder.Build();
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live", StringComparer.Ordinal),
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class BlockingStartupCheck : IStartupCheck
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public Task Completed => completed.Task;

        public string Name => "blocking";

        public int Order => 0;

        public bool IsCritical => true;

        public async Task ExecuteAsync(CancellationToken ct)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct).ConfigureAwait(false);
            completed.TrySetResult();
        }

        public void Release()
        {
            release.TrySetResult();
        }
    }
}
