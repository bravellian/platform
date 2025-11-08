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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.MsSql;
using Xunit;

/// <summary>
/// Tests for semaphore registration and control-plane integration.
/// </summary>
public class SemaphoreRegistrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer msSqlContainer;
    private string? connectionString;

    public SemaphoreRegistrationTests()
    {
        this.msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await this.msSqlContainer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        this.connectionString = this.msSqlContainer.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        await this.msSqlContainer.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public void SemaphoreService_RegisteredOnlyInControlPlane_WithList()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)); // Add null logger
        services.AddPlatformMultiDatabaseWithControlPlaneAndList(
            new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = this.connectionString!,
                    SchemaName = "dbo",
                },
            },
            controlPlaneConnectionString: this.connectionString!,
            enableSchemaDeployment: false);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var semaphoreService = serviceProvider.GetService<ISemaphoreService>();
        semaphoreService.ShouldNotBeNull();
    }

    [Fact]
    public void SemaphoreService_RegisteredOnlyInControlPlane_WithDiscovery()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)); // Add null logger
        
        // Register a mock discovery service
        services.AddSingleton<IPlatformDatabaseDiscovery>(
            new ListBasedDatabaseDiscovery(new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = this.connectionString!,
                    SchemaName = "dbo",
                },
            }));

        // Act
        services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
            controlPlaneConnectionString: this.connectionString!,
            enableSchemaDeployment: false);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var semaphoreService = serviceProvider.GetService<ISemaphoreService>();
        semaphoreService.ShouldNotBeNull();
    }

    [Fact]
    public void SemaphoreService_Registered_InSingleDatabase()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)); // Add null logger
        services.AddPlatformSingleDatabase(
            connectionString: this.connectionString!,
            databaseName: "test",
            schemaName: "dbo",
            enableSchemaDeployment: false);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var semaphoreService = serviceProvider.GetService<ISemaphoreService>();
        semaphoreService.ShouldNotBeNull();
    }

    [Fact]
    public void SemaphoreService_NotRegistered_InMultiDatabaseWithoutControlPlane()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)); // Add null logger
        services.AddPlatformMultiDatabaseWithList(
            new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = this.connectionString!,
                    SchemaName = "dbo",
                },
            },
            enableSchemaDeployment: false);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var semaphoreService = serviceProvider.GetService<ISemaphoreService>();
        semaphoreService.ShouldBeNull();
    }

    [Fact]
    public void SemaphoreReaperService_RegisteredInControlPlane()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)); // Add null logger
        services.AddPlatformMultiDatabaseWithControlPlaneAndList(
            new[]
            {
                new PlatformDatabase
                {
                    Name = "db1",
                    ConnectionString = this.connectionString!,
                    SchemaName = "dbo",
                },
            },
            controlPlaneConnectionString: this.connectionString!,
            enableSchemaDeployment: false);

        // Assert - Check if SemaphoreReaperService is registered without building the service provider
        // which would try to resolve all dependencies
        var reaperServiceDescriptor = services.FirstOrDefault(s => s.ImplementationType == typeof(SemaphoreReaperService));
        reaperServiceDescriptor.ShouldNotBeNull();
        reaperServiceDescriptor.ServiceType.ShouldBe(typeof(IHostedService));
    }

    [Fact]
    public void SemaphoreReaperService_RegisteredInSingleDatabase()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)); // Add null logger
        services.AddPlatformSingleDatabase(
            connectionString: this.connectionString!,
            databaseName: "test",
            schemaName: "dbo",
            enableSchemaDeployment: false);

        // Assert - Check if SemaphoreReaperService is registered without building the service provider
        // which would try to resolve all dependencies
        var reaperServiceDescriptor = services.FirstOrDefault(s => s.ImplementationType == typeof(SemaphoreReaperService));
        reaperServiceDescriptor.ShouldNotBeNull();
        reaperServiceDescriptor.ServiceType.ShouldBe(typeof(IHostedService));
    }
}
