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


using Bravellian.Platform.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bravellian.Platform.Tests;

public class ObservabilityRegistrationTests
{
    [Fact]
    public void AddPlatformObservability_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddPlatformObservability();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var watchdog = serviceProvider.GetService<IWatchdog>();
        watchdog.ShouldNotBeNull();

        var hostedServices = serviceProvider.GetServices<IHostedService>();
        hostedServices.ShouldContain(s => s is WatchdogService);
    }

    [Fact]
    public void AddPlatformObservability_AllowsConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddPlatformObservability(o =>
        {
            o.EnableMetrics = false;
            o.EnableLogging = true;
            o.Watchdog.ScanPeriod = TimeSpan.FromSeconds(60);
        });

        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ObservabilityOptions>>();

        // Assert
        options.Value.EnableMetrics.ShouldBeFalse();
        options.Value.EnableLogging.ShouldBeTrue();
        options.Value.Watchdog.ScanPeriod.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ObservabilityBuilder_CanAddAlertSink()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddPlatformObservability()
            .AddWatchdogAlertSink((ctx, ct) =>
            {
                return Task.CompletedTask;
            });

        var serviceProvider = services.BuildServiceProvider();
        var sinks = serviceProvider.GetServices<IWatchdogAlertSink>();

        // Assert
        sinks.ShouldNotBeEmpty();
    }

    [Fact]
    public void ObservabilityBuilder_CanAddHeartbeatSink()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddPlatformObservability()
            .AddHeartbeatSink((ctx, ct) => Task.CompletedTask);

        var serviceProvider = services.BuildServiceProvider();
        var sinks = serviceProvider.GetServices<IHeartbeatSink>();

        // Assert
        sinks.ShouldNotBeEmpty();
    }

    [Fact]
    public void ObservabilityBuilder_CanAddHealthChecks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddPlatformObservability()
            .AddPlatformHealthChecks();

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        // Health checks are registered via AddHealthChecks, we can verify the service is present
        var healthCheckService = serviceProvider.GetService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
        healthCheckService.ShouldNotBeNull();
    }

    [Fact]
    public void WatchdogAlertContext_ContainsAllRequiredFields()
    {
        // Arrange
        var attributes = new System.Collections.Generic.Dictionary<string, object?>
(StringComparer.Ordinal)
        {
            ["test_key"] = "test_value",
        };

        // Act
        var context = new WatchdogAlertContext(
            WatchdogAlertKind.OverdueJob,
            "scheduler",
            "job-123",
            "Test message",
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2024-01-01T00:01:00Z"),
            attributes);

        // Assert
        context.Kind.ShouldBe(WatchdogAlertKind.OverdueJob);
        context.Component.ShouldBe("scheduler");
        context.Key.ShouldBe("job-123");
        context.Message.ShouldBe("Test message");
        context.FirstSeenAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        context.LastSeenAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:01:00Z"));
        context.Attributes["test_key"].ShouldBe("test_value");
    }

    [Fact]
    public void WatchdogSnapshot_ContainsAlerts()
    {
        // Arrange
        var alerts = new[]
        {
            new ActiveAlert(
                WatchdogAlertKind.StuckInbox,
                "inbox",
                "msg-123",
                "Message stuck",
                DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                DateTimeOffset.Parse("2024-01-01T00:05:00Z"),
                new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal)),
        };

        // Act
        var snapshot = new WatchdogSnapshot(
            DateTimeOffset.Parse("2024-01-01T00:10:00Z"),
            DateTimeOffset.Parse("2024-01-01T00:09:00Z"),
            alerts);

        // Assert
        snapshot.LastScanAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:10:00Z"));
        snapshot.LastHeartbeatAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:09:00Z"));
        snapshot.ActiveAlerts.Count.ShouldBe(1);
        snapshot.ActiveAlerts[0].Kind.ShouldBe(WatchdogAlertKind.StuckInbox);
    }
}
