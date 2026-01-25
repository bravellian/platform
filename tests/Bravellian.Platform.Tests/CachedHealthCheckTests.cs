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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Bravellian.Platform.Tests;

public class CachedHealthCheckTests
{
    private static readonly DateTimeOffset TestStartTime = DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// When a healthy result is cached, then repeated checks within the cache window reuse the result.
    /// </summary>
    /// <intent>
    /// Verify healthy results are cached for the configured duration.
    /// </intent>
    /// <scenario>
    /// Given a CachedHealthCheck with a FakeTimeProvider and a CountingHealthCheck returning Healthy.
    /// </scenario>
    /// <behavior>
    /// Then the inner check runs twice across three calls separated by time advances.
    /// </behavior>
    [Fact]
    public async Task CachesHealthyResults_UntilDurationExpires()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(TestStartTime);
        var inner = new CountingHealthCheck(HealthStatus.Healthy);
        var options = new CachedHealthCheckOptions
        {
            HealthyCacheDuration = TimeSpan.FromMinutes(1),
            DegradedCacheDuration = TimeSpan.Zero,
            UnhealthyCacheDuration = TimeSpan.Zero,
        };
        var cached = new CachedHealthCheck(inner, options, fakeTime);

        // Act
        await cached.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        fakeTime.Advance(TimeSpan.FromSeconds(30));
        await cached.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        fakeTime.Advance(TimeSpan.FromMinutes(1));
        await cached.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        inner.InvocationCount.ShouldBe(2);
    }

    /// <summary>
    /// When an unhealthy result is returned with zero unhealthy cache duration, then the next check re-evaluates immediately.
    /// </summary>
    /// <intent>
    /// Ensure unhealthy results do not get cached when configured for immediate rechecks.
    /// </intent>
    /// <scenario>
    /// Given a SequenceHealthCheck that returns Unhealthy then Healthy with UnhealthyCacheDuration set to zero.
    /// </scenario>
    /// <behavior>
    /// Then the second call invokes the inner check again and returns Healthy.
    /// </behavior>
    [Fact]
    public async Task RechecksImmediately_WhenUnhealthy()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(TestStartTime);
        var inner = new SequenceHealthCheck([
            HealthCheckResult.Unhealthy("down"),
            HealthCheckResult.Healthy("recovered"),
        ]);
        var options = new CachedHealthCheckOptions
        {
            HealthyCacheDuration = TimeSpan.FromMinutes(1),
            DegradedCacheDuration = TimeSpan.FromMinutes(1),
            UnhealthyCacheDuration = TimeSpan.Zero,
        };
        var cached = new CachedHealthCheck(inner, options, fakeTime);

        // Act
        var first = await cached.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        var second = await cached.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        first.Status.ShouldBe(HealthStatus.Unhealthy);
        second.Status.ShouldBe(HealthStatus.Healthy);
        inner.InvocationCount.ShouldBe(2);
    }

    /// <summary>
    /// When a degraded result is cached, then repeated checks within the degraded cache window reuse the result.
    /// </summary>
    /// <intent>
    /// Verify degraded results are cached for the configured duration.
    /// </intent>
    /// <scenario>
    /// Given a CachedHealthCheck with a FakeTimeProvider and a CountingHealthCheck returning Degraded.
    /// </scenario>
    /// <behavior>
    /// Then the inner check runs twice across three calls as time advances past the cache duration.
    /// </behavior>
    [Fact]
    public async Task CachesDegradedResults_UntilDurationExpires()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(TestStartTime);
        var inner = new CountingHealthCheck(HealthStatus.Degraded);
        var options = new CachedHealthCheckOptions
        {
            HealthyCacheDuration = TimeSpan.FromMinutes(1),
            DegradedCacheDuration = TimeSpan.FromSeconds(30),
            UnhealthyCacheDuration = TimeSpan.Zero,
        };
        var cached = new CachedHealthCheck(inner, options, fakeTime);

        // Act
        await cached.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        fakeTime.Advance(TimeSpan.FromSeconds(15));
        await cached.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        fakeTime.Advance(TimeSpan.FromSeconds(20));
        await cached.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        inner.InvocationCount.ShouldBe(2);
    }

    /// <summary>
    /// When a cached check is registered with per-name options, then the configured cache durations are honored.
    /// </summary>
    /// <intent>
    /// Ensure AddCachedCheck applies custom options for the named check.
    /// </intent>
    /// <scenario>
    /// Given a ServiceCollection with a FakeTimeProvider and a CountingHealthCheck registered via AddCachedCheck.
    /// </scenario>
    /// <behavior>
    /// Then repeated health checks within the cache window do not re-invoke the inner check.</behavior>
    [Fact]
    public async Task BuilderRegistersCachedCheck_WithOptionsPerName()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(TestStartTime);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fakeTime);
        services.AddSingleton<CountingHealthCheck>(new CountingHealthCheck(HealthStatus.Healthy));

        services
            .AddHealthChecks()
            .AddCachedCheck<CountingHealthCheck>(
                "cached",
                options =>
                {
                    options.HealthyCacheDuration = TimeSpan.FromMinutes(5);
                    options.DegradedCacheDuration = TimeSpan.FromSeconds(5);
                });

        var provider = services.BuildServiceProvider();
        var healthService = provider.GetRequiredService<HealthCheckService>();
        var inner = provider.GetRequiredService<CountingHealthCheck>();

        // Act
        await healthService.CheckHealthAsync(TestContext.Current.CancellationToken);
        fakeTime.Advance(TimeSpan.FromSeconds(30));
        await healthService.CheckHealthAsync(TestContext.Current.CancellationToken);
        fakeTime.Advance(TimeSpan.FromMinutes(10));
        await healthService.CheckHealthAsync(TestContext.Current.CancellationToken);

        // Assert
        inner.InvocationCount.ShouldBe(2);
    }

    /// <summary>
    /// When a delegate-based cached check is registered, then caching reduces delegate invocations.
    /// </summary>
    /// <intent>
    /// Validate AddCachedCheck with a delegate honors the healthy cache duration.</intent>
    /// <scenario>
    /// Given a cached check delegate that increments a counter and a FakeTimeProvider.</scenario>
    /// <behavior>
    /// Then three health checks result in two delegate invocations after time advances.</behavior>
    [Fact]
    public async Task BuilderRegistersDelegateBasedCachedCheck_WithCaching()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(TestStartTime);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fakeTime);

        var invocationCount = 0;
        services
            .AddHealthChecks()
            .AddCachedCheck(
                "delegateCheck",
                (sp, ct) =>
                {
                    invocationCount++;
                    return Task.FromResult(HealthCheckResult.Healthy("ok"));
                },
                options =>
                {
                    options.HealthyCacheDuration = TimeSpan.FromMinutes(1);
                });

        var provider = services.BuildServiceProvider();
        var healthService = provider.GetRequiredService<HealthCheckService>();

        // Act
        await healthService.CheckHealthAsync(TestContext.Current.CancellationToken);
        fakeTime.Advance(TimeSpan.FromSeconds(30));
        await healthService.CheckHealthAsync(TestContext.Current.CancellationToken);
        fakeTime.Advance(TimeSpan.FromMinutes(1));
        await healthService.CheckHealthAsync(TestContext.Current.CancellationToken);

        // Assert
        invocationCount.ShouldBe(2);
    }

    /// <summary>
    /// When HealthyCacheDuration is negative, then validation fails.</summary>
    /// <intent>Enforce non-negative healthy cache durations.</intent>
    /// <scenario>Given CachedHealthCheckOptions with a negative HealthyCacheDuration.</scenario>
    /// <behavior>Then validation fails and references HealthyCacheDuration in the message.</behavior>
    [Fact]
    public void OptionsValidator_RejectsNegativeHealthyCacheDuration()
    {
        // Arrange
        var validator = new CachedHealthCheckOptionsValidator();
        var options = new CachedHealthCheckOptions
        {
            HealthyCacheDuration = TimeSpan.FromSeconds(-1),
        };

        // Act
        var result = validator.Validate("test", options);

        // Assert
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("HealthyCacheDuration");
    }

    /// <summary>
    /// When DegradedCacheDuration is negative, then validation fails.</summary>
    /// <intent>Enforce non-negative degraded cache durations.</intent>
    /// <scenario>Given CachedHealthCheckOptions with a negative DegradedCacheDuration.</scenario>
    /// <behavior>Then validation fails and references DegradedCacheDuration in the message.</behavior>
    [Fact]
    public void OptionsValidator_RejectsNegativeDegradedCacheDuration()
    {
        // Arrange
        var validator = new CachedHealthCheckOptionsValidator();
        var options = new CachedHealthCheckOptions
        {
            DegradedCacheDuration = TimeSpan.FromSeconds(-1),
        };

        // Act
        var result = validator.Validate("test", options);

        // Assert
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DegradedCacheDuration");
    }

    /// <summary>
    /// When UnhealthyCacheDuration is negative, then validation fails.</summary>
    /// <intent>Enforce non-negative unhealthy cache durations.</intent>
    /// <scenario>Given CachedHealthCheckOptions with a negative UnhealthyCacheDuration.</scenario>
    /// <behavior>Then validation fails and references UnhealthyCacheDuration in the message.</behavior>
    [Fact]
    public void OptionsValidator_RejectsNegativeUnhealthyCacheDuration()
    {
        // Arrange
        var validator = new CachedHealthCheckOptionsValidator();
        var options = new CachedHealthCheckOptions
        {
            UnhealthyCacheDuration = TimeSpan.FromSeconds(-1),
        };

        // Act
        var result = validator.Validate("test", options);

        // Assert
        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("UnhealthyCacheDuration");
    }

    /// <summary>
    /// When cache durations are non-negative, then validation succeeds.</summary>
    /// <intent>Confirm valid CachedHealthCheckOptions pass validation.</intent>
    /// <scenario>Given options with positive healthy/degraded durations and zero unhealthy duration.</scenario>
    /// <behavior>Then validation succeeds.</behavior>
    [Fact]
    public void OptionsValidator_AcceptsValidOptions()
    {
        // Arrange
        var validator = new CachedHealthCheckOptionsValidator();
        var options = new CachedHealthCheckOptions
        {
            HealthyCacheDuration = TimeSpan.FromMinutes(1),
            DegradedCacheDuration = TimeSpan.FromSeconds(30),
            UnhealthyCacheDuration = TimeSpan.Zero,
        };

        // Act
        var result = validator.Validate("test", options);

        // Assert
        result.Succeeded.ShouldBeTrue();
    }

    private sealed class CountingHealthCheck : IHealthCheck
    {
        private readonly HealthStatus status;

        public CountingHealthCheck(HealthStatus status)
        {
            this.status = status;
        }

        public int InvocationCount { get; private set; }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(new HealthCheckResult(status));
        }
    }

    private sealed class SequenceHealthCheck : IHealthCheck
    {
        private readonly Queue<HealthCheckResult> results;

        public SequenceHealthCheck(IEnumerable<HealthCheckResult> results)
        {
            this.results = new Queue<HealthCheckResult>(results);
        }

        public int InvocationCount { get; private set; }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(results.TryDequeue(out var result) ? result : HealthCheckResult.Healthy());
        }
    }
}
