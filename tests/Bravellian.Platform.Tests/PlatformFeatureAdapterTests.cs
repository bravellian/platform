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

namespace Bravellian.Platform.Tests;

public class PlatformFeatureAdapterTests
{
    [Fact]
    public void AddPlatformOutbox_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformOutbox();

        using var provider = services.BuildServiceProvider();

        var storeProvider = provider.GetRequiredService<IOutboxStoreProvider>();
        Assert.IsType<PlatformOutboxStoreProvider>(storeProvider);
    }

    [Fact]
    public void AddPlatformInbox_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformInbox();

        using var provider = services.BuildServiceProvider();

        var storeProvider = provider.GetRequiredService<IInboxWorkStoreProvider>();
        Assert.IsType<PlatformInboxWorkStoreProvider>(storeProvider);
    }

    [Fact]
    public void AddPlatformScheduler_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformScheduler();

        using var provider = services.BuildServiceProvider();

        var storeProvider = provider.GetRequiredService<ISchedulerStoreProvider>();
        Assert.IsType<PlatformSchedulerStoreProvider>(storeProvider);
    }

    [Fact]
    public void AddPlatformFanout_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformFanout();

        using var provider = services.BuildServiceProvider();

        var repositoryProvider = provider.GetRequiredService<IFanoutRepositoryProvider>();
        Assert.IsType<PlatformFanoutRepositoryProvider>(repositoryProvider);
    }

    [Fact]
    public void AddPlatformLeases_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformLeases();

        using var provider = services.BuildServiceProvider();

        var leaseProvider = provider.GetRequiredService<ILeaseFactoryProvider>();
        Assert.IsType<PlatformLeaseFactoryProvider>(leaseProvider);
    }

    private static ServiceCollection CreateBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton<IPlatformDatabaseDiscovery>(new StubDiscovery());
        services.AddSingleton(new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseWithControl,
            ControlPlaneConnectionString = "Server=localhost;Database=Control;Trusted_Connection=True;",
            ControlPlaneSchemaName = "dbo",
            UsesDiscovery = true,
        });

        return services;
    }

    private sealed class StubDiscovery : IPlatformDatabaseDiscovery
    {
        public Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
        {
            var databases = new List<PlatformDatabase>
            {
                new()
                {
                    Name = "tenant1",
                    ConnectionString = "Server=localhost;Database=Tenant1;Trusted_Connection=True;",
                    SchemaName = "dbo",
                },
                new()
                {
                    Name = "tenant2",
                    ConnectionString = "Server=localhost;Database=Tenant2;Trusted_Connection=True;",
                    SchemaName = "dbo",
                },
            };

            return Task.FromResult<IReadOnlyCollection<PlatformDatabase>>(databases);
        }
    }
}
