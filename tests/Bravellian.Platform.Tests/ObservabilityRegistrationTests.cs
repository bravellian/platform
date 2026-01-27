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
    /// <summary>When AddPlatformObservability is called, then watchdog services are registered in DI.</summary>
    /// <intent>Verify basic observability wiring registers the watchdog and hosted service.</intent>
    /// <scenario>Given a ServiceCollection with logging and AddPlatformObservability applied.</scenario>
    /// <behavior>Then IWatchdog resolves and WatchdogService is present among hosted services.</behavior>
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

    /// <summary>When AddPlatformObservability is configured, then the options reflect the provided settings.</summary>
    /// <intent>Ensure custom observability options are bound into IOptions.</intent>
    /// <scenario>Given AddPlatformObservability invoked with a configuration delegate.</scenario>
    /// <behavior>Then ObservabilityOptions exposes the configured metrics, logging, and scan period values.</behavior>
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

    /// <summary>When an alert sink is added via the builder, then it is registered for resolution.</summary>
    /// <intent>Verify watchdog alert sinks can be registered through the builder.</intent>
    /// <scenario>Given AddPlatformObservability followed by AddWatchdogAlertSink with a delegate.</scenario>
    /// <behavior>Then IWatchdogAlertSink services are present in DI.</behavior>
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

    /// <summary>When a heartbeat sink is added via the builder, then it is registered for resolution.</summary>
    /// <intent>Verify heartbeat sinks can be registered through the observability builder.</intent>
    /// <scenario>Given AddPlatformObservability followed by AddHeartbeatSink with a delegate.</scenario>
    /// <behavior>Then IHeartbeatSink services are present in DI.</behavior>
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

    /// <summary>When platform health checks are added via the builder, then the health check service is registered.</summary>
    /// <intent>Confirm health check registration is wired through observability setup.</intent>
    /// <scenario>Given AddPlatformObservability followed by AddPlatformHealthChecks.</scenario>
    /// <behavior>Then HealthCheckService resolves from the service provider.</behavior>
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

    /// <summary>When a WatchdogAlertContext is created, then all supplied fields are preserved.</summary>
    /// <intent>Verify alert context carries kind, component, timestamps, and attributes.</intent>
    /// <scenario>Given a WatchdogAlertContext constructed with explicit values and attributes.</scenario>
    /// <behavior>Then the properties match the provided inputs, including attribute entries.</behavior>
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
            DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2024-01-01T00:01:00Z", System.Globalization.CultureInfo.InvariantCulture),
            attributes);

        // Assert
        context.Kind.ShouldBe(WatchdogAlertKind.OverdueJob);
        context.Component.ShouldBe("scheduler");
        context.Key.ShouldBe("job-123");
        context.Message.ShouldBe("Test message");
        context.FirstSeenAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        context.LastSeenAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:01:00Z", System.Globalization.CultureInfo.InvariantCulture));
        context.Attributes["test_key"].ShouldBe("test_value");
    }

    /// <summary>When a WatchdogSnapshot is created with alerts, then those alerts are retained.</summary>
    /// <intent>Ensure watchdog snapshots surface alert payloads and timestamps.</intent>
    /// <scenario>Given a WatchdogSnapshot constructed with one ActiveAlert and timestamps.</scenario>
    /// <behavior>Then the snapshot contains the alert and preserves LastScanAt/LastHeartbeatAt.</behavior>
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
                DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse("2024-01-01T00:05:00Z", System.Globalization.CultureInfo.InvariantCulture),
                new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal)),
        };

        // Act
        var snapshot = new WatchdogSnapshot(
            DateTimeOffset.Parse("2024-01-01T00:10:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2024-01-01T00:09:00Z", System.Globalization.CultureInfo.InvariantCulture),
            alerts);

        // Assert
        snapshot.LastScanAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:10:00Z", System.Globalization.CultureInfo.InvariantCulture));
        snapshot.LastHeartbeatAt.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:09:00Z", System.Globalization.CultureInfo.InvariantCulture));
        snapshot.ActiveAlerts.Count.ShouldBe(1);
        snapshot.ActiveAlerts[0].Kind.ShouldBe(WatchdogAlertKind.StuckInbox);
    }
}

