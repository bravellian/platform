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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Bravellian.Platform.Postgres.Tests;

public class ControlPlaneGlobalSchedulerTests
{
    /// <summary>
    /// When control plane registration is used, then global scheduler services are available.
    /// </summary>
    /// <intent>Ensure global scheduler registrations are wired for control plane environments.</intent>
    /// <scenario>Given AddPostgresPlatformMultiDatabaseWithControlPlaneAndList called with valid options.</scenario>
    /// <behavior>Then the global scheduler, outbox store, and lease factory can be resolved.</behavior>
    [Fact]
    public void AddPostgresPlatformMultiDatabaseWithControlPlaneAndList_RegistersGlobalSchedulerServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var databases = new[]
        {
            new PlatformDatabase
            {
                Name = "db1",
                ConnectionString = "Host=localhost;Database=db1;",
                SchemaName = "app",
            },
        };

        var controlPlaneOptions = new PlatformControlPlaneOptions
        {
            ConnectionString = "Host=localhost;Database=control;",
            SchemaName = "control",
            EnableSchemaDeployment = false,
        };

        // Act
        services.AddPostgresPlatformMultiDatabaseWithControlPlaneAndList(databases, controlPlaneOptions);
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredService<IGlobalSchedulerClient>().ShouldNotBeNull();
        provider.GetRequiredService<IGlobalOutboxStore>().ShouldNotBeNull();
        provider.GetRequiredService<IGlobalSystemLeaseFactory>().ShouldNotBeNull();
    }
}
