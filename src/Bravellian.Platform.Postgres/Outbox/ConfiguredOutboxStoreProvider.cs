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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Bravellian.Platform;
/// <summary>
/// Provides access to multiple outbox stores configured at startup.
/// This implementation creates stores based on the provided options.
/// </summary>
internal sealed class ConfiguredOutboxStoreProvider : IOutboxStoreProvider
{
    private readonly IReadOnlyList<IOutboxStore> stores;
    private readonly Dictionary<IOutboxStore, string> storeIdentifiers;
    private readonly Dictionary<string, IOutboxStore> storesByKey;
    private readonly Dictionary<string, IOutbox> outboxesByKey;
    private readonly IReadOnlyList<PostgresOutboxOptions> outboxOptions;
    private readonly ILogger<ConfiguredOutboxStoreProvider> logger;

    public ConfiguredOutboxStoreProvider(
        IEnumerable<PostgresOutboxOptions> outboxOptions,
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
            var storeLogger = loggerFactory.CreateLogger<PostgresOutboxStore>();
            var store = new PostgresOutboxStore(
                Options.Create(options),
                timeProvider,
                storeLogger);

            var outboxLogger = loggerFactory.CreateLogger<PostgresOutboxService>();
            var outbox = new PostgresOutboxService(
                Options.Create(options),
                outboxLogger);

            storesList.Add(store);

            // Use connection string or a custom identifier
            var identifier = options.ConnectionString.Contains("Database=", StringComparison.OrdinalIgnoreCase)
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

    /// <summary>
    /// Initializes the outbox stores by deploying database schemas if enabled.
    /// This method should be called after construction to ensure all databases are ready.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Initialization logs failures and continues.")]
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var options in outboxOptions)
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
                        "Deploying outbox schema for database: {Identifier}",
                        identifier);

                    await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
                        options.ConnectionString,
                        options.SchemaName,
                        options.TableName).ConfigureAwait(false);

                    await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
                        options.ConnectionString,
                        options.SchemaName).ConfigureAwait(false);

                    logger.LogInformation(
                        "Successfully deployed outbox schema for database: {Identifier}",
                        identifier);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to deploy outbox schema for database: {Identifier}. Store will be available but may fail on first use.",
                        identifier);
                }
            }
        }
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

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Parsing failures fall back to UnknownDB.")]
    private static string ExtractDatabaseName(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return string.IsNullOrEmpty(builder.Database) ? "UnknownDB" : builder.Database;
        }
        catch (Exception ex)
        {
            // Log the exception details for diagnostics
            System.Diagnostics.Debug.WriteLine($"Failed to extract database name from connection string: {ex.Message}");
            return "UnknownDB";
        }
    }
}






