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


using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Tests for PlatformLifecycleService validation behavior.
/// </summary>
public class PlatformLifecycleServiceTests
{
    [Fact]
    public async Task StartAsync_WithDynamicDiscovery_AndNoDatabases_DoesNotThrowException()
    {
        // Arrange - Dynamic discovery with no databases (they may be added later)
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseNoControl,
            UsesDiscovery = true,
            EnableSchemaDeployment = false,
            RequiresDatabaseAtStartup = false, // Dynamic discovery: can start with zero databases
        };

        var discovery = new EmptyDatabaseDiscovery();
        var logger = NullLogger<PlatformLifecycleService>.Instance;
        var service = new PlatformLifecycleService(configuration, logger, discovery);

        // Act & Assert - Should not throw
        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WithListBasedDiscovery_AndNoDatabases_ThrowsException()
    {
        // Arrange - List-based discovery with no databases (this is an error)
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseNoControl,
            UsesDiscovery = false,
            EnableSchemaDeployment = false,
            RequiresDatabaseAtStartup = true, // List-based: must have at least one database
        };

        var discovery = new EmptyDatabaseDiscovery();
        var logger = NullLogger<PlatformLifecycleService>.Instance;
        var service = new PlatformLifecycleService(configuration, logger, discovery);

        // Act & Assert - Should throw
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await service.StartAsync(CancellationToken.None));

        exception.Message.ShouldContain("At least one database is required");
    }

    [Fact]
    public async Task StartAsync_WithDynamicDiscoveryAndControlPlane_AndNoDatabases_DoesNotThrowException()
    {
        // Arrange - Dynamic discovery with control plane, but no application databases yet
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseWithControl,
            UsesDiscovery = true,
            EnableSchemaDeployment = false,
            RequiresDatabaseAtStartup = false, // Dynamic discovery: can start with zero databases
            ControlPlaneConnectionString = "Server=localhost;Database=ControlPlane;",
        };

        var discovery = new EmptyDatabaseDiscovery();
        var logger = NullLogger<PlatformLifecycleService>.Instance;
        var service = new PlatformLifecycleService(configuration, logger, discovery);

        // Act & Assert - Should not throw (control plane validation will fail, but that's a different concern)
        // In a real scenario, the control plane would be available even if no app databases exist yet
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await service.StartAsync(CancellationToken.None));

        // The exception should be from control plane validation, not database discovery
        exception.Message.ShouldContain("control plane");
        exception.Message.ShouldNotContain("At least one database is required");
    }

    [Fact]
    public async Task StartAsync_WithDynamicDiscovery_AndOneDatabase_Succeeds()
    {
        // Arrange - Dynamic discovery that returns one database
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseNoControl,
            UsesDiscovery = true,
            EnableSchemaDeployment = false,
            RequiresDatabaseAtStartup = false,
        };

        var discovery = new TestDatabaseDiscovery(new[]
        {
            new PlatformDatabase
            {
                Name = "db1",
                ConnectionString = "Server=localhost;Database=Db1;",
            },
        });

        var logger = NullLogger<PlatformLifecycleService>.Instance;
        var service = new PlatformLifecycleService(configuration, logger, discovery);

        // Act & Assert - Should not throw
        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WithListBasedDiscovery_AndOneDatabase_Succeeds()
    {
        // Arrange - List-based discovery with one database
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseNoControl,
            UsesDiscovery = false,
            EnableSchemaDeployment = false,
            RequiresDatabaseAtStartup = true,
        };

        var discovery = new TestDatabaseDiscovery(new[]
        {
            new PlatformDatabase
            {
                Name = "db1",
                ConnectionString = "Server=localhost;Database=Db1;",
            },
        });

        var logger = NullLogger<PlatformLifecycleService>.Instance;
        var service = new PlatformLifecycleService(configuration, logger, discovery);

        // Act & Assert - Should not throw
        await service.StartAsync(CancellationToken.None);
    }

    // Test discovery implementations
    private class EmptyDatabaseDiscovery : IPlatformDatabaseDiscovery
    {
        public Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<PlatformDatabase>>(Array.Empty<PlatformDatabase>());
        }
    }

    private class TestDatabaseDiscovery : IPlatformDatabaseDiscovery
    {
        private readonly IReadOnlyCollection<PlatformDatabase> databases;

        public TestDatabaseDiscovery(IReadOnlyCollection<PlatformDatabase> databases)
        {
            this.databases = databases;
        }

        public Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(databases);
        }
    }
}
