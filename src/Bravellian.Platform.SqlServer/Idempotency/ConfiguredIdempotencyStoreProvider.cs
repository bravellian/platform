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

using System.Diagnostics.CodeAnalysis;
using Bravellian.Platform.Idempotency;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

internal sealed class ConfiguredIdempotencyStoreProvider : IIdempotencyStoreProvider
{
    private readonly IReadOnlyList<IIdempotencyStore> stores;
    private readonly Dictionary<IIdempotencyStore, string> storeIdentifiers;
    private readonly Dictionary<string, IIdempotencyStore> storesByKey;
    private readonly IReadOnlyList<SqlIdempotencyOptions> idempotencyOptions;
    private readonly ILogger<ConfiguredIdempotencyStoreProvider> logger;

    public ConfiguredIdempotencyStoreProvider(
        IEnumerable<SqlIdempotencyOptions> idempotencyOptions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        var storesList = new List<IIdempotencyStore>();
        var identifiersDict = new Dictionary<IIdempotencyStore, string>();
        var keyDict = new Dictionary<string, IIdempotencyStore>(StringComparer.Ordinal);
        var optionsList = idempotencyOptions.ToList();

        foreach (var options in optionsList)
        {
            var storeLogger = loggerFactory.CreateLogger<SqlIdempotencyStore>();
            var store = new SqlIdempotencyStore(
                Options.Create(options),
                timeProvider,
                storeLogger);

            storesList.Add(store);

            var identifier = options.ConnectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase)
                ? ExtractDatabaseName(options.ConnectionString)
                : $"{options.SchemaName}.{options.TableName}";

            identifiersDict[store] = identifier;
            keyDict[identifier] = store;
        }

        stores = storesList;
        storeIdentifiers = identifiersDict;
        storesByKey = keyDict;
        this.idempotencyOptions = optionsList;
        logger = loggerFactory.CreateLogger<ConfiguredIdempotencyStoreProvider>();
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Initialization logs failures and continues.")]
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var options in idempotencyOptions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.EnableSchemaDeployment)
            {
                var identifier = options.ConnectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase)
                    ? ExtractDatabaseName(options.ConnectionString)
                    : $"{options.SchemaName}.{options.TableName}";

                try
                {
                    logger.LogInformation(
                        "Deploying idempotency schema for database: {Identifier}",
                        identifier);

                    await DatabaseSchemaManager.EnsureIdempotencySchemaAsync(
                        options.ConnectionString,
                        options.SchemaName,
                        options.TableName).ConfigureAwait(false);

                    logger.LogInformation(
                        "Successfully deployed idempotency schema for database: {Identifier}",
                        identifier);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to deploy idempotency schema for database: {Identifier}. Store will be available but may fail on first use.",
                        identifier);
                }
            }
        }
    }

    public Task<IReadOnlyList<IIdempotencyStore>> GetAllStoresAsync() =>
        Task.FromResult<IReadOnlyList<IIdempotencyStore>>(stores);

    public string GetStoreIdentifier(IIdempotencyStore store)
    {
        return storeIdentifiers.TryGetValue(store, out var identifier)
            ? identifier
            : "Unknown";
    }

    public IIdempotencyStore? GetStoreByKey(string key)
    {
        return storesByKey.TryGetValue(key, out var store) ? store : null;
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
            System.Diagnostics.Debug.WriteLine($"Failed to extract database name from connection string: {ex.Message}");
            return "UnknownDB";
        }
    }
}
