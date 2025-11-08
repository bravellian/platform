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

using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

/// <summary>
/// Tests for the new control plane configuration options.
/// </summary>
public class ControlPlaneConfigurationTests
{
    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlaneAndList_WithOptions_ConfiguresSchemaName()
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
        services.AddPlatformMultiDatabaseWithControlPlaneAndList(databases, controlPlaneOptions);

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

    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlaneAndDiscovery_WithOptions_ConfiguresSchemaName()
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
        services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(controlPlaneOptions);

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

    [Fact]
    public void PlatformControlPlaneOptions_DefaultSchemaName_IsDbo()
    {
        // Arrange & Act
        var options = new PlatformControlPlaneOptions
        {
            ConnectionString = "Server=localhost;Database=Test;",
        };

        // Assert
        options.SchemaName.ShouldBe("dbo");
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlaneAndList_OldSignature_StillWorks()
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
        services.AddPlatformMultiDatabaseWithControlPlaneAndList(
            databases,
            "Server=localhost;Database=ControlPlane;",
            enableSchemaDeployment: false);
#pragma warning restore CS0618 // Type or member is obsolete

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should default to "dbo"
        var config = serviceProvider.GetRequiredService<PlatformConfiguration>();
        config.ControlPlaneSchemaName.ShouldBe("dbo");

        var semaphoreOptions = serviceProvider.GetRequiredService<IOptions<SemaphoreOptions>>();
        semaphoreOptions.Value.SchemaName.ShouldBe("dbo");
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlaneAndDiscovery_OldSignature_StillWorks()
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
        services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
            "Server=localhost;Database=ControlPlane;",
            enableSchemaDeployment: false);
#pragma warning restore CS0618 // Type or member is obsolete

        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should default to "dbo"
        var config = serviceProvider.GetRequiredService<PlatformConfiguration>();
        config.ControlPlaneSchemaName.ShouldBe("dbo");

        var semaphoreOptions = serviceProvider.GetRequiredService<IOptions<SemaphoreOptions>>();
        semaphoreOptions.Value.SchemaName.ShouldBe("dbo");
    }
}
