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


using Bravellian.Platform.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Provides access to a pre-configured list of scheduler stores.
/// Each scheduler store represents a separate database/tenant.
/// </summary>
public sealed class ConfiguredSchedulerStoreProvider : ISchedulerStoreProvider
{
    private readonly Dictionary<string, StoreEntry> storesByIdentifier = new(StringComparer.Ordinal);
    private readonly List<ISchedulerStore> allStores = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguredSchedulerStoreProvider"/> class.
    /// </summary>
    /// <param name="configs">Scheduler database configurations.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="eventEmitter">Optional platform event emitter.</param>
    public ConfiguredSchedulerStoreProvider(
        IEnumerable<SchedulerDatabaseConfig> configs,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        IPlatformEventEmitter? eventEmitter = null)
    {
        ArgumentNullException.ThrowIfNull(configs);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        foreach (var config in configs)
        {
            var store = new SqlSchedulerStore(
                Options.Create(new SqlSchedulerOptions
                {
                    ConnectionString = config.ConnectionString,
                    SchemaName = config.SchemaName,
                    JobsTableName = config.JobsTableName,
                    JobRunsTableName = config.JobRunsTableName,
                    TimersTableName = config.TimersTableName,
                }),
                timeProvider);

            var client = new SqlSchedulerClient(
                Options.Create(new SqlSchedulerOptions
                {
                    ConnectionString = config.ConnectionString,
                    SchemaName = config.SchemaName,
                    JobsTableName = config.JobsTableName,
                    JobRunsTableName = config.JobRunsTableName,
                    TimersTableName = config.TimersTableName,
                }),
                timeProvider);

            var outboxLogger = loggerFactory.CreateLogger<SqlOutboxService>();
            var outbox = new SqlOutboxService(
                Options.Create(new SqlOutboxOptions
                {
                    ConnectionString = config.ConnectionString,
                    SchemaName = config.SchemaName,
                    TableName = "Outbox",
                }),
                outboxLogger,
                joinStore: null,
                eventEmitter);

            var entry = new StoreEntry
            {
                Identifier = config.Identifier,
                Store = store,
                Client = client,
                Outbox = outbox,
            };

            storesByIdentifier[config.Identifier] = entry;
            allStores.Add(store);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ISchedulerStore>> GetAllStoresAsync() =>
        Task.FromResult<IReadOnlyList<ISchedulerStore>>(allStores);

    /// <inheritdoc/>
    public string GetStoreIdentifier(ISchedulerStore store)
    {
        foreach (var entry in storesByIdentifier.Values)
        {
            if (ReferenceEquals(entry.Store, store))
            {
                return entry.Identifier;
            }
        }

        return "Unknown";
    }

    /// <inheritdoc/>
    public ISchedulerStore? GetStoreByKey(string key)
    {
        if (storesByIdentifier.TryGetValue(key, out var entry))
        {
            return entry.Store;
        }

        return null;
    }

    /// <inheritdoc/>
    public ISchedulerClient? GetSchedulerClientByKey(string key)
    {
        if (storesByIdentifier.TryGetValue(key, out var entry))
        {
            return entry.Client;
        }

        return null;
    }

    /// <inheritdoc/>
    public IOutbox? GetOutboxByKey(string key)
    {
        if (storesByIdentifier.TryGetValue(key, out var entry))
        {
            return entry.Outbox;
        }

        return null;
    }

    private sealed class StoreEntry
    {
        public required string Identifier { get; set; }

        public required ISchedulerStore Store { get; set; }

        public required ISchedulerClient Client { get; set; }

        public required IOutbox Outbox { get; set; }
    }
}
