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
