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


using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;
/// <summary>
/// Background service that validates platform configuration at startup.
/// </summary>
internal sealed class PlatformLifecycleService : IHostedService
{
    private readonly PlatformConfiguration configuration;
    private readonly IPlatformDatabaseDiscovery? discovery;
    private readonly ILogger<PlatformLifecycleService> logger;

    public PlatformLifecycleService(
        PlatformConfiguration configuration,
        ILogger<PlatformLifecycleService> logger,
        IPlatformDatabaseDiscovery? discovery = null)
    {
        this.configuration = configuration;
        this.discovery = discovery;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Platform starting with environment style: {EnvironmentStyle}, Discovery: {UsesDiscovery}",
            configuration.EnvironmentStyle,
            configuration.UsesDiscovery);

        // Validate configuration based on environment style
        switch (configuration.EnvironmentStyle)
        {
            case PlatformEnvironmentStyle.MultiDatabaseNoControl:
            case PlatformEnvironmentStyle.MultiDatabaseWithControl:
                await ValidateMultiDatabaseAsync(cancellationToken);
                break;
        }

        // Validate control plane if configured
        if (configuration.ControlPlaneConnectionString != null)
        {
            await ValidateControlPlaneAsync(cancellationToken);
        }

        logger.LogInformation("Platform startup validation completed successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Platform lifecycle service stopping.");
        return Task.CompletedTask;
    }

    private async Task ValidateMultiDatabaseAsync(CancellationToken cancellationToken)
    {
        if (discovery == null)
        {
            throw new InvalidOperationException("Discovery must be configured for multi-database environment.");
        }

        var databases = await discovery.DiscoverDatabasesAsync(cancellationToken);

        if (databases.Count == 0)
        {
            // For dynamic discovery scenarios, it's acceptable to start with zero databases
            // as they may be added later. For static list-based configurations, we should throw.
            if (configuration.RequiresDatabaseAtStartup)
            {
                throw new InvalidOperationException(
                    "Multi-database discovery returned no databases. At least one database is required.");
            }

            logger.LogWarning(
                "Multi-database discovery returned no databases. The platform will continue running " +
                "and wait for databases to be discovered. Control Plane Configured={HasControlPlane}",
                configuration.ControlPlaneConnectionString != null);
            return;
        }

        logger.LogInformation(
            "Multi-database validated: Count={DatabaseCount}, Control Plane Configured={HasControlPlane}",
            databases.Count,
            configuration.ControlPlaneConnectionString != null);

        // Log database names (redacted if necessary)
        foreach (var db in databases)
        {
            logger.LogDebug("Discovered database: Name={DatabaseName}, Schema={SchemaName}", db.Name, db.SchemaName);
        }
    }

    private async Task ValidateControlPlaneAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.ControlPlaneConnectionString))
        {
            throw new InvalidOperationException("Control plane connection string is null or empty.");
        }

        logger.LogInformation("Validating control plane connectivity...");

        try
        {
            await using var connection = new SqlConnection(configuration.ControlPlaneConnectionString);
            await connection.OpenAsync(cancellationToken);

            logger.LogInformation("Control plane connectivity validated successfully.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to connect to control plane. Ensure the control plane database is accessible.", ex);
        }
    }

    private async Task TestDatabaseConnectivityAsync(PlatformDatabase database, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            logger.LogDebug("Database connectivity test passed for: {DatabaseName}", database.Name);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to database '{database.Name}'. Ensure the database is accessible.", ex);
        }
    }
}
