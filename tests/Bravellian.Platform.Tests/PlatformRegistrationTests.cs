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

namespace Bravellian.Platform.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public class PlatformRegistrationTests
{
    [Fact]
    public void AddPlatformMultiDatabaseWithList_SingleDatabase_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - Test that single database scenarios work with multi-database code
        services.AddPlatformMultiDatabaseWithList(new[]
        {
            new PlatformDatabase { Name = "default", ConnectionString = "Server=localhost;Database=Test;", SchemaName = "dbo" },
        });

        // Assert
        // Should register configuration
        var config = GetRequiredService<PlatformConfiguration>(services);
        Assert.NotNull(config);
        Assert.Equal(PlatformEnvironmentStyle.MultiDatabaseNoControl, config.EnvironmentStyle);
        Assert.False(config.UsesDiscovery);

        // Should register discovery
        var discovery = GetRequiredService<IPlatformDatabaseDiscovery>(services);
        Assert.NotNull(discovery);

        // Should register time abstractions
        var timeProvider = GetRequiredService<TimeProvider>(services);
        Assert.NotNull(timeProvider);

        var clock = GetRequiredService<IMonotonicClock>(services);
        Assert.NotNull(clock);
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithList_CalledTwice_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPlatformMultiDatabaseWithList(new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db1;" },
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPlatformMultiDatabaseWithList(new[]
            {
                new PlatformDatabase { Name = "db2", ConnectionString = "Server=localhost;Database=Db2;" },
            }));

        Assert.Contains("already been called", ex.Message);
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithList_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var databases = new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db1;" },
            new PlatformDatabase { Name = "db2", ConnectionString = "Server=localhost;Database=Db2;" },
        };

        // Act
        services.AddPlatformMultiDatabaseWithList(databases);

        // Assert
        var config = GetRequiredService<PlatformConfiguration>(services);
        Assert.NotNull(config);
        Assert.Equal(PlatformEnvironmentStyle.MultiDatabaseNoControl, config.EnvironmentStyle);
        Assert.False(config.UsesDiscovery);

        var discovery = GetRequiredService<IPlatformDatabaseDiscovery>(services);
        Assert.NotNull(discovery);
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithList_EmptyList_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        var databases = Array.Empty<PlatformDatabase>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => services.AddPlatformMultiDatabaseWithList(databases));

        Assert.Contains("must not be empty", ex.Message);
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithList_DuplicateNames_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        var databases = new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db1;" },
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db2;" },
        };

        // Act & Assert - Should throw during ListBasedDatabaseDiscovery construction
        Assert.Throws<ArgumentException>(
            () => services.AddPlatformMultiDatabaseWithList(databases));
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithDiscovery_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register a test discovery implementation
        services.AddSingleton<IPlatformDatabaseDiscovery>(new TestDatabaseDiscovery());

        // Act
        services.AddPlatformMultiDatabaseWithDiscovery();

        // Assert
        var config = GetRequiredService<PlatformConfiguration>(services);
        Assert.NotNull(config);
        Assert.Equal(PlatformEnvironmentStyle.MultiDatabaseNoControl, config.EnvironmentStyle);
        Assert.True(config.UsesDiscovery);
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlaneAndList_RegistersControlPlane()
    {
        // Arrange
        var services = new ServiceCollection();
        var databases = new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db1;" },
        };

        // Act
        services.AddPlatformMultiDatabaseWithControlPlaneAndList(
            databases,
            "Server=localhost;Database=ControlPlane;");

        // Assert
        var config = GetRequiredService<PlatformConfiguration>(services);
        Assert.NotNull(config);
        Assert.Equal(PlatformEnvironmentStyle.MultiDatabaseWithControl, config.EnvironmentStyle);
        Assert.NotNull(config.ControlPlaneConnectionString);
    }

    [Fact]
    public void ListBasedDatabaseDiscovery_ReturnsConfiguredDatabases()
    {
        // Arrange
        var databases = new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "conn1", SchemaName = "dbo" },
            new PlatformDatabase { Name = "db2", ConnectionString = "conn2", SchemaName = "custom" },
        };

        var discovery = new ListBasedDatabaseDiscovery(databases);

        // Act
        var result = discovery.DiscoverDatabasesAsync().GetAwaiter().GetResult();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, db => db.Name == "db1" && db.ConnectionString == "conn1");
        Assert.Contains(result, db => db.Name == "db2" && db.ConnectionString == "conn2");
    }

    private static T GetRequiredService<T>(IServiceCollection services)
        where T : notnull
    {
        using var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<T>();
    }

    // Test discovery implementation
    private class TestDatabaseDiscovery : IPlatformDatabaseDiscovery
    {
        public Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
        {
            var databases = new[]
            {
                new PlatformDatabase { Name = "test1", ConnectionString = "conn1" },
                new PlatformDatabase { Name = "test2", ConnectionString = "conn2" },
            };

            return Task.FromResult<IReadOnlyCollection<PlatformDatabase>>(databases);
        }
    }
}
