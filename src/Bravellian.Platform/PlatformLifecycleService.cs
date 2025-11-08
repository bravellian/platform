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

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        this.logger.LogInformation(
            "Platform starting with environment style: {EnvironmentStyle}, Discovery: {UsesDiscovery}",
            this.configuration.EnvironmentStyle,
            this.configuration.UsesDiscovery);

        // Validate configuration based on environment style
        switch (this.configuration.EnvironmentStyle)
        {
            case PlatformEnvironmentStyle.SingleDatabase:
                await this.ValidateSingleDatabaseAsync(cancellationToken);
                break;

            case PlatformEnvironmentStyle.MultiDatabaseNoControl:
            case PlatformEnvironmentStyle.MultiDatabaseWithControl:
                await this.ValidateMultiDatabaseAsync(cancellationToken);
                break;
        }

        // Validate control plane if configured
        if (this.configuration.ControlPlaneConnectionString != null)
        {
            await this.ValidateControlPlaneAsync(cancellationToken);
        }

        this.logger.LogInformation("Platform startup validation completed successfully.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        this.logger.LogInformation("Platform lifecycle service stopping.");
        return Task.CompletedTask;
    }

    private async Task ValidateSingleDatabaseAsync(CancellationToken cancellationToken)
    {
        if (this.discovery == null)
        {
            throw new InvalidOperationException("Discovery must be configured for single database environment.");
        }

        var databases = await this.discovery.DiscoverDatabasesAsync(cancellationToken);

        if (databases.Count == 0)
        {
            throw new InvalidOperationException("No databases discovered for single database environment.");
        }

        if (databases.Count > 1)
        {
            throw new InvalidOperationException(
                $"Single database environment discovered {databases.Count} databases. Expected exactly 1.");
        }

        var database = databases.Single();
        this.logger.LogInformation(
            "Single database validated: Name={DatabaseName}, Schema={SchemaName}",
            database.Name,
            database.SchemaName);

        // Optional: Test connectivity
        await this.TestDatabaseConnectivityAsync(database, cancellationToken);
    }

    private async Task ValidateMultiDatabaseAsync(CancellationToken cancellationToken)
    {
        if (this.discovery == null)
        {
            throw new InvalidOperationException("Discovery must be configured for multi-database environment.");
        }

        var databases = await this.discovery.DiscoverDatabasesAsync(cancellationToken);

        if (databases.Count == 0)
        {
            throw new InvalidOperationException(
                "Multi-database discovery returned no databases. At least one database is required.");
        }

        this.logger.LogInformation(
            "Multi-database validated: Count={DatabaseCount}, Control Plane Configured={HasControlPlane}",
            databases.Count,
            this.configuration.ControlPlaneConnectionString != null);

        // Log database names (redacted if necessary)
        foreach (var db in databases)
        {
            this.logger.LogDebug("Discovered database: Name={DatabaseName}, Schema={SchemaName}", db.Name, db.SchemaName);
        }
    }

    private async Task ValidateControlPlaneAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(this.configuration.ControlPlaneConnectionString))
        {
            throw new InvalidOperationException("Control plane connection string is null or empty.");
        }

        this.logger.LogInformation("Validating control plane connectivity...");

        try
        {
            await using var connection = new SqlConnection(this.configuration.ControlPlaneConnectionString);
            await connection.OpenAsync(cancellationToken);

            this.logger.LogInformation("Control plane connectivity validated successfully.");
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

            this.logger.LogDebug("Database connectivity test passed for: {DatabaseName}", database.Name);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to connect to database '{database.Name}'. Ensure the database is accessible.", ex);
        }
    }
}
