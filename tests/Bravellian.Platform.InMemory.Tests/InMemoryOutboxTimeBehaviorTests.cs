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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Bravellian.Platform.Tests;

[Trait("Category", "Unit")]
public sealed class InMemoryOutboxTimeBehaviorTests
{
    [Fact]
    public async Task ClaimDueAsync_RespectsFakeTimeProvider()
    {
        var timeProvider = new FakeTimeProvider();
        using var provider = BuildProvider(timeProvider);

        var outbox = provider.GetRequiredService<IOutbox>();
        var storeProvider = provider.GetRequiredService<IOutboxStoreProvider>();
        var store = (await storeProvider.GetAllStoresAsync()).Single();

        var now = timeProvider.GetUtcNow();
        var dueTime = now.AddMinutes(5);

        await outbox.EnqueueAsync("Test.Topic", "payload", correlationId: null, dueTimeUtc: dueTime, TestContext.Current.CancellationToken);

        var messages = await store.ClaimDueAsync(10, TestContext.Current.CancellationToken);
        messages.Count.ShouldBe(0);

        timeProvider.Advance(TimeSpan.FromMinutes(5));

        messages = await store.ClaimDueAsync(10, TestContext.Current.CancellationToken);
        messages.Count.ShouldBe(1);
    }

    private static ServiceProvider BuildProvider(FakeTimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddInMemoryPlatformMultiDatabaseWithList(new[]
        {
            new InMemoryPlatformDatabase { Name = "default" },
        });

        services.AddTimeAbstractions(timeProvider);

        return services.BuildServiceProvider();
    }
}
