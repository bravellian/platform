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
/// Fanout repository provider that uses the unified platform database discovery.
/// </summary>
internal sealed class PlatformFanoutRepositoryProvider : IFanoutRepositoryProvider
{
    private readonly IPlatformDatabaseDiscovery discovery;
    private readonly ILoggerFactory loggerFactory;
    private IReadOnlyList<IFanoutPolicyRepository>? cachedPolicyRepositories;
    private IReadOnlyList<IFanoutCursorRepository>? cachedCursorRepositories;
    private readonly Dictionary<IFanoutPolicyRepository, string> policyIdentifiers = new();
    private readonly Dictionary<IFanoutCursorRepository, string> cursorIdentifiers = new();
    private readonly Dictionary<string, IFanoutPolicyRepository> policyRepositoriesByKey = new();
    private readonly Dictionary<string, IFanoutCursorRepository> cursorRepositoriesByKey = new();

    public PlatformFanoutRepositoryProvider(
        IPlatformDatabaseDiscovery discovery,
        ILoggerFactory loggerFactory)
    {
        this.discovery = discovery;
        this.loggerFactory = loggerFactory;
    }

    public async Task<IReadOnlyList<IFanoutPolicyRepository>> GetAllPolicyRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        if (this.cachedPolicyRepositories == null)
        {
            await InitializeAsync(cancellationToken);
        }
        
        return this.cachedPolicyRepositories!;
    }

    public async Task<IReadOnlyList<IFanoutCursorRepository>> GetAllCursorRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        if (this.cachedCursorRepositories == null)
        {
            await InitializeAsync(cancellationToken);
        }
        
        return this.cachedCursorRepositories!;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var databases = await this.discovery.DiscoverDatabasesAsync(cancellationToken);
        var policyRepositories = new List<IFanoutPolicyRepository>();
        var cursorRepositories = new List<IFanoutCursorRepository>();
        
        foreach (var db in databases)
        {
            var options = new SqlFanoutOptions
            {
                ConnectionString = db.ConnectionString,
                SchemaName = db.SchemaName,
            };
            
            var policyRepo = new SqlFanoutPolicyRepository(Options.Create(options));
            var cursorRepo = new SqlFanoutCursorRepository(Options.Create(options));

            policyRepositories.Add(policyRepo);
            cursorRepositories.Add(cursorRepo);

            this.policyIdentifiers[policyRepo] = db.Name;
            this.cursorIdentifiers[cursorRepo] = db.Name;
            this.policyRepositoriesByKey[db.Name] = policyRepo;
            this.cursorRepositoriesByKey[db.Name] = cursorRepo;
        }
        
        this.cachedPolicyRepositories = policyRepositories;
        this.cachedCursorRepositories = cursorRepositories;
    }

    public IFanoutPolicyRepository GetPolicyRepositoryByKey(string key)
    {
        if (this.cachedPolicyRepositories == null)
        {
            InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        
        return this.policyRepositoriesByKey.TryGetValue(key, out var repo)
            ? repo
            : throw new KeyNotFoundException($"No fanout policy repository found for key: {key}");
    }

    public IFanoutCursorRepository GetCursorRepositoryByKey(string key)
    {
        if (this.cachedCursorRepositories == null)
        {
            InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        
        return this.cursorRepositoriesByKey.TryGetValue(key, out var repo)
            ? repo
            : throw new KeyNotFoundException($"No fanout cursor repository found for key: {key}");
    }

    public string GetRepositoryIdentifier(IFanoutPolicyRepository repository)
    {
        return this.policyIdentifiers.TryGetValue(repository, out var identifier)
            ? identifier
            : "unknown";
    }

    public string GetRepositoryIdentifier(IFanoutCursorRepository repository)
    {
        return this.cursorIdentifiers.TryGetValue(repository, out var identifier)
            ? identifier
            : "unknown";
    }
}
