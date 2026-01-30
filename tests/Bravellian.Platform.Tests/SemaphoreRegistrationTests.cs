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
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Tests for semaphore registration and control-plane integration.
/// </summary>
public class SemaphoreRegistrationTests : IAsyncLifetime
{
    private const string SaPassword = "Str0ng!Passw0rd!";
    private readonly IContainer msSqlContainer;
    private string? connectionString;

    public SemaphoreRegistrationTests()
    {
        msSqlContainer = new ContainerBuilder("mcr.microsoft.com/mssql/server:2022-CU10-ubuntu-22.04")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", SaPassword)
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithPortBinding(1433, true)
            .WithReuse(true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1433))
            .Build();
    }

    public async ValueTask InitializeAsync()
    {
        await msSqlContainer.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        connectionString = BuildConnectionString(msSqlContainer);
        await WaitForServerReadyAsync(connectionString, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await msSqlContainer.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>When multi-database registration omits a control plane, then ISemaphoreService is not registered.</summary>
    /// <intent>Ensure semaphore services remain global and require a control plane.</intent>
    /// <scenario>Given AddSqlPlatformMultiDatabaseWithList called without control-plane options.</scenario>
    /// <behavior>Then ISemaphoreService is not present in the service provider.</behavior>
    [Fact]
    public void SemaphoreService_NotRegistered_InMultiDatabaseWithoutControlPlane()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)); // Add null logger
        services.AddSqlPlatformMultiDatabaseWithList(
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

    private static string BuildConnectionString(IContainer container)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{container.Hostname},{container.GetMappedPublicPort(1433)}",
            UserID = "sa",
            Password = SaPassword,
            InitialCatalog = "master",
            Encrypt = false,
            TrustServerCertificate = true,
        };

        return builder.ConnectionString;
    }

    private static async Task WaitForServerReadyAsync(string connectionString, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(60);

        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            try
            {
                var connection = new SqlConnection(connectionString);
                await using (connection.ConfigureAwait(false))
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (SqlException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("SQL Server did not become available before the timeout.");
    }
}

