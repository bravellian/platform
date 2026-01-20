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


using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Provides access to multiple outbox stores configured at startup.
/// This implementation creates stores based on the provided options.
/// </summary>
internal sealed class ConfiguredOutboxStoreProvider : IOutboxStoreProvider
{
    private readonly IReadOnlyList<IOutboxStore> stores;
    private readonly IReadOnlyDictionary<IOutboxStore, string> storeIdentifiers;
    private readonly IReadOnlyDictionary<string, IOutboxStore> storesByKey;
    private readonly IReadOnlyDictionary<string, IOutbox> outboxesByKey;
    private readonly IReadOnlyList<SqlOutboxOptions> outboxOptions;
    private readonly ILogger<ConfiguredOutboxStoreProvider> logger;

    public ConfiguredOutboxStoreProvider(
        IEnumerable<SqlOutboxOptions> outboxOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        var storesList = new List<IOutboxStore>();
        var identifiersDict = new Dictionary<IOutboxStore, string>();
        var keyDict = new Dictionary<string, IOutboxStore>(StringComparer.Ordinal);
        var outboxDict = new Dictionary<string, IOutbox>(StringComparer.Ordinal);
        var optionsList = outboxOptions.ToList();

        foreach (var options in optionsList)
        {
            var storeLogger = loggerFactory.CreateLogger<SqlOutboxStore>();
            var store = new SqlOutboxStore(
                Options.Create(options),
                timeProvider,
                storeLogger);

            var outboxLogger = loggerFactory.CreateLogger<SqlOutboxService>();
            var outbox = new SqlOutboxService(
                Options.Create(options),
                outboxLogger);

            storesList.Add(store);

            // Use connection string or a custom identifier
            var identifier = options.ConnectionString.Contains("Database=")
                ? ExtractDatabaseName(options.ConnectionString)
                : $"{options.SchemaName}.{options.TableName}";

            identifiersDict[store] = identifier;
            keyDict[identifier] = store;
            outboxDict[identifier] = outbox;
        }

        stores = storesList;
        storeIdentifiers = identifiersDict;
        storesByKey = keyDict;
        outboxesByKey = outboxDict;
        this.outboxOptions = optionsList;
        logger = loggerFactory.CreateLogger<ConfiguredOutboxStoreProvider>();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync() =>
        Task.FromResult<IReadOnlyList<IOutboxStore>>(stores);

    /// <inheritdoc/>
    public string GetStoreIdentifier(IOutboxStore store)
    {
        return storeIdentifiers.TryGetValue(store, out var identifier)
            ? identifier
            : "Unknown";
    }

    /// <inheritdoc/>
    public IOutboxStore? GetStoreByKey(string key)
    {
        return storesByKey.TryGetValue(key, out var store) ? store : null;
    }

    /// <inheritdoc/>
    public IOutbox? GetOutboxByKey(string key)
    {
        return outboxesByKey.TryGetValue(key, out var outbox) ? outbox : null;
    }

    private static string ExtractDatabaseName(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return string.IsNullOrEmpty(builder.InitialCatalog) ? "UnknownDB" : builder.InitialCatalog;
        }
        catch (Exception ex)
        {
            // Log the exception details for diagnostics
            System.Diagnostics.Debug.WriteLine($"Failed to extract database name from connection string: {ex.Message}");
            return "UnknownDB";
        }
    }
}
