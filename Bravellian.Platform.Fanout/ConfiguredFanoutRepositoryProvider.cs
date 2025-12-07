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
/// Provides access to multiple fanout repositories configured at startup.
/// This implementation creates repositories based on the provided options.
/// Call <see cref="InitializeAsync"/> after construction if schema deployment is enabled to ensure all databases are ready.
/// </summary>
internal sealed class ConfiguredFanoutRepositoryProvider : IFanoutRepositoryProvider, IDisposable
{
    private readonly IReadOnlyList<IFanoutPolicyRepository> policyRepositories;
    private readonly IReadOnlyList<IFanoutCursorRepository> cursorRepositories;
    private readonly IReadOnlyDictionary<IFanoutPolicyRepository, string> policyIdentifiers;
    private readonly IReadOnlyDictionary<IFanoutCursorRepository, string> cursorIdentifiers;
    private readonly IReadOnlyDictionary<string, IFanoutPolicyRepository> policyRepositoriesByKey;
    private readonly IReadOnlyDictionary<string, IFanoutCursorRepository> cursorRepositoriesByKey;
    private readonly IReadOnlyList<SqlFanoutOptions> fanoutOptions;
    private readonly ILogger<ConfiguredFanoutRepositoryProvider> logger;

    public ConfiguredFanoutRepositoryProvider(
        IEnumerable<SqlFanoutOptions> fanoutOptions,
        ILoggerFactory loggerFactory)
    {
        var policyReposList = new List<IFanoutPolicyRepository>();
        var cursorReposList = new List<IFanoutCursorRepository>();
        var policyIdentifiersDict = new Dictionary<IFanoutPolicyRepository, string>();
        var cursorIdentifiersDict = new Dictionary<IFanoutCursorRepository, string>();
        var policyKeyDict = new Dictionary<string, IFanoutPolicyRepository>(StringComparer.Ordinal);
        var cursorKeyDict = new Dictionary<string, IFanoutCursorRepository>(StringComparer.Ordinal);
        var optionsList = fanoutOptions.ToList();

        foreach (var options in optionsList)
        {
            var policyRepo = new SqlFanoutPolicyRepository(Options.Create(options));
            var cursorRepo = new SqlFanoutCursorRepository(Options.Create(options));

            policyReposList.Add(policyRepo);
            cursorReposList.Add(cursorRepo);

            // Use connection string or a custom identifier
            var identifier = ExtractIdentifier(options);

            // Check for duplicate identifiers
            if (policyKeyDict.ContainsKey(identifier))
            {
                throw new InvalidOperationException(
                    $"Duplicate fanout identifier detected: '{identifier}'. Each fanout database must have a unique identifier.");
            }

            policyIdentifiersDict[policyRepo] = identifier;
            cursorIdentifiersDict[cursorRepo] = identifier;
            policyKeyDict[identifier] = policyRepo;
            cursorKeyDict[identifier] = cursorRepo;
        }

        policyRepositories = policyReposList;
        cursorRepositories = cursorReposList;
        policyIdentifiers = policyIdentifiersDict;
        cursorIdentifiers = cursorIdentifiersDict;
        policyRepositoriesByKey = policyKeyDict;
        cursorRepositoriesByKey = cursorKeyDict;
        this.fanoutOptions = optionsList;
        logger = loggerFactory.CreateLogger<ConfiguredFanoutRepositoryProvider>();
    }

    /// <summary>
    /// Initializes the fanout repositories by deploying database schemas if enabled.
    /// This method should be called after construction when schema deployment is enabled to ensure all databases are ready.
    /// The provider will work without calling this method, but database operations may fail if schemas don't exist.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var options in fanoutOptions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.EnableSchemaDeployment)
            {
                var identifier = ExtractIdentifier(options);

                try
                {
                    logger.LogInformation(
                        "Deploying fanout schema for database: {Identifier}",
                        identifier);

                    await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
                        options.ConnectionString,
                        options.SchemaName,
                        options.PolicyTableName,
                        options.CursorTableName).ConfigureAwait(false);

                    logger.LogInformation(
                        "Successfully deployed fanout schema for database: {Identifier}",
                        identifier);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to deploy fanout schema for database: {Identifier}. Repository will be available but may fail on first use.",
                        identifier);
                }
            }
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IFanoutPolicyRepository>> GetAllPolicyRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(policyRepositories);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IFanoutCursorRepository>> GetAllCursorRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(cursorRepositories);
    }

    /// <inheritdoc/>
    public string GetRepositoryIdentifier(IFanoutPolicyRepository repository)
    {
        return policyIdentifiers.TryGetValue(repository, out var identifier)
            ? identifier
            : "Unknown";
    }

    /// <inheritdoc/>
    public string GetRepositoryIdentifier(IFanoutCursorRepository repository)
    {
        return cursorIdentifiers.TryGetValue(repository, out var identifier)
            ? identifier
            : "Unknown";
    }

    /// <inheritdoc/>
    public IFanoutPolicyRepository? GetPolicyRepositoryByKey(string key)
    {
        return policyRepositoriesByKey.TryGetValue(key, out var repo) ? repo : null;
    }

    /// <inheritdoc/>
    public IFanoutCursorRepository? GetCursorRepositoryByKey(string key)
    {
        return cursorRepositoriesByKey.TryGetValue(key, out var repo) ? repo : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Dispose all repositories if they implement IDisposable
        foreach (var repo in policyRepositories)
        {
            (repo as IDisposable)?.Dispose();
        }

        foreach (var repo in cursorRepositories)
        {
            (repo as IDisposable)?.Dispose();
        }
    }

    private static string ExtractIdentifier(SqlFanoutOptions options)
    {
        return options.ConnectionString.Contains("Database=")
            ? ExtractDatabaseName(options.ConnectionString)
            : $"{options.SchemaName}.{options.PolicyTableName}";
    }

    private static string ExtractDatabaseName(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return string.IsNullOrEmpty(builder.InitialCatalog) ? "UnknownDB" : builder.InitialCatalog;
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"Invalid connection string in fanout options: {connectionString}",
                nameof(connectionString),
                ex);
        }
    }
}
