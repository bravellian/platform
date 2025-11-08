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

namespace Bravellian.Platform.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bravellian.Platform.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

public class WatchdogServiceTests
{
    [Fact]
    public void GetSnapshot_ReturnsInitialState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z")));
        services.Configure<ObservabilityOptions>(o => { });
        var serviceProvider = services.BuildServiceProvider();

        var logger = serviceProvider.GetRequiredService<ILogger<WatchdogService>>();
        var options = serviceProvider.GetRequiredService<IOptions<ObservabilityOptions>>();
        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();

        var watchdog = new WatchdogService(
            logger,
            options,
            timeProvider,
            Enumerable.Empty<IWatchdogAlertSink>(),
            Enumerable.Empty<IHeartbeatSink>());

        // Act
        var snapshot = watchdog.GetSnapshot();

        // Assert
        snapshot.ShouldNotBeNull();
        snapshot.ActiveAlerts.ShouldBeEmpty();
        snapshot.LastScanAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        snapshot.LastHeartbeatAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
    }

    [Fact]
    public async Task WatchdogService_EmitsHeartbeat()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        var heartbeatReceived = false;
        long sequenceNumber = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fakeTime);
        services.Configure<ObservabilityOptions>(o =>
        {
            o.Watchdog.ScanPeriod = TimeSpan.FromSeconds(5);
            o.Watchdog.HeartbeatPeriod = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<IHeartbeatSink>(new DelegateHeartbeatSink((ctx, ct) =>
        {
            heartbeatReceived = true;
            sequenceNumber = ctx.SequenceNumber;
            return Task.CompletedTask;
        }));

        var serviceProvider = services.BuildServiceProvider();

        var logger = serviceProvider.GetRequiredService<ILogger<WatchdogService>>();
        var options = serviceProvider.GetRequiredService<IOptions<ObservabilityOptions>>();
        var heartbeatSinks = serviceProvider.GetRequiredService<IEnumerable<IHeartbeatSink>>();

        var watchdog = new WatchdogService(
            logger,
            options,
            fakeTime,
            Enumerable.Empty<IWatchdogAlertSink>(),
            heartbeatSinks);

        using var cts = new CancellationTokenSource();

        // Act
        var watchdogTask = watchdog.StartAsync(cts.Token);
        
        // Advance time to trigger heartbeat (HeartbeatPeriod is 10s)
        fakeTime.Advance(TimeSpan.FromSeconds(11));
        
        // Give the service a moment to process (minimal real time needed)
        await Task.Delay(50);

        cts.Cancel();
        await watchdog.StopAsync(default);
        await watchdogTask; // Ensure background service has fully stopped

        // Assert
        heartbeatReceived.ShouldBeTrue();
        sequenceNumber.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WatchdogService_DetectsOverdueJobs()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        WatchdogAlertContext? receivedAlert = null;

        var schedulerState = new FakeSchedulerState();
        schedulerState.OverdueJobs.Add(("job-123", DateTimeOffset.Parse("2023-12-31T23:59:00Z")));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fakeTime);
        services.Configure<ObservabilityOptions>(o =>
        {
            o.Watchdog.ScanPeriod = TimeSpan.FromSeconds(5);
            o.Watchdog.JobOverdueThreshold = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<IWatchdogAlertSink>(new DelegateAlertSink((ctx, ct) =>
        {
            receivedAlert = ctx;
            return Task.CompletedTask;
        }));

        var serviceProvider = services.BuildServiceProvider();

        var logger = serviceProvider.GetRequiredService<ILogger<WatchdogService>>();
        var options = serviceProvider.GetRequiredService<IOptions<ObservabilityOptions>>();
        var alertSinks = serviceProvider.GetRequiredService<IEnumerable<IWatchdogAlertSink>>();

        var watchdog = new WatchdogService(
            logger,
            options,
            fakeTime,
            alertSinks,
            Enumerable.Empty<IHeartbeatSink>(),
            schedulerState: schedulerState);

        using var cts = new CancellationTokenSource();

        // Act
        var watchdogTask = watchdog.StartAsync(cts.Token);
        
        // Advance time to trigger scan
        fakeTime.Advance(TimeSpan.FromSeconds(6));
        await Task.Delay(100); // Give the service time to process

        cts.Cancel();
        await watchdog.StopAsync(default);
        await watchdogTask; // Ensure background service has fully stopped

        // Assert
        receivedAlert.ShouldNotBeNull();
        receivedAlert.Kind.ShouldBe(WatchdogAlertKind.OverdueJob);
        receivedAlert.Component.ShouldBe("scheduler");
        receivedAlert.Key.ShouldContain("job-123");
    }

    [Fact]
    public void WatchdogHealthCheck_ReturnsHealthy_WhenNoAlerts()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fakeTime);
        services.Configure<ObservabilityOptions>(o => { });
        var serviceProvider = services.BuildServiceProvider();

        var logger = serviceProvider.GetRequiredService<ILogger<WatchdogService>>();
        var options = serviceProvider.GetRequiredService<IOptions<ObservabilityOptions>>();

        var watchdog = new WatchdogService(
            logger,
            options,
            fakeTime,
            Enumerable.Empty<IWatchdogAlertSink>(),
            Enumerable.Empty<IHeartbeatSink>());

        var healthCheck = new WatchdogHealthCheck(watchdog, fakeTime, options);

        // Act
        var result = healthCheck.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext()).Result;

        // Assert
        result.Status.ShouldBe(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
    }

    private class FakeSchedulerState : ISchedulerState
    {
        public List<(string JobId, DateTimeOffset DueTime)> OverdueJobs { get; } = new();

        public Task<IReadOnlyList<(string JobId, DateTimeOffset DueTime)>> GetOverdueJobsAsync(TimeSpan threshold, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<(string JobId, DateTimeOffset DueTime)>>(this.OverdueJobs);
        }
    }

    private class DelegateAlertSink : IWatchdogAlertSink
    {
        private readonly Func<WatchdogAlertContext, CancellationToken, Task> handler;

        public DelegateAlertSink(Func<WatchdogAlertContext, CancellationToken, Task> handler)
        {
            this.handler = handler;
        }

        public Task OnAlertAsync(WatchdogAlertContext context, CancellationToken cancellationToken)
        {
            return this.handler(context, cancellationToken);
        }
    }

    private class DelegateHeartbeatSink : IHeartbeatSink
    {
        private readonly Func<HeartbeatContext, CancellationToken, Task> handler;

        public DelegateHeartbeatSink(Func<HeartbeatContext, CancellationToken, Task> handler)
        {
            this.handler = handler;
        }

        public Task OnHeartbeatAsync(HeartbeatContext context, CancellationToken cancellationToken)
        {
            return this.handler(context, cancellationToken);
        }
    }
}
