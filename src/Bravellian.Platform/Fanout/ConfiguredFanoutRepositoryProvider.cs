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
/// Provides access to multiple fanout repositories configured at startup.
/// This implementation creates repositories based on the provided options.
/// </summary>
internal sealed class ConfiguredFanoutRepositoryProvider : IFanoutRepositoryProvider
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
        var policyKeyDict = new Dictionary<string, IFanoutPolicyRepository>();
        var cursorKeyDict = new Dictionary<string, IFanoutCursorRepository>();
        var optionsList = fanoutOptions.ToList();

        foreach (var options in optionsList)
        {
            var policyRepo = new SqlFanoutPolicyRepository(Options.Create(options));
            var cursorRepo = new SqlFanoutCursorRepository(Options.Create(options));

            policyReposList.Add(policyRepo);
            cursorReposList.Add(cursorRepo);

            // Use connection string or a custom identifier
            var identifier = options.ConnectionString.Contains("Database=")
                ? ExtractDatabaseName(options.ConnectionString)
                : $"{options.SchemaName}.{options.PolicyTableName}";

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

        this.policyRepositories = policyReposList;
        this.cursorRepositories = cursorReposList;
        this.policyIdentifiers = policyIdentifiersDict;
        this.cursorIdentifiers = cursorIdentifiersDict;
        this.policyRepositoriesByKey = policyKeyDict;
        this.cursorRepositoriesByKey = cursorKeyDict;
        this.fanoutOptions = optionsList;
        this.logger = loggerFactory.CreateLogger<ConfiguredFanoutRepositoryProvider>();
    }

    /// <summary>
    /// Initializes the fanout repositories by deploying database schemas if enabled.
    /// This method should be called after construction to ensure all databases are ready.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var options in this.fanoutOptions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.EnableSchemaDeployment)
            {
                var identifier = options.ConnectionString.Contains("Database=")
                    ? ExtractDatabaseName(options.ConnectionString)
                    : $"{options.SchemaName}.{options.PolicyTableName}";

                try
                {
                    this.logger.LogInformation(
                        "Deploying fanout schema for database: {Identifier}",
                        identifier);

                    await DatabaseSchemaManager.EnsureFanoutSchemaAsync(
                        options.ConnectionString,
                        options.SchemaName,
                        options.PolicyTableName,
                        options.CursorTableName).ConfigureAwait(false);

                    this.logger.LogInformation(
                        "Successfully deployed fanout schema for database: {Identifier}",
                        identifier);
                }
                catch (Exception ex)
                {
                    this.logger.LogError(
                        ex,
                        "Failed to deploy fanout schema for database: {Identifier}. Repository will be available but may fail on first use.",
                        identifier);
                }
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<IFanoutPolicyRepository> GetAllPolicyRepositories() => this.policyRepositories;

    /// <inheritdoc/>
    public IReadOnlyList<IFanoutCursorRepository> GetAllCursorRepositories() => this.cursorRepositories;

    /// <inheritdoc/>
    public string GetRepositoryIdentifier(IFanoutPolicyRepository repository)
    {
        return this.policyIdentifiers.TryGetValue(repository, out var identifier)
            ? identifier
            : "Unknown";
    }

    /// <inheritdoc/>
    public string GetRepositoryIdentifier(IFanoutCursorRepository repository)
    {
        return this.cursorIdentifiers.TryGetValue(repository, out var identifier)
            ? identifier
            : "Unknown";
    }

    /// <inheritdoc/>
    public IFanoutPolicyRepository? GetPolicyRepositoryByKey(string key)
    {
        return this.policyRepositoriesByKey.TryGetValue(key, out var repo) ? repo : null;
    }

    /// <inheritdoc/>
    public IFanoutCursorRepository? GetCursorRepositoryByKey(string key)
    {
        return this.cursorRepositoriesByKey.TryGetValue(key, out var repo) ? repo : null;
    }

    private static string ExtractDatabaseName(string connectionString)
    {
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
            return string.IsNullOrEmpty(builder.InitialCatalog) ? "UnknownDB" : builder.InitialCatalog;
        }
        catch (ArgumentException)
        {
            // Return fallback value on connection string parsing error
            return "UnknownDB";
        }
    }
}
