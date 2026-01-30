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

using System.Linq;
using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.Tests.TestUtilities;

internal sealed class InMemoryInboxWorkStoreBehaviorHarness : IInboxWorkStoreBehaviorHarness
{
    private ServiceProvider? provider;
    private IInbox? inbox;
    private IInboxWorkStore? store;

    public IInbox Inbox => inbox ?? throw new InvalidOperationException("Harness has not been initialized.");

    public IInboxWorkStore WorkStore => store ?? throw new InvalidOperationException("Harness has not been initialized.");

    public ValueTask InitializeAsync() => new(ResetAsync());

    public ValueTask DisposeAsync()
    {
        provider?.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task ResetAsync()
    {
        provider?.Dispose();

        var services = new ServiceCollection();
        services.AddInMemoryPlatformMultiDatabaseWithList(new[]
        {
            new InMemoryPlatformDatabase { Name = "default" },
        });

        provider = services.BuildServiceProvider();
        inbox = provider.GetRequiredService<IInbox>();

        var storeProvider = provider.GetRequiredService<IInboxWorkStoreProvider>();
        var stores = await storeProvider.GetAllStoresAsync();
        store = stores.Single();
    }
}
