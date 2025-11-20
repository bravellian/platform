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

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        var keyDict = new Dictionary<string, IOutboxStore>();
        var outboxDict = new Dictionary<string, IOutbox>();
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

        this.stores = storesList;
        this.storeIdentifiers = identifiersDict;
        this.storesByKey = keyDict;
        this.outboxesByKey = outboxDict;
        this.outboxOptions = optionsList;
        this.logger = loggerFactory.CreateLogger<ConfiguredOutboxStoreProvider>();
    }

    /// <summary>
    /// Initializes the outbox stores by deploying database schemas if enabled.
    /// This method should be called after construction to ensure all databases are ready.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var options in this.outboxOptions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.EnableSchemaDeployment)
            {
                var identifier = options.ConnectionString.Contains("Database=")
                    ? ExtractDatabaseName(options.ConnectionString)
                    : $"{options.SchemaName}.{options.TableName}";

                try
                {
                    this.logger.LogInformation(
                        "Deploying outbox schema for database: {Identifier}",
                        identifier);

                    await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
                        options.ConnectionString,
                        options.SchemaName,
                        options.TableName).ConfigureAwait(false);

                    await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(
                        options.ConnectionString,
                        options.SchemaName).ConfigureAwait(false);

                    this.logger.LogInformation(
                        "Successfully deployed outbox schema for database: {Identifier}",
                        identifier);
                }
                catch (Exception ex)
                {
                    this.logger.LogError(
                        ex,
                        "Failed to deploy outbox schema for database: {Identifier}. Store will be available but may fail on first use.",
                        identifier);
                }
            }
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IOutboxStore>> GetAllStoresAsync() =>
        Task.FromResult<IReadOnlyList<IOutboxStore>>(this.stores);

    /// <inheritdoc/>
    public string GetStoreIdentifier(IOutboxStore store)
    {
        return this.storeIdentifiers.TryGetValue(store, out var identifier)
            ? identifier
            : "Unknown";
    }

    /// <inheritdoc/>
    public IOutboxStore? GetStoreByKey(string key)
    {
        return this.storesByKey.TryGetValue(key, out var store) ? store : null;
    }

    /// <inheritdoc/>
    public IOutbox? GetOutboxByKey(string key)
    {
        return this.outboxesByKey.TryGetValue(key, out var outbox) ? outbox : null;
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
