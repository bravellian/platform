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

namespace Bravellian.Platform.Tests;
/// <summary>
/// Unit tests for MetricAggregator.
/// </summary>
public sealed class MetricAggregatorTests
{
    /// <summary>When values are recorded, then the snapshot returns the correct sum and count.</summary>
    /// <intent>Verify aggregation totals across multiple recorded values.</intent>
    /// <scenario>Given a MetricAggregator that records three numeric values.</scenario>
    /// <behavior>Then GetSnapshotAndReset reports sum 60 and count 3.</behavior>
    [Fact]
    public void Aggregator_Should_Calculate_Sum_And_Count()
    {
        var aggregator = new MetricAggregator();

        aggregator.Record(10);
        aggregator.Record(20);
        aggregator.Record(30);

        var snapshot = aggregator.GetSnapshotAndReset();

        Assert.Equal(60, snapshot.Sum);
        Assert.Equal(3, snapshot.Count);
    }

    /// <summary>When values are recorded, then the snapshot reports the minimum and maximum correctly.</summary>
    /// <intent>Ensure min/max tracking reflects the recorded range.</intent>
    /// <scenario>Given a MetricAggregator that records values 5, 15, 3, and 20.</scenario>
    /// <behavior>Then GetSnapshotAndReset returns Min = 3 and Max = 20.</behavior>
    [Fact]
    public void Aggregator_Should_Track_Min_And_Max()
    {
        var aggregator = new MetricAggregator();

        aggregator.Record(5);
        aggregator.Record(15);
        aggregator.Record(3);
        aggregator.Record(20);

        var snapshot = aggregator.GetSnapshotAndReset();

        Assert.Equal(3, snapshot.Min);
        Assert.Equal(20, snapshot.Max);
    }

    /// <summary>When values are recorded, then the snapshot keeps the last recorded value.</summary>
    /// <intent>Verify the aggregator tracks the most recent observation.</intent>
    /// <scenario>Given a MetricAggregator that records 10, 20, then 15.</scenario>
    /// <behavior>Then GetSnapshotAndReset returns Last = 15.</behavior>
    [Fact]
    public void Aggregator_Should_Track_Last_Value()
    {
        var aggregator = new MetricAggregator();

        aggregator.Record(10);
        aggregator.Record(20);
        aggregator.Record(15);

        var snapshot = aggregator.GetSnapshotAndReset();

        Assert.Equal(15, snapshot.Last);
    }

    /// <summary>When a distribution of values is recorded, then percentile estimates are populated and within expected ranges.</summary>
    /// <intent>Validate percentile calculation for a known 1..100 data set.</intent>
    /// <scenario>Given a MetricAggregator that records integers 1 through 100.</scenario>
    /// <behavior>Then P50, P95, and P99 are non-null and fall within expected ranges.</behavior>
    [Fact]
    public void Aggregator_Should_Calculate_Percentiles()
    {
        var aggregator = new MetricAggregator();

        // Record 100 values: 1, 2, 3, ..., 100
        for (int i = 1; i <= 100; i++)
        {
            aggregator.Record(i);
        }

        var snapshot = aggregator.GetSnapshotAndReset();

        Assert.NotNull(snapshot.P50);
        Assert.NotNull(snapshot.P95);
        Assert.NotNull(snapshot.P99);

        // P50 should be around 50
        Assert.InRange(snapshot.P50.Value, 45, 55);

        // P95 should be around 95
        Assert.InRange(snapshot.P95.Value, 90, 100);

        // P99 should be around 99
        Assert.InRange(snapshot.P99.Value, 95, 100);
    }

    /// <summary>When a snapshot is taken, then the aggregator resets its state for subsequent readings.</summary>
    /// <intent>Ensure GetSnapshotAndReset clears accumulated values.</intent>
    /// <scenario>Given a MetricAggregator that records two values and then snapshots twice.</scenario>
    /// <behavior>Then the second snapshot returns zeros and nulls for all aggregates.</behavior>
    [Fact]
    public void Aggregator_Should_Reset_After_Snapshot()
    {
        var aggregator = new MetricAggregator();

        aggregator.Record(10);
        aggregator.Record(20);

        var snapshot1 = aggregator.GetSnapshotAndReset();
        Assert.Equal(30, snapshot1.Sum);
        Assert.Equal(2, snapshot1.Count);

        // After reset, should be empty
        var snapshot2 = aggregator.GetSnapshotAndReset();
        Assert.Equal(0, snapshot2.Sum);
        Assert.Equal(0, snapshot2.Count);
        Assert.Null(snapshot2.Min);
        Assert.Null(snapshot2.Max);
    }

    /// <summary>When no values are recorded, then the snapshot reports zeros and null percentiles.</summary>
    /// <intent>Verify the empty state snapshot uses default aggregate values.</intent>
    /// <scenario>Given a new MetricAggregator with no recorded values.</scenario>
    /// <behavior>Then GetSnapshotAndReset returns zero sum/count and null min/max/percentiles.</behavior>
    [Fact]
    public void Aggregator_Should_Handle_Empty_State()
    {
        var aggregator = new MetricAggregator();

        var snapshot = aggregator.GetSnapshotAndReset();

        Assert.Equal(0, snapshot.Sum);
        Assert.Equal(0, snapshot.Count);
        Assert.Null(snapshot.Min);
        Assert.Null(snapshot.Max);
        Assert.Null(snapshot.P50);
        Assert.Null(snapshot.P95);
        Assert.Null(snapshot.P99);
    }
}
