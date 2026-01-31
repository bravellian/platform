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


using System;
using Bravellian.Platform.Metrics;

namespace Bravellian.Platform.Tests;

public class PlatformMetricCatalogTests
{
    /// <summary>
    /// When the platform metric catalog is enumerated, then it returns a non-empty list.
    /// </summary>
    /// <intent>
    /// Ensure the catalog exposes at least one metric.
    /// </intent>
    /// <scenario>
    /// Given PlatformMetricCatalog.All is accessed without additional setup.
    /// </scenario>
    /// <behavior>
    /// Then the returned list is not null and contains entries.
    /// </behavior>
    [Fact]
    public void All_ReturnsNonEmptyList()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;

        // Assert
        metrics.ShouldNotBeNull();
        metrics.Count.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// When the platform metric catalog is enumerated, then it contains the expected outbox metrics.
    /// </summary>
    /// <intent>
    /// Verify that outbox metrics are registered in the catalog.
    /// </intent>
    /// <scenario>
    /// Given PlatformMetricCatalog.All is evaluated.
    /// </scenario>
    /// <behavior>
    /// Then the list includes outbox published, pending, oldest age, and latency metrics.
    /// </behavior>
    [Fact]
    public void All_ContainsOutboxMetrics()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;

        // Assert
        metrics.ShouldContain(m => m.Name == "outbox.published.count");
        metrics.ShouldContain(m => m.Name == "outbox.pending.count");
        metrics.ShouldContain(m => m.Name == "outbox.oldest_age.seconds");
        metrics.ShouldContain(m => m.Name == "outbox.publish_latency.ms");
    }

    /// <summary>
    /// When the platform metric catalog is enumerated, then it contains the expected inbox metrics.
    /// </summary>
    /// <intent>
    /// Verify that inbox metrics are registered in the catalog.
    /// </intent>
    /// <scenario>
    /// Given PlatformMetricCatalog.All is evaluated.
    /// </scenario>
    /// <behavior>
    /// Then the list includes inbox processed, retry, failed, and processing latency metrics.
    /// </behavior>
    [Fact]
    public void All_ContainsInboxMetrics()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;

        // Assert
        metrics.ShouldContain(m => m.Name == "inbox.processed.count");
        metrics.ShouldContain(m => m.Name == "inbox.retry.count");
        metrics.ShouldContain(m => m.Name == "inbox.failed.count");
        metrics.ShouldContain(m => m.Name == "inbox.processing_latency.ms");
    }

    /// <summary>
    /// When the platform metric catalog is enumerated, then it contains the expected DLQ metrics.
    /// </summary>
    /// <intent>
    /// Verify that dead-letter queue metrics are registered in the catalog.
    /// </intent>
    /// <scenario>
    /// Given PlatformMetricCatalog.All is evaluated.
    /// </scenario>
    /// <behavior>
    /// Then the list includes DLQ depth and oldest age metrics.
    /// </behavior>
    [Fact]
    public void All_ContainsDlqMetrics()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;

        // Assert
        metrics.ShouldContain(m => m.Name == "dlq.depth");
        metrics.ShouldContain(m => m.Name == "dlq.oldest_age.seconds");
    }

    /// <summary>
    /// When the platform metric catalog is enumerated, then each metric has required metadata populated.
    /// </summary>
    /// <intent>
    /// Ensure metric definitions have valid names, units, descriptions, tags, and aggregation kinds.
    /// </intent>
    /// <scenario>
    /// Given PlatformMetricCatalog.All is evaluated.
    /// </scenario>
    /// <behavior>
    /// Then each metric has non-empty name/unit/description, non-null tags, and a defined aggregation kind.
    /// </behavior>
    [Fact]
    public void All_MetricsHaveValidProperties()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;

        // Assert
        foreach (var metric in metrics)
        {
            metric.Name.ShouldNotBeNullOrEmpty();
            metric.Unit.ShouldNotBeNullOrEmpty();
            metric.Description.ShouldNotBeNullOrEmpty();
            metric.AllowedTags.ShouldNotBeNull();

            // Verify AggKind is a valid enum value
            Enum.IsDefined<MetricAggregationKind>(metric.AggKind).ShouldBeTrue();
        }
    }

    /// <summary>
    /// When counter metrics are selected from the catalog, then their units are count except for latency or age metrics.
    /// </summary>
    /// <intent>
    /// Validate unit conventions for counter metrics.
    /// </intent>
    /// <scenario>
    /// Given PlatformMetricCatalog.All is filtered to counter aggregation metrics.
    /// </scenario>
    /// <behavior>
    /// Then non-latency and non-age counters use the Count unit.
    /// </behavior>
    [Fact]
    public void All_CounterMetricsHaveCountUnit()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;
        var counterMetrics = metrics.Where(m => m.AggKind == MetricAggregationKind.Counter).ToList();

        // Assert
        counterMetrics.ShouldNotBeEmpty();
        foreach (var metric in counterMetrics)
        {
            // Most counters should have "count" as unit
            if (!metric.Name.Contains("latency", StringComparison.Ordinal)
                && !metric.Name.Contains("age", StringComparison.Ordinal))
            {
                metric.Unit.ShouldBe(MetricUnit.Count);
            }
        }
    }

    /// <summary>
    /// When histogram metrics are selected from the catalog, then their units are time-based.
    /// </summary>
    /// <intent>
    /// Ensure histogram metrics report timing in milliseconds or seconds.
    /// </intent>
    /// <scenario>
    /// Given PlatformMetricCatalog.All is filtered to histogram aggregation metrics.
    /// </scenario>
    /// <behavior>
    /// Then each histogram metric uses Milliseconds or Seconds units.
    /// </behavior>
    [Fact]
    public void All_HistogramMetricsHaveTimeUnits()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;
        var histogramMetrics = metrics.Where(m => m.AggKind == MetricAggregationKind.Histogram).ToList();

        // Assert
        histogramMetrics.ShouldNotBeEmpty();
        foreach (var metric in histogramMetrics)
        {
            // Histograms should typically measure time
            metric.Unit.ShouldBeOneOf(MetricUnit.Milliseconds, MetricUnit.Seconds);
        }
    }
}

