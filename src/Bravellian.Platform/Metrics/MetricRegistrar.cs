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


using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Metrics;
/// <summary>
/// Default implementation of <see cref="IMetricRegistrar"/>.
/// </summary>
internal sealed class MetricRegistrar : IMetricRegistrar
{
    private readonly ILogger<MetricRegistrar> _logger;
    private readonly ConcurrentDictionary<string, MetricRegistration> _registrations;

    public MetricRegistrar(ILogger<MetricRegistrar> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _registrations = new ConcurrentDictionary<string, MetricRegistration>(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public void Register(MetricRegistration metric)
    {
        ArgumentNullException.ThrowIfNull(metric);

        if (string.IsNullOrWhiteSpace(metric.Name))
        {
            throw new ArgumentException("Metric name cannot be null or empty", nameof(metric));
        }

        if (_registrations.TryAdd(metric.Name, metric))
        {
            _logger.LogInformation("Registered metric {MetricName} with tags: {Tags}", metric.Name, string.Join(", ", metric.AllowedTags));
        }
        else
        {
            _logger.LogWarning("Metric {MetricName} is already registered", metric.Name);
        }
    }

    /// <inheritdoc/>
    public void RegisterRange(IEnumerable<MetricRegistration> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        foreach (var metric in metrics)
        {
            Register(metric);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<MetricRegistration> GetAll()
    {
        return _registrations.Values.ToList();
    }

    /// <summary>
    /// Checks if a tag is allowed for a specific metric.
    /// </summary>
    /// <param name="metricName">The metric name.</param>
    /// <param name="tagKey">The tag key to check.</param>
    /// <returns>True if the tag is allowed, false otherwise.</returns>
    internal bool IsTagAllowed(string metricName, string tagKey)
    {
        if (_registrations.TryGetValue(metricName, out var registration))
        {
            return registration.AllowedTags.Contains(tagKey, StringComparer.Ordinal);
        }

        // If metric not registered, fall back to default allowed tags
        return false;
    }
}
