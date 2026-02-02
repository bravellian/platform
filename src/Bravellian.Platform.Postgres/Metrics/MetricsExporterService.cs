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
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Metrics;
/// <summary>
/// Background service that exports metrics to SQL Server databases.
/// </summary>
internal sealed class MetricsExporterService : BackgroundService
{
    private readonly ILogger<MetricsExporterService> _logger;
    private readonly PostgresMetricsExporterOptions _options;
    private readonly IPlatformDatabaseDiscovery _databaseDiscovery;
    private readonly MetricRegistrar? _metricRegistrar;
    private readonly MeterListener _listener;
    private readonly Guid _instanceId;
    private readonly ConcurrentDictionary<string, MetricDefinition> _metricDefinitions;
    private readonly ConcurrentDictionary<MetricSeriesKey, MetricAggregator> _minuteAggregators;
    private readonly ConcurrentDictionary<MetricSeriesKey, MetricAggregator> _hourlyAggregators;
    private DateTime? _lastFlushUtc;
    private DateTime? _lastHourlyFlushUtc;
    private string? _lastError;

    public MetricsExporterService(
        ILogger<MetricsExporterService> logger,
        IOptions<PostgresMetricsExporterOptions> options,
        IPlatformDatabaseDiscovery databaseDiscovery,
        MetricRegistrar metricRegistrar)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(databaseDiscovery);
        _logger = logger;
        _options = options.Value;
        _databaseDiscovery = databaseDiscovery;
        _metricRegistrar = metricRegistrar ?? throw new ArgumentNullException(nameof(metricRegistrar));
        _instanceId = Guid.NewGuid();
        _metricDefinitions = new ConcurrentDictionary<string, MetricDefinition>(StringComparer.Ordinal);
        _minuteAggregators = new ConcurrentDictionary<MetricSeriesKey, MetricAggregator>();
        _hourlyAggregators = new ConcurrentDictionary<MetricSeriesKey, MetricAggregator>();

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                // Only subscribe to Bravellian.Platform meters
                if (instrument.Meter.Name.StartsWith("Bravellian.Platform", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        _listener.SetMeasurementEventCallback<double>(OnMeasurementRecorded);
        _listener.SetMeasurementEventCallback<long>(OnMeasurementRecorded);
        _listener.SetMeasurementEventCallback<int>(OnMeasurementRecorded);
    }

    /// <summary>
    /// Gets the last flush time (UTC).
    /// </summary>
    public DateTime? LastFlushUtc => _lastFlushUtc;

    /// <summary>
    /// Gets the last error message.
    /// </summary>
    public string? LastError => _lastError;

    public override void Dispose()
    {
        _listener.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Metrics exporter is disabled");
            return;
        }

        _listener.Start();
        _logger.LogInformation("Metrics exporter started with instance ID {InstanceId}", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.FlushInterval, stoppingToken).ConfigureAwait(false);
                await FlushMinuteMetricsAsync(stoppingToken).ConfigureAwait(false);
                await CheckHourlyFlushAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogError(ex, "Error during metrics flush");
            }
        }

        _listener.Dispose();
        _logger.LogInformation("Metrics exporter stopped");
    }

    private void OnMeasurementRecorded<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        where T : struct
    {
        try
        {
            var value = Convert.ToDouble(measurement, System.Globalization.CultureInfo.InvariantCulture);
            var metricName = instrument.Name;
            var unit = instrument.Unit ?? "count";

            // Extract database ID and service from tags
            Guid? databaseId = null;
            var service = _options.ServiceName;
            var filteredTags = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                if (tag.Key.Equals("database_id", StringComparison.Ordinal) && tag.Value is Guid guid)
                {
                    databaseId = guid;
                }
                else if (tag.Key.Equals("service", StringComparison.Ordinal) && tag.Value is string svc)
                {
                    service = svc;
                }
                else if (tag.Value is string strValue && IsAllowedTag(metricName, tag.Key))
                {
                    filteredTags[tag.Key] = strValue;
                }
            }

            var seriesKey = new MetricSeriesKey
            {
                MetricName = metricName,
                DatabaseId = databaseId,
                Service = service,
                InstanceId = _instanceId,
                Tags = filteredTags,
            };

            // Determine aggregation kind
            var aggKind = DetermineAggregationKind(instrument);

            // Store metric definition
            _metricDefinitions.TryAdd(metricName, new MetricDefinition
            {
                Name = metricName,
                Unit = unit,
                AggKind = aggKind,
                Description = instrument.Description ?? metricName,
            });

            // Record to minute aggregator
            var minuteAggregator = _minuteAggregators.GetOrAdd(seriesKey, _ => new MetricAggregator(_options.ReservoirSize));
            minuteAggregator.Record(value);

            // Record to hourly aggregator if enabled
            if (_options.EnableCentralRollup && !string.IsNullOrEmpty(_options.CentralConnectionString))
            {
                if (databaseId is null)
                {
                    _logger.LogWarning(
                        "Skipping central rollup for metric {MetricName} because database_id tag is missing.",
                        metricName);
                }
                else
                {
                    var hourlyAggregator = _hourlyAggregators.GetOrAdd(seriesKey, _ => new MetricAggregator(_options.ReservoirSize));
                    hourlyAggregator.Record(value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error recording measurement for {InstrumentName}", instrument.Name);
        }
    }

    private static MetricAggregationKind DetermineAggregationKind(Instrument instrument)
    {
        return instrument switch
        {
            Counter<long> => MetricAggregationKind.Counter,
            Counter<int> => MetricAggregationKind.Counter,
            Counter<double> => MetricAggregationKind.Counter,
            ObservableCounter<long> => MetricAggregationKind.Counter,
            ObservableCounter<int> => MetricAggregationKind.Counter,
            ObservableCounter<double> => MetricAggregationKind.Counter,
            Histogram<long> => MetricAggregationKind.Histogram,
            Histogram<int> => MetricAggregationKind.Histogram,
            Histogram<double> => MetricAggregationKind.Histogram,
            _ => MetricAggregationKind.Gauge,
        };
    }

    private bool IsAllowedTag(string metricName, string tagKey)
    {
        // First check if the metric has specific allowed tags via registrar
        if (_metricRegistrar != null && _metricRegistrar.IsTagAllowed(metricName, tagKey))
        {
            return true;
        }

        // Fall back to global allowed tags
        return _options.GlobalAllowedTags.Contains(tagKey);
    }

    private async Task FlushMinuteMetricsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var bucketStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

        var databases = await _databaseDiscovery.DiscoverDatabasesAsync(cancellationToken).ConfigureAwait(false);

        // Group aggregators by database
        var aggregatorsByDatabase = _minuteAggregators
            .GroupBy(kvp => kvp.Key.DatabaseId)
            .ToList();

        foreach (var databaseGroup in aggregatorsByDatabase)
        {
            var databaseId = databaseGroup.Key;

            // For now, write to the first database (or a default database)
            // In a multi-database setup, you'd map databaseId to the appropriate database
            var targetDb = databases.FirstOrDefault();
            if (targetDb == null || string.IsNullOrEmpty(targetDb.ConnectionString))
            {
                continue;
            }

            await FlushDatabaseGroupAsync(databaseGroup, targetDb.ConnectionString, bucketStart, cancellationToken).ConfigureAwait(false);
        }

        _lastFlushUtc = DateTime.UtcNow;

        await UpdateHeartbeatAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task FlushDatabaseGroupAsync(
        IGrouping<Guid?, KeyValuePair<MetricSeriesKey, MetricAggregator>> databaseGroup,
        string connectionString,
        DateTime bucketStart,
        CancellationToken cancellationToken)
    {
        foreach (var kvp in databaseGroup)
        {
            var seriesKey = kvp.Key;
            var aggregator = kvp.Value;
            var snapshot = aggregator.GetSnapshotAndReset();

            if (snapshot.Count == 0)
            {
                continue; // Skip empty snapshots
            }

            try
            {
                if (_metricDefinitions.TryGetValue(seriesKey.MetricName, out var metricDef))
                {
                    await PostgresMetricsWriter.WriteMinutePointAsync(
                        connectionString,
                        _options.SchemaName,
                        seriesKey,
                        snapshot,
                        bucketStart,
                        metricDef.Unit,
                        metricDef.AggKind.ToString().ToUpperInvariant(),
                        metricDef.Description,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error writing minute metric for series {MetricName}", seriesKey.MetricName);
            }
        }
    }

    private async Task UpdateHeartbeatAsync(CancellationToken cancellationToken)
    {
        // Update heartbeat in central DB if configured
        if (!string.IsNullOrEmpty(_options.CentralConnectionString))
        {
            try
            {
                await PostgresMetricsWriter.UpdateHeartbeatAsync(
                    _options.CentralConnectionString,
                    _options.SchemaName,
                    _instanceId.ToString(),
                    _lastFlushUtc!.Value,
                    _lastError,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating heartbeat");
            }
        }
    }

    private async Task CheckHourlyFlushAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableCentralRollup || string.IsNullOrEmpty(_options.CentralConnectionString))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

        // Flush once per hour when we cross into a new hour
        if (_lastHourlyFlushUtc == null || _lastHourlyFlushUtc.Value < currentHour)
        {
            await FlushHourlyMetricsAsync(cancellationToken).ConfigureAwait(false);
            _lastHourlyFlushUtc = currentHour;
        }
    }

    private async Task FlushHourlyMetricsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var bucketStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-1);

        foreach (var kvp in _hourlyAggregators)
        {
            var seriesKey = kvp.Key;
            var aggregator = kvp.Value;
            var snapshot = aggregator.GetSnapshotAndReset();

            if (snapshot.Count == 0)
            {
                continue;
            }

            try
            {
                if (_metricDefinitions.TryGetValue(seriesKey.MetricName, out var metricDef))
                {
                    await PostgresMetricsWriter.WriteHourlyPointAsync(
                        _options.CentralConnectionString!,
                        _options.SchemaName,
                        seriesKey,
                        snapshot,
                        bucketStart,
                        metricDef.Unit,
                        metricDef.AggKind.ToString().ToUpperInvariant(),
                        metricDef.Description,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error writing hourly metric for series {MetricName}", seriesKey.MetricName);
            }
        }
    }
}





