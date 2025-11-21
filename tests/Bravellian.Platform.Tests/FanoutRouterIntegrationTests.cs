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


using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Tests for multi-database fanout configuration and routing.
/// </summary>
public class FanoutRouterIntegrationTests
{
    private readonly ITestOutputHelper testOutputHelper;

    public FanoutRouterIntegrationTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    [Fact]
    public async Task AddMultiSqlFanout_WithListOfOptions_RegistersServicesCorrectly()
    {
        // Arrange
        var fanoutOptions = new[]
        {
            new SqlFanoutOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                PolicyTableName = "FanoutPolicy",
                CursorTableName = "FanoutCursor",
                EnableSchemaDeployment = false,
            },
            new SqlFanoutOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "dbo",
                PolicyTableName = "FanoutPolicy",
                CursorTableName = "FanoutCursor",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();

        // Act - Create the provider using the same logic as the extension method
        var repositoryProvider = new ConfiguredFanoutRepositoryProvider(fanoutOptions, loggerFactory);
        var router = new FanoutRouter(repositoryProvider, loggerFactory.CreateLogger<FanoutRouter>());

        // Assert - Verify the provider was created correctly
        var policyRepos = await repositoryProvider.GetAllPolicyRepositoriesAsync();
        policyRepos.ShouldNotBeNull();
        policyRepos.Count.ShouldBe(2);

        var cursorRepos = await repositoryProvider.GetAllCursorRepositoriesAsync();
        cursorRepos.ShouldNotBeNull();
        cursorRepos.Count.ShouldBe(2);

        // Verify router can get repositories for both tenants
        var tenant1Policy = router.GetPolicyRepository("Tenant1");
        var tenant2Policy = router.GetPolicyRepository("Tenant2");

        tenant1Policy.ShouldNotBeNull();
        tenant2Policy.ShouldNotBeNull();
        tenant1Policy.ShouldNotBe(tenant2Policy);

        var tenant1Cursor = router.GetCursorRepository("Tenant1");
        var tenant2Cursor = router.GetCursorRepository("Tenant2");

        tenant1Cursor.ShouldNotBeNull();
        tenant2Cursor.ShouldNotBeNull();
        tenant1Cursor.ShouldNotBe(tenant2Cursor);

        testOutputHelper.WriteLine("AddMultiSqlFanout pattern successfully creates functional components");
    }

    [Fact]
    public async Task AddMultiSqlFanout_RepositoryProvider_ReturnsCorrectIdentifiers()
    {
        // Arrange
        var fanoutOptions = new[]
        {
            new SqlFanoutOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
            new SqlFanoutOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();

        // Act
        var repositoryProvider = new ConfiguredFanoutRepositoryProvider(fanoutOptions, loggerFactory);
        var policyRepositories = await repositoryProvider.GetAllPolicyRepositoriesAsync();
        var cursorRepositories = await repositoryProvider.GetAllCursorRepositoriesAsync();

        // Assert
        policyRepositories.Count.ShouldBe(2);
        cursorRepositories.Count.ShouldBe(2);

        var identifier1 = repositoryProvider.GetRepositoryIdentifier(policyRepositories[0]);
        var identifier2 = repositoryProvider.GetRepositoryIdentifier(policyRepositories[1]);

        identifier1.ShouldNotBeNullOrWhiteSpace();
        identifier2.ShouldNotBeNullOrWhiteSpace();
        identifier1.ShouldNotBe(identifier2);

        testOutputHelper.WriteLine($"Repository identifiers: {identifier1}, {identifier2}");
    }

    [Fact]
    public void FanoutRouter_GetPolicyRepository_ReturnsCorrectRepository()
    {
        // Arrange
        var fanoutOptions = new[]
        {
            new SqlFanoutOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();
        var repositoryProvider = new ConfiguredFanoutRepositoryProvider(fanoutOptions, loggerFactory);

        // Act
        var router = new FanoutRouter(repositoryProvider, loggerFactory.CreateLogger<FanoutRouter>());
        var policyRepo = router.GetPolicyRepository("Tenant1");
        var cursorRepo = router.GetCursorRepository("Tenant1");

        // Assert
        policyRepo.ShouldNotBeNull();
        cursorRepo.ShouldNotBeNull();
    }

    [Fact]
    public void FanoutRouter_GetPolicyRepository_ThrowsWhenKeyNotFound()
    {
        // Arrange
        var fanoutOptions = new[]
        {
            new SqlFanoutOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();
        var repositoryProvider = new ConfiguredFanoutRepositoryProvider(fanoutOptions, loggerFactory);

        // Act
        var router = new FanoutRouter(repositoryProvider, loggerFactory.CreateLogger<FanoutRouter>());

        // Assert
        Should.Throw<InvalidOperationException>(() => router.GetPolicyRepository("NonExistentKey"))
            .Message.ShouldContain("NonExistentKey");

        Should.Throw<InvalidOperationException>(() => router.GetCursorRepository("NonExistentKey"))
            .Message.ShouldContain("NonExistentKey");
    }

    [Fact]
    public async Task AddDynamicMultiSqlFanout_RegistersServicesCorrectly()
    {
        // Arrange
        var mockDiscovery = new MockFanoutDatabaseDiscovery();
        var timeProvider = TimeProvider.System;
        var loggerFactory = CreateLoggerFactory();

        // Act - Create the provider using the same logic as the extension method
        var repositoryProvider = new DynamicFanoutRepositoryProvider(
            mockDiscovery,
            timeProvider,
            loggerFactory,
            loggerFactory.CreateLogger<DynamicFanoutRepositoryProvider>());

        // Assert
        repositoryProvider.ShouldNotBeNull();

        // Trigger a refresh to load databases
        var policyRepos = await repositoryProvider.GetAllPolicyRepositoriesAsync();
        policyRepos.ShouldNotBeNull();
        policyRepos.Count.ShouldBe(2);

        var cursorRepos = await repositoryProvider.GetAllCursorRepositoriesAsync();
        cursorRepos.ShouldNotBeNull();
        cursorRepos.Count.ShouldBe(2);

        testOutputHelper.WriteLine("AddDynamicMultiSqlFanout pattern successfully creates functional components");
    }

    private class MockFanoutDatabaseDiscovery : IFanoutDatabaseDiscovery
    {
        public Task<IEnumerable<FanoutDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
        {
            var configs = new[]
            {
                new FanoutDatabaseConfig
                {
                    Identifier = "MockTenant1",
                    ConnectionString = "Server=localhost;Database=MockTenant1;",
                },
                new FanoutDatabaseConfig
                {
                    Identifier = "MockTenant2",
                    ConnectionString = "Server=localhost;Database=MockTenant2;",
                },
            };

            return Task.FromResult<IEnumerable<FanoutDatabaseConfig>>(configs);
        }
    }
}
