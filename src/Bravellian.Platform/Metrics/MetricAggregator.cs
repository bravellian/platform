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

namespace Bravellian.Platform.Metrics;
/// <summary>
/// Aggregates metric values for a time window.
/// </summary>
internal sealed class MetricAggregator
{
    private readonly Lock _lock = new();
    private readonly List<double> _reservoir;
    private readonly int _reservoirSize;

    private double _sum;
    private int _count;
    private double _min = double.MaxValue;
    private double _max = double.MinValue;
    private double _last;

    public MetricAggregator(int reservoirSize = 1000)
    {
        _reservoirSize = reservoirSize;
        _reservoir = new List<double>(reservoirSize);
    }

    /// <summary>
    /// Records a value.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Reservoir sampling uses non-cryptographic randomness.")]
    public void Record(double value)
    {
        lock (_lock)
        {
            _sum += value;
            _count++;
            _last = value;

            if (value < _min)
            {
                _min = value;
            }

            if (value > _max)
            {
                _max = value;
            }

            // Add to reservoir for percentile calculation using standard Algorithm R
            if (_reservoir.Count < _reservoirSize)
            {
                _reservoir.Add(value);
            }
            else
            {
                // Standard Algorithm R for reservoir sampling
                var randomIndex = Random.Shared.Next(_count);
                if (randomIndex < _reservoirSize)
                {
                    _reservoir[randomIndex] = value;
                }
            }
        }
    }

    /// <summary>
    /// Gets the aggregated snapshot and resets the aggregator.
    /// </summary>
    public MetricSnapshot GetSnapshotAndReset()
    {
        lock (_lock)
        {
            // Sort reservoir once for all percentile calculations
            List<double>? sorted = null;
            if (_reservoir.Count > 0)
            {
                sorted = _reservoir.OrderBy(x => x).ToList();
            }

            var snapshot = new MetricSnapshot
            {
                Sum = _sum,
                Count = _count,
                Min = _count > 0 ? _min : null,
                Max = _count > 0 ? _max : null,
                Last = _count > 0 ? _last : null,
                P50 = CalculatePercentile(sorted, 0.50),
                P95 = CalculatePercentile(sorted, 0.95),
                P99 = CalculatePercentile(sorted, 0.99),
            };

            // Reset
            _sum = 0;
            _count = 0;
            _min = double.MaxValue;
            _max = double.MinValue;
            _last = 0;
            _reservoir.Clear();

            return snapshot;
        }
    }

    private static double? CalculatePercentile(List<double>? sorted, double percentile)
    {
        if (sorted == null || sorted.Count == 0)
        {
            return null;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));
        return sorted[index];
    }
}

