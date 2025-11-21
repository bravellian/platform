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


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bravellian.Platform.Semaphore;
/// <summary>
/// Extension methods for registering semaphore services.
/// </summary>
internal static class SemaphoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers semaphore services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The database connection string for semaphores.</param>
    /// <param name="schemaName">The schema name for semaphore tables (default: "dbo").</param>
    /// <param name="configure">Optional configuration action.</param>
    internal static void AddSemaphoreServices(
        this IServiceCollection services,
        string connectionString,
        string schemaName = "dbo",
        Action<SemaphoreOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);

        services.AddOptions<SemaphoreOptions>()
            .Configure(options =>
            {
                options.ConnectionString = connectionString;
                options.SchemaName = schemaName;
                configure?.Invoke(options);
            })
            .ValidateOnStart();

        services.AddSingleton<ISemaphoreService, SqlSemaphoreService>();
        services.AddSingleton<IHostedService, SemaphoreReaperService>();
    }
}
