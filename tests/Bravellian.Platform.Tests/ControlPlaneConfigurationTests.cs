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


using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Tests for the new control plane configuration options.
/// </summary>
public class ControlPlaneConfigurationTests
{
    /// <summary>
    /// When list-based control plane registration specifies a schema name, then configuration and semaphore options use it.
    /// </summary>
    /// <intent>
    /// Verify control-plane schema settings are propagated to configuration and semaphore options.</intent>
    /// <scenario>
    /// Given AddSqlPlatformMultiDatabaseWithControlPlaneAndList called with control plane options specifying SchemaName = "control".
    /// </scenario>
    /// <behavior>
    /// Then PlatformConfiguration and SemaphoreOptions use the control plane schema and connection string.</behavior>
    [Fact]
    public void AddSqlPlatformMultiDatabaseWithControlPlaneAndList_WithOptions_ConfiguresSchemaName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var databases = new[]
        {
            new PlatformDatabase
            {
                Name = "db1",
                ConnectionString = "Server=localhost;Database=Db1;",
                SchemaName = "app",
            },
        };

        var controlPlaneOptions = new PlatformControlPlaneOptions
        {
            ConnectionString = "Server=localhost;Database=ControlPlane;",
            SchemaName = "control",
            EnableSchemaDeployment = false,
        };

        // Act
        services.AddSqlPlatformMultiDatabaseWithControlPlaneAndList(databases, controlPlaneOptions);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var config = serviceProvider.GetRequiredService<PlatformConfiguration>();
        config.ControlPlaneSchemaName.ShouldBe("control");
        config.ControlPlaneConnectionString.ShouldBe("Server=localhost;Database=ControlPlane;");

        // Verify semaphore options are configured with the control plane schema
        var semaphoreOptions = serviceProvider.GetRequiredService<IOptions<SemaphoreOptions>>();
        semaphoreOptions.Value.SchemaName.ShouldBe("control");
        semaphoreOptions.Value.ConnectionString.ShouldBe("Server=localhost;Database=ControlPlane;");
    }

    /// <summary>
    /// When discovery-based control plane registration specifies a schema name, then configuration and semaphore options use it.
    /// </summary>
    /// <intent>
    /// Verify control-plane settings flow through discovery-based registration.</intent>
    /// <scenario>
    /// Given AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery with a ListBasedDatabaseDiscovery and custom SchemaName.
    /// </scenario>
    /// <behavior>
    /// Then PlatformConfiguration reflects the schema and semaphore options use the same values.</behavior>
    [Fact]
    public void AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery_WithOptions_ConfiguresSchemaName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Register a mock discovery service
        services.AddSingleton<IPlatformDatabaseDiscovery>(
            new ListBasedDatabaseDiscovery(new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = "Server=localhost;Database=Db1;",
                    SchemaName = "app",
                },
            }));

        var controlPlaneOptions = new PlatformControlPlaneOptions
        {
            ConnectionString = "Server=localhost;Database=ControlPlane;",
            SchemaName = "custom_control",
            EnableSchemaDeployment = true,
        };

        // Act
        services.AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery(controlPlaneOptions);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var config = serviceProvider.GetRequiredService<PlatformConfiguration>();
        config.ControlPlaneSchemaName.ShouldBe("custom_control");
        config.ControlPlaneConnectionString.ShouldBe("Server=localhost;Database=ControlPlane;");
        config.EnableSchemaDeployment.ShouldBeTrue();

        // Verify semaphore options are configured with the control plane schema
        var semaphoreOptions = serviceProvider.GetRequiredService<IOptions<SemaphoreOptions>>();
        semaphoreOptions.Value.SchemaName.ShouldBe("custom_control");
        semaphoreOptions.Value.ConnectionString.ShouldBe("Server=localhost;Database=ControlPlane;");
    }

    /// <summary>
    /// When PlatformControlPlaneOptions is created without a schema name, then it defaults to "infra".
    /// </summary>
    /// <intent>
    /// Confirm control-plane options default schema aligns with platform conventions.</intent>
    /// <scenario>
    /// Given a PlatformControlPlaneOptions instance with only ConnectionString set.
    /// </scenario>
    /// <behavior>
    /// Then SchemaName is "infra".</behavior>
    [Fact]
    public void PlatformControlPlaneOptions_DefaultSchemaName_IsDbo()
    {
        // Arrange & Act
        var options = new PlatformControlPlaneOptions
        {
            ConnectionString = "Server=localhost;Database=Test;",
        };

        // Assert
        options.SchemaName.ShouldBe("infra");
    }

    /// <summary>
    /// When the obsolete list-based control plane overload is used, then it still wires defaults correctly.
    /// </summary>
    /// <intent>
    /// Ensure legacy registration paths continue to configure the control plane schema.</intent>
    /// <scenario>
    /// Given AddSqlPlatformMultiDatabaseWithControlPlaneAndList called via the obsolete signature.
    /// </scenario>
    /// <behavior>
    /// Then PlatformConfiguration and SemaphoreOptions default the schema to "infra".</behavior>
    [Fact]
    public void AddSqlPlatformMultiDatabaseWithControlPlaneAndList_OldSignature_StillWorks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var databases = new[]
        {
            new PlatformDatabase
            {
                Name = "db1",
                ConnectionString = "Server=localhost;Database=Db1;",
            },
        };

        // Act - Using the obsolete signature
#pragma warning disable CS0618 // Type or member is obsolete
        services.AddSqlPlatformMultiDatabaseWithControlPlaneAndList(
            databases,
            "Server=localhost;Database=ControlPlane;",
            enableSchemaDeployment: false);
#pragma warning restore CS0618 // Type or member is obsolete

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should default to "infra"
        var config = serviceProvider.GetRequiredService<PlatformConfiguration>();
        config.ControlPlaneSchemaName.ShouldBe("infra");

        var semaphoreOptions = serviceProvider.GetRequiredService<IOptions<SemaphoreOptions>>();
        semaphoreOptions.Value.SchemaName.ShouldBe("infra");
    }

    /// <summary>
    /// When the obsolete discovery-based control plane overload is used, then it still wires defaults correctly.
    /// </summary>
    /// <intent>
    /// Ensure legacy discovery registration continues to configure the control plane schema.</intent>
    /// <scenario>
    /// Given AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery called via the obsolete signature and a list-based discovery.
    /// </scenario>
    /// <behavior>
    /// Then PlatformConfiguration and SemaphoreOptions default the schema to "infra".</behavior>
    [Fact]
    public void AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery_OldSignature_StillWorks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSingleton<IPlatformDatabaseDiscovery>(
            new ListBasedDatabaseDiscovery(new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = "Server=localhost;Database=Db1;",
                },
            }));

        // Act - Using the obsolete signature
#pragma warning disable CS0618 // Type or member is obsolete
        services.AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery(
            "Server=localhost;Database=ControlPlane;",
            enableSchemaDeployment: false);
#pragma warning restore CS0618 // Type or member is obsolete

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should default to "infra"
        var config = serviceProvider.GetRequiredService<PlatformConfiguration>();
        config.ControlPlaneSchemaName.ShouldBe("infra");

        var semaphoreOptions = serviceProvider.GetRequiredService<IOptions<SemaphoreOptions>>();
        semaphoreOptions.Value.SchemaName.ShouldBe("infra");
    }
}

