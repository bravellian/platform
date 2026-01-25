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
    /// <summary>When AddPlatformOutbox is registered, then the outbox store provider resolves as the platform implementation.</summary>
    /// <intent>Confirm platform outbox registration binds the provider to the platform adapter.</intent>
    /// <scenario>Given a service collection configured with a stub discovery and platform configuration.</scenario>
    /// <behavior>Then resolving IOutboxStoreProvider yields PlatformOutboxStoreProvider.</behavior>
    [Fact]
    public void AddPlatformOutbox_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformOutbox();

        using var provider = services.BuildServiceProvider();

        var storeProvider = provider.GetRequiredService<IOutboxStoreProvider>();
        Assert.IsType<PlatformOutboxStoreProvider>(storeProvider);
    }

    /// <summary>When AddPlatformInbox is registered, then the inbox work store provider resolves as the platform implementation.</summary>
    /// <intent>Confirm platform inbox registration binds the provider to the platform adapter.</intent>
    /// <scenario>Given a service collection configured with a stub discovery and platform configuration.</scenario>
    /// <behavior>Then resolving IInboxWorkStoreProvider yields PlatformInboxWorkStoreProvider.</behavior>
    [Fact]
    public void AddPlatformInbox_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformInbox();

        using var provider = services.BuildServiceProvider();

        var storeProvider = provider.GetRequiredService<IInboxWorkStoreProvider>();
        Assert.IsType<PlatformInboxWorkStoreProvider>(storeProvider);
    }

    /// <summary>When AddPlatformScheduler is registered, then the scheduler store provider resolves as the platform implementation.</summary>
    /// <intent>Confirm platform scheduler registration binds the provider to the platform adapter.</intent>
    /// <scenario>Given a service collection configured with a stub discovery and platform configuration.</scenario>
    /// <behavior>Then resolving ISchedulerStoreProvider yields PlatformSchedulerStoreProvider.</behavior>
    [Fact]
    public void AddPlatformScheduler_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformScheduler();

        using var provider = services.BuildServiceProvider();

        var storeProvider = provider.GetRequiredService<ISchedulerStoreProvider>();
        Assert.IsType<PlatformSchedulerStoreProvider>(storeProvider);
    }

    /// <summary>When AddPlatformFanout is registered, then the fanout repository provider resolves as the platform implementation.</summary>
    /// <intent>Confirm platform fanout registration binds the provider to the platform adapter.</intent>
    /// <scenario>Given a service collection configured with a stub discovery and platform configuration.</scenario>
    /// <behavior>Then resolving IFanoutRepositoryProvider yields PlatformFanoutRepositoryProvider.</behavior>
    [Fact]
    public void AddPlatformFanout_RegistersPlatformProvider()
    {
        var services = CreateBaseServices();

        services.AddPlatformFanout();

        using var provider = services.BuildServiceProvider();

        var repositoryProvider = provider.GetRequiredService<IFanoutRepositoryProvider>();
        Assert.IsType<PlatformFanoutRepositoryProvider>(repositoryProvider);
    }

    /// <summary>When AddPlatformLeases is registered, then the lease factory provider resolves as the platform implementation.</summary>
    /// <intent>Confirm platform lease registration binds the provider to the platform adapter.</intent>
    /// <scenario>Given a service collection configured with a stub discovery and platform configuration.</scenario>
    /// <behavior>Then resolving ILeaseFactoryProvider yields PlatformLeaseFactoryProvider.</behavior>
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
            ControlPlaneSchemaName = "infra",
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
                    SchemaName = "infra",
                },
                new()
                {
                    Name = "tenant2",
                    ConnectionString = "Server=localhost;Database=Tenant2;Trusted_Connection=True;",
                    SchemaName = "infra",
                },
            };

            return Task.FromResult<IReadOnlyCollection<PlatformDatabase>>(databases);
        }
    }
}
