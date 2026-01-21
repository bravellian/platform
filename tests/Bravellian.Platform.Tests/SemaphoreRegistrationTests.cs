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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Tests for semaphore registration and control-plane integration.
/// </summary>
public class SemaphoreRegistrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer msSqlContainer;
    private string? connectionString;

    public SemaphoreRegistrationTests()
    {
        msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await msSqlContainer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        connectionString = msSqlContainer.GetConnectionString();
    }

    public async ValueTask DisposeAsync()
    {
        await msSqlContainer.DisposeAsync().ConfigureAwait(false);
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
                    ConnectionString = connectionString!,
                    SchemaName = "infra",
                },
            },
            enableSchemaDeployment: false);

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var semaphoreService = serviceProvider.GetService<ISemaphoreService>();
        semaphoreService.ShouldBeNull();
    }
}
