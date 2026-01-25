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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.Tests;

public class DatabaseSchemaDeploymentTests
{
    [Fact]
    public void AddPostgresOutbox_WithSchemaDeploymentEnabled_RegistersSchemaService()
    {
        var services = new ServiceCollection();
        var options = new PostgresOutboxOptions
        {
            ConnectionString = "Host=localhost;Database=TestDb;Username=postgres;Password=postgres;",
            EnableSchemaDeployment = true,
        };

        services.AddPostgresOutbox(options);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.NotNull(schemaCompletionDescriptor);
        Assert.NotNull(hostedServiceDescriptor);
    }

    [Fact]
    public void AddPostgresOutbox_WithSchemaDeploymentDisabled_DoesNotRegisterSchemaService()
    {
        var services = new ServiceCollection();
        var options = new PostgresOutboxOptions
        {
            ConnectionString = "Host=localhost;Database=TestDb;Username=postgres;Password=postgres;",
            EnableSchemaDeployment = false,
        };

        services.AddPostgresOutbox(options);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.Null(schemaCompletionDescriptor);
        Assert.Null(hostedServiceDescriptor);
    }

    [Fact]
    public void SchemaCompletion_RegisteredSeparatelyFromBackgroundService()
    {
        var services = new ServiceCollection();
        var options = new PostgresOutboxOptions
        {
            ConnectionString = "Host=localhost;Database=TestDb;Username=postgres;Password=postgres;",
            EnableSchemaDeployment = true,
        };

        services.AddPostgresOutbox(options);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var databaseSchemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(DatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.NotNull(schemaCompletionDescriptor);
        Assert.NotNull(databaseSchemaCompletionDescriptor);
        Assert.NotNull(hostedServiceDescriptor);

        Assert.Equal(ServiceLifetime.Singleton, schemaCompletionDescriptor.Lifetime);
        Assert.Equal(ServiceLifetime.Singleton, databaseSchemaCompletionDescriptor.Lifetime);

        Assert.Null(schemaCompletionDescriptor.ImplementationType);
        Assert.NotNull(schemaCompletionDescriptor.ImplementationFactory);

        Assert.Equal(typeof(DatabaseSchemaCompletion), databaseSchemaCompletionDescriptor.ImplementationType);
    }

    [Fact]
    public void DatabaseSchemaCompletion_CoordinatesStateCorrectly()
    {
        var completion = new DatabaseSchemaCompletion();

        Assert.False(completion.SchemaDeploymentCompleted.IsCompleted);

        completion.SetCompleted();

        Assert.True(completion.SchemaDeploymentCompleted.IsCompleted);
        Assert.Equal(TaskStatus.RanToCompletion, completion.SchemaDeploymentCompleted.Status);
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithList_WithSchemaDeploymentEnabled_RegistersSchemaService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var databases = new[]
        {
            new PlatformDatabase
            {
                Name = "db1",
                ConnectionString = "Host=localhost;Database=Db1;Username=postgres;Password=postgres;",
                SchemaName = "infra",
            },
        };

        services.AddPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: true);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.NotNull(schemaCompletionDescriptor);
        Assert.NotNull(hostedServiceDescriptor);
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlane_WithSchemaDeploymentEnabled_RegistersSchemaService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var databases = new[]
        {
            new PlatformDatabase
            {
                Name = "db1",
                ConnectionString = "Host=localhost;Database=Db1;Username=postgres;Password=postgres;",
                SchemaName = "infra",
            },
        };

        var controlPlaneOptions = new PlatformControlPlaneOptions
        {
            ConnectionString = "Host=localhost;Database=ControlPlane;Username=postgres;Password=postgres;",
            SchemaName = "infra",
            EnableSchemaDeployment = true,
        };

        services.AddPlatformMultiDatabaseWithControlPlaneAndList(databases, controlPlaneOptions);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.NotNull(schemaCompletionDescriptor);
        Assert.NotNull(hostedServiceDescriptor);
    }

    [Fact]
    public void AddPlatformMultiDatabaseWithList_WithSchemaDeploymentDisabled_DoesNotRegisterSchemaService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var databases = new[]
        {
            new PlatformDatabase
            {
                Name = "db1",
                ConnectionString = "Host=localhost;Database=Db1;Username=postgres;Password=postgres;",
                SchemaName = "infra",
            },
        };

        services.AddPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: false);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.Null(schemaCompletionDescriptor);
        Assert.Null(hostedServiceDescriptor);
    }
}
