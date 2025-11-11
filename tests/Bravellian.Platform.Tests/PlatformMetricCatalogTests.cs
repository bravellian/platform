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
using Shouldly;
using Xunit;

public class PlatformMetricCatalogTests
{
    [Fact]
    public void All_ReturnsNonEmptyList()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;

        // Assert
        metrics.ShouldNotBeNull();
        metrics.Count.ShouldBeGreaterThan(0);
    }

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

    [Fact]
    public void All_ContainsDlqMetrics()
    {
        // Act
        var metrics = PlatformMetricCatalog.All;

        // Assert
        metrics.ShouldContain(m => m.Name == "dlq.depth");
        metrics.ShouldContain(m => m.Name == "dlq.oldest_age.seconds");
    }

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
            Enum.IsDefined(typeof(MetricAggregationKind), metric.AggKind).ShouldBeTrue();
        }
    }

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
            if (!metric.Name.Contains("latency") && !metric.Name.Contains("age"))
            {
                metric.Unit.ShouldBe(MetricUnit.Count);
            }
        }
    }

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
