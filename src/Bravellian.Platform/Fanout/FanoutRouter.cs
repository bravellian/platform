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

/// <summary>
/// Default implementation of IFanoutRouter that uses IFanoutRepositoryProvider
/// to route operations to the correct database.
/// </summary>
internal sealed class FanoutRouter : IFanoutRouter
{
    private readonly IFanoutRepositoryProvider repositoryProvider;
    private readonly ILogger<FanoutRouter> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FanoutRouter"/> class.
    /// </summary>
    /// <param name="repositoryProvider">The repository provider for accessing multiple databases.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public FanoutRouter(
        IFanoutRepositoryProvider repositoryProvider,
        ILogger<FanoutRouter> logger)
    {
        this.repositoryProvider = repositoryProvider;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public IFanoutPolicyRepository GetPolicyRepository(string key)
    {
        var repository = this.repositoryProvider.GetPolicyRepositoryByKey(key);

        if (repository == null)
        {
            this.logger.LogError(
                "No fanout policy repository found for routing key: {Key}",
                key);

            throw new InvalidOperationException(
                $"No fanout policy repository configured for routing key: {key}");
        }

        return repository;
    }

    /// <inheritdoc/>
    public IFanoutCursorRepository GetCursorRepository(string key)
    {
        var repository = this.repositoryProvider.GetCursorRepositoryByKey(key);

        if (repository == null)
        {
            this.logger.LogError(
                "No fanout cursor repository found for routing key: {Key}",
                key);

            throw new InvalidOperationException(
                $"No fanout cursor repository configured for routing key: {key}");
        }

        return repository;
    }
}
