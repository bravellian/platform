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

using Bravellian.Platform.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

public class MetricRegistrarTests
{
    [Fact]
    public void Register_WithValidMetric_AddsToRegistry()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metric = new MetricRegistration(
            "test.metric",
            "count",
            "counter",
            "Test metric",
            new[] { "tag1", "tag2" });

        // Act
        registrar.Register(metric);
        var all = registrar.GetAll();

        // Assert
        all.ShouldContain(m => m.Name == "test.metric");
        all.Count.ShouldBe(1);
    }

    [Fact]
    public void Register_WithDuplicateMetric_LogsWarning()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metric = new MetricRegistration(
            "test.metric",
            "count",
            "counter",
            "Test metric",
            new[] { "tag1" });

        // Act
        registrar.Register(metric);
        registrar.Register(metric); // Should log warning but not throw

        // Assert
        var all = registrar.GetAll();
        all.Count.ShouldBe(1);
    }

    [Fact]
    public void RegisterRange_WithMultipleMetrics_AddsAllToRegistry()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metrics = new[]
        {
            new MetricRegistration("metric1", "count", "counter", "Metric 1", new[] { "tag1" }),
            new MetricRegistration("metric2", "ms", "histogram", "Metric 2", new[] { "tag2" }),
            new MetricRegistration("metric3", "count", "gauge", "Metric 3", new[] { "tag3" }),
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

    [Fact]
    public void IsTagAllowed_WithRegisteredMetricAndAllowedTag_ReturnsTrue()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metric = new MetricRegistration(
            "test.metric",
            "count",
            "counter",
            "Test metric",
            new[] { "allowed_tag" });
        registrar.Register(metric);

        // Act
        var result = registrar.IsTagAllowed("test.metric", "allowed_tag");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsTagAllowed_WithRegisteredMetricAndDisallowedTag_ReturnsFalse()
    {
        // Arrange
        var logger = new NullLogger<MetricRegistrar>();
        var registrar = new MetricRegistrar(logger);
        var metric = new MetricRegistration(
            "test.metric",
            "count",
            "counter",
            "Test metric",
            new[] { "allowed_tag" });
        registrar.Register(metric);

        // Act
        var result = registrar.IsTagAllowed("test.metric", "disallowed_tag");

        // Assert
        result.ShouldBeFalse();
    }

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
