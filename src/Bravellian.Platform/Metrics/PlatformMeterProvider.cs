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

using System.Diagnostics.Metrics;

namespace Bravellian.Platform.Metrics;

/// <summary>
/// Provides helpers to create meters and common metric instruments.
/// </summary>
public sealed class PlatformMeterProvider
{
    /// <summary>
    /// Initializes a provider that uses the supplied meter factory and options.
    /// </summary>
    /// <param name="meterFactory">The meter factory to create the meter from.</param>
    /// <param name="options">Options describing the meter identity.</param>
    public PlatformMeterProvider(IMeterFactory meterFactory, PlatformMeterOptions options)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(options);
        Meter = meterFactory.Create(options.MeterName, options.MeterVersion);
    }

    /// <summary>
    /// Initializes a provider with a standalone meter.
    /// </summary>
    /// <param name="meterName">The meter name.</param>
    /// <param name="meterVersion">The meter version.</param>
    public PlatformMeterProvider(string meterName, string? meterVersion = null)
    {
        Meter = new Meter(meterName, meterVersion);
    }

    /// <summary>
    /// Gets the underlying meter used to create instruments.
    /// </summary>
    public Meter Meter { get; }

    /// <summary>
    /// Creates a long counter instrument.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="unit">The unit of measure.</param>
    /// <param name="description">The metric description.</param>
    public Counter<long> CreateCounter(string name, string? unit = null, string? description = null)
        => Meter.CreateCounter<long>(name, unit, description);

    /// <summary>
    /// Creates a double counter instrument.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="unit">The unit of measure.</param>
    /// <param name="description">The metric description.</param>
    public Counter<double> CreateCounterDouble(string name, string? unit = null, string? description = null)
        => Meter.CreateCounter<double>(name, unit, description);

    /// <summary>
    /// Creates a long up/down counter instrument.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="unit">The unit of measure.</param>
    /// <param name="description">The metric description.</param>
    public UpDownCounter<long> CreateUpDownCounter(string name, string? unit = null, string? description = null)
        => Meter.CreateUpDownCounter<long>(name, unit, description);

    /// <summary>
    /// Creates a double histogram instrument.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="unit">The unit of measure.</param>
    /// <param name="description">The metric description.</param>
    public Histogram<double> CreateHistogram(string name, string? unit = null, string? description = null)
        => Meter.CreateHistogram<double>(name, unit, description);

    /// <summary>
    /// Creates an observable long gauge instrument.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="observe">The callback that provides the gauge value.</param>
    /// <param name="unit">The unit of measure.</param>
    /// <param name="description">The metric description.</param>
    public ObservableGauge<long> CreateObservableGauge(string name, Func<long> observe, string? unit = null, string? description = null)
        => Meter.CreateObservableGauge(name, observe, unit, description);

    /// <summary>
    /// Creates an observable double gauge instrument.
    /// </summary>
    /// <param name="name">The metric name.</param>
    /// <param name="observe">The callback that provides the gauge value.</param>
    /// <param name="unit">The unit of measure.</param>
    /// <param name="description">The metric description.</param>
    public ObservableGauge<double> CreateObservableGauge(string name, Func<double> observe, string? unit = null, string? description = null)
        => Meter.CreateObservableGauge(name, observe, unit, description);
}
