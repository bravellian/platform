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


using Bravellian.Platform.Metrics;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CA1861
namespace Bravellian.Platform.Tests;

public class MetricRegistrarTests
{
    /// <summary>When a valid metric is registered, then it appears in the registry.</summary>
    /// <intent>Verify MetricRegistrar stores new metric registrations.</intent>
    /// <scenario>Given a MetricRegistrar and a MetricRegistration for "test.metric".</scenario>
    /// <behavior>Then GetAll returns a single entry that matches the registered metric.</behavior>
    [Fact]
    public void Register_WithValidMetric_AddsToRegistry()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metric = new MetricRegistration(
            "test.metric",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Test metric",
            new[] { "tag1", "tag2" });

        // Act
        registrar.Register(metric);
        var all = registrar.GetAll();

        // Assert
        all.ShouldContain(m => m.Name == "test.metric");
        all.Count.ShouldBe(1);
    }

    /// <summary>When the same metric is registered twice, then the registry keeps a single entry.</summary>
    /// <intent>Ensure duplicate registration does not create duplicate entries.</intent>
    /// <scenario>Given a MetricRegistrar that registers the same metric twice.</scenario>
    /// <behavior>Then GetAll returns one registration for the metric.</behavior>
    [Fact]
    public void Register_WithDuplicateMetric_LogsWarning()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metric = new MetricRegistration(
            "test.metric",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Test metric",
            new[] { "tag1" });

        // Act
        registrar.Register(metric);
        registrar.Register(metric); // Should log warning but not throw

        // Assert
        var all = registrar.GetAll();
        all.Count.ShouldBe(1);
    }

    /// <summary>When multiple metrics are registered in bulk, then all of them appear in the registry.</summary>
    /// <intent>Verify RegisterRange registers each provided metric.</intent>
    /// <scenario>Given three MetricRegistration instances passed to RegisterRange.</scenario>
    /// <behavior>Then GetAll returns three entries matching the metric names.</behavior>
    [Fact]
    public void RegisterRange_WithMultipleMetrics_AddsAllToRegistry()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metrics = new[]
        {
            new MetricRegistration("metric1", MetricUnit.Count, MetricAggregationKind.Counter, "Metric 1", new[] { "tag1" }),
            new MetricRegistration("metric2", MetricUnit.Milliseconds, MetricAggregationKind.Histogram, "Metric 2", new[] { "tag2" }),
            new MetricRegistration("metric3", MetricUnit.Count, MetricAggregationKind.Gauge, "Metric 3", new[] { "tag3" }),
        };

        // Act
        registrar.RegisterRange(metrics);
        var all = registrar.GetAll();

        // Assert
        all.Count.ShouldBe(3);
        all.ShouldContain(m => m.Name == "metric1");
        all.ShouldContain(m => m.Name == "metric2");
        all.ShouldContain(m => m.Name == "metric3");
    }

    /// <summary>When a registered metric allows a tag, then IsTagAllowed returns true.</summary>
    /// <intent>Confirm allowed tags are honored for registered metrics.</intent>
    /// <scenario>Given a registered metric that includes the "allowed_tag" value.</scenario>
    /// <behavior>Then IsTagAllowed returns true for that tag.</behavior>
    [Fact]
    public void IsTagAllowed_WithRegisteredMetricAndAllowedTag_ReturnsTrue()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metric = new MetricRegistration(
            "test.metric",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Test metric",
            new[] { "allowed_tag" });
        registrar.Register(metric);

        // Act
        var result = registrar.IsTagAllowed("test.metric", "allowed_tag");

        // Assert
        result.ShouldBeTrue();
    }

    /// <summary>When a registered metric does not allow a tag, then IsTagAllowed returns false.</summary>
    /// <intent>Ensure disallowed tags are rejected even for registered metrics.</intent>
    /// <scenario>Given a registered metric that only allows "allowed_tag".</scenario>
    /// <behavior>Then IsTagAllowed returns false for a different tag value.</behavior>
    [Fact]
    public void IsTagAllowed_WithRegisteredMetricAndDisallowedTag_ReturnsFalse()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metric = new MetricRegistration(
            "test.metric",
            MetricUnit.Count,
            MetricAggregationKind.Counter,
            "Test metric",
            new[] { "allowed_tag" });
        registrar.Register(metric);

        // Act
        var result = registrar.IsTagAllowed("test.metric", "disallowed_tag");

        // Assert
        result.ShouldBeFalse();
    }

    /// <summary>When a metric is not registered, then IsTagAllowed returns false for any tag.</summary>
    /// <intent>Verify tag checks fail for unknown metrics.</intent>
    /// <scenario>Given a MetricRegistrar with no registration for "unknown.metric".</scenario>
    /// <behavior>Then IsTagAllowed returns false for the requested tag.</behavior>
    [Fact]
    public void IsTagAllowed_WithUnregisteredMetric_ReturnsFalse()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);

        // Act
        var result = registrar.IsTagAllowed("unknown.metric", "some_tag");

        // Assert
        result.ShouldBeFalse();
    }
}
#pragma warning restore CA1861

