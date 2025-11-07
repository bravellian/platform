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

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Provides access to multiple outbox stores configured at startup.
/// This implementation creates stores based on the provided options.
/// </summary>
public sealed class ConfiguredOutboxStoreProvider : IOutboxStoreProvider
{
    private readonly IReadOnlyList<IOutboxStore> stores;
    private readonly IReadOnlyDictionary<IOutboxStore, string> storeIdentifiers;
    private readonly IReadOnlyDictionary<string, IOutboxStore> storesByIdentifier;

    public ConfiguredOutboxStoreProvider(
        IEnumerable<SqlOutboxOptions> outboxOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        var storesList = new List<IOutboxStore>();
        var identifiersDict = new Dictionary<IOutboxStore, string>();
        var storesByIdentifierDict = new Dictionary<string, IOutboxStore>(StringComparer.OrdinalIgnoreCase);

        foreach (var options in outboxOptions)
        {
            var logger = loggerFactory.CreateLogger<SqlOutboxStore>();
            var store = new SqlOutboxStore(
                Options.Create(options),
                timeProvider,
                logger);

            storesList.Add(store);

            // Use connection string or a custom identifier
            var identifier = options.ConnectionString.Contains("Database=")
                ? ExtractDatabaseName(options.ConnectionString)
                : $"{options.SchemaName}.{options.TableName}";

            identifiersDict[store] = identifier;
            storesByIdentifierDict[identifier] = store;
        }

        this.stores = storesList;
        this.storeIdentifiers = identifiersDict;
        this.storesByIdentifier = storesByIdentifierDict;
    }

    /// <inheritdoc/>
    public IOutboxStore GetStore(object key)
    {
        if (key is not string identifier)
        {
            throw new ArgumentException("The key must be a string identifier.", nameof(key));
        }

        if (this.storesByIdentifier.TryGetValue(identifier, out var store))
        {
            return store;
        }

        throw new KeyNotFoundException($"No outbox store found with the identifier '{identifier}'.");
    }

    /// <inheritdoc/>
    public IReadOnlyList<IOutboxStore> GetAllStores() => this.stores;

    /// <inheritdoc/>
    public string GetStoreIdentifier(IOutboxStore store)
    {
        return this.storeIdentifiers.TryGetValue(store, out var identifier)
            ? identifier
            : "Unknown";
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
