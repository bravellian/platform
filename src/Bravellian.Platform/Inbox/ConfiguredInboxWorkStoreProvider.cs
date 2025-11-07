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

namespace Bravellian.Platform;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides access to multiple inbox work stores configured at startup.
/// This implementation creates stores based on the provided options.
/// </summary>
public sealed class ConfiguredInboxWorkStoreProvider : IInboxWorkStoreProvider
{
    private readonly IReadOnlyList<IInboxWorkStore> stores;
    private readonly IReadOnlyDictionary<IInboxWorkStore, string> storeIdentifiers;
    private readonly IReadOnlyDictionary<string, IInboxWorkStore> storesByKey;
    private readonly IReadOnlyDictionary<string, IInbox> inboxesByKey;

    public ConfiguredInboxWorkStoreProvider(
        IEnumerable<SqlInboxOptions> inboxOptions,
        ILoggerFactory loggerFactory)
    {
        var storesList = new List<IInboxWorkStore>();
        var identifiersDict = new Dictionary<IInboxWorkStore, string>();
        var keyDict = new Dictionary<string, IInboxWorkStore>();
        var inboxDict = new Dictionary<string, IInbox>();

        foreach (var options in inboxOptions)
        {
            var storeLogger = loggerFactory.CreateLogger<SqlInboxWorkStore>();
            var store = new SqlInboxWorkStore(
                Options.Create(options),
                storeLogger);

            var inboxLogger = loggerFactory.CreateLogger<SqlInboxService>();
            var inbox = new SqlInboxService(
                Options.Create(options),
                inboxLogger);

            storesList.Add(store);

            // Use connection string or a custom identifier
            var identifier = options.ConnectionString.Contains("Database=")
                ? ExtractDatabaseName(options.ConnectionString)
                : $"{options.SchemaName}.{options.TableName}";

            identifiersDict[store] = identifier;
            keyDict[identifier] = store;
            inboxDict[identifier] = inbox;
        }

        this.stores = storesList;
        this.storeIdentifiers = identifiersDict;
        this.storesByKey = keyDict;
        this.inboxesByKey = inboxDict;
    }

    /// <inheritdoc/>
    public IReadOnlyList<IInboxWorkStore> GetAllStores() => this.stores;

    /// <inheritdoc/>
    public string GetStoreIdentifier(IInboxWorkStore store)
    {
        return this.storeIdentifiers.TryGetValue(store, out var identifier)
            ? identifier
            : "Unknown";
    }

    /// <inheritdoc/>
    public IInboxWorkStore? GetStoreByKey(string key)
    {
        return this.storesByKey.TryGetValue(key, out var store) ? store : null;
    }

    /// <inheritdoc/>
    public IInbox? GetInboxByKey(string key)
    {
        return this.inboxesByKey.TryGetValue(key, out var inbox) ? inbox : null;
    }

    private static string ExtractDatabaseName(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return string.IsNullOrEmpty(builder.InitialCatalog) ? "UnknownDB" : builder.InitialCatalog;
        }
        catch
        {
            // Return fallback value on any parsing error
            return "UnknownDB";
        }
    }
}
