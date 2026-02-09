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
    /// <summary>When schema deployment is enabled, then Postgres outbox registers schema completion and background services.</summary>
    /// <intent>Validate service registration for the outbox schema deployment path.</intent>
    /// <scenario>Given a ServiceCollection and PostgresOutboxOptions with EnableSchemaDeployment set to true.</scenario>
    /// <behavior>The service collection contains IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService registrations.</behavior>
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

    /// <summary>When schema deployment is disabled, then Postgres outbox does not register schema services.</summary>
    /// <intent>Validate service registration is skipped when schema deployment is off.</intent>
    /// <scenario>Given a ServiceCollection and PostgresOutboxOptions with EnableSchemaDeployment set to false.</scenario>
    /// <behavior>The service collection lacks IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService registrations.</behavior>
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

    /// <summary>When schema deployment is enabled, then schema completion is registered separately from the background service.</summary>
    /// <intent>Ensure completion services are singletons independent of the hosted service registration.</intent>
    /// <scenario>Given AddPostgresOutbox is called with EnableSchemaDeployment set to true.</scenario>
    /// <behavior>IDatabaseSchemaCompletion and DatabaseSchemaCompletion are singletons and the background service is registered.</behavior>
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

    /// <summary>When SetCompleted is called, then SchemaDeploymentCompleted finishes successfully.</summary>
    /// <intent>Verify schema completion signaling transitions the task to a completed state.</intent>
    /// <scenario>Given a new DatabaseSchemaCompletion instance with an incomplete task.</scenario>
    /// <behavior>The completion task becomes completed with status RanToCompletion.</behavior>
    [Fact]
    public void DatabaseSchemaCompletion_CoordinatesStateCorrectly()
    {
        var completion = new DatabaseSchemaCompletion();

        Assert.False(completion.SchemaDeploymentCompleted.IsCompleted);

        completion.SetCompleted();

        Assert.True(completion.SchemaDeploymentCompleted.IsCompleted);
        Assert.Equal(TaskStatus.RanToCompletion, completion.SchemaDeploymentCompleted.Status);
    }

    /// <summary>When list-based multi-database schema deployment is enabled, then schema services are registered.</summary>
    /// <intent>Confirm list-based multi-database registration wires schema deployment services.</intent>
    /// <scenario>Given a ServiceCollection, one PlatformDatabase entry, and enableSchemaDeployment set to true.</scenario>
    /// <behavior>IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService are registered.</behavior>
    [Fact]
    public void AddPostgresPlatformMultiDatabaseWithList_WithSchemaDeploymentEnabled_RegistersSchemaService()
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

        services.AddPostgresPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: true);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.NotNull(schemaCompletionDescriptor);
        Assert.NotNull(hostedServiceDescriptor);
    }

    /// <summary>When schema verification is enabled, then the verification service is registered.</summary>
    /// <intent>Ensure schema verification wiring is added when enabled.</intent>
    /// <scenario>Given AddPostgresPlatformMultiDatabaseWithList with enableSchemaVerification set to true.</scenario>
    /// <behavior>DatabaseSchemaVerificationService is registered in the service collection.</behavior>
    [Fact]
    public void AddPostgresPlatformMultiDatabaseWithList_WithSchemaVerificationEnabled_RegistersVerifier()
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

        services.AddPostgresPlatformMultiDatabaseWithList(
            databases,
            enableSchemaDeployment: false,
            enableSchemaVerification: true);

        var hostedServiceDescriptor = services.FirstOrDefault(
            s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaVerificationService));

        Assert.NotNull(hostedServiceDescriptor);
    }

    /// <summary>When control-plane schema deployment is enabled, then schema services are registered.</summary>
    /// <intent>Confirm control-plane multi-database registration wires schema deployment services.</intent>
    /// <scenario>Given a ServiceCollection, tenant list, and control-plane options with EnableSchemaDeployment set to true.</scenario>
    /// <behavior>IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService are registered.</behavior>
    [Fact]
    public void AddPostgresPlatformMultiDatabaseWithControlPlaneAndList_WithSchemaDeploymentEnabled_RegistersSchemaService()
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

        services.AddPostgresPlatformMultiDatabaseWithControlPlaneAndList(databases, controlPlaneOptions);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.NotNull(schemaCompletionDescriptor);
        Assert.NotNull(hostedServiceDescriptor);
    }

    /// <summary>When list-based schema deployment is disabled, then schema services are not registered.</summary>
    /// <intent>Confirm list-based multi-database registration skips schema deployment services.</intent>
    /// <scenario>Given a ServiceCollection, one PlatformDatabase entry, and enableSchemaDeployment set to false.</scenario>
    /// <behavior>IDatabaseSchemaCompletion and DatabaseSchemaBackgroundService are absent.</behavior>
    [Fact]
    public void AddPostgresPlatformMultiDatabaseWithList_WithSchemaDeploymentDisabled_DoesNotRegisterSchemaService()
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

        services.AddPostgresPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: false);

        var schemaCompletionDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseSchemaCompletion));
        var hostedServiceDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(DatabaseSchemaBackgroundService));

        Assert.Null(schemaCompletionDescriptor);
        Assert.Null(hostedServiceDescriptor);
    }
}

