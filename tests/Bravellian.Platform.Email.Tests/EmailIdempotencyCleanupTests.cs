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

using Bravellian.Platform.Email.AspNetCore;
using Bravellian.Platform.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Bravellian.Platform.Email.Tests;

public sealed class EmailIdempotencyCleanupTests
{
    [Fact]
    public void AddEmailIdempotencyCleanupHostedService_ThrowsForInvalidRetention()
    {
        var services = new ServiceCollection();

        Should.Throw<OptionsValidationException>(() =>
            services.AddBravellianEmailIdempotencyCleanupHostedService(options =>
            {
                options.RetentionPeriod = TimeSpan.Zero;
            }));
    }

    [Fact]
    public void AddEmailIdempotencyCleanupHostedService_ThrowsForInvalidInterval()
    {
        var services = new ServiceCollection();

        Should.Throw<OptionsValidationException>(() =>
            services.AddBravellianEmailIdempotencyCleanupHostedService(options =>
            {
                options.CleanupInterval = TimeSpan.Zero;
            }));
    }

    [Fact]
    public async Task CleanupService_InvokesCleanupOnSchedule()
    {
        var store = new FakeCleanupStore();
        var provider = new FakeStoreProvider(store);
        var options = Options.Create(new EmailIdempotencyCleanupOptions
        {
            RetentionPeriod = TimeSpan.FromSeconds(1),
            CleanupInterval = TimeSpan.FromMilliseconds(50)
        });
        var mono = new FixedMonotonicClock();
        var logger = NullLogger<EmailIdempotencyCleanupService>.Instance;
        using var service = new EmailIdempotencyCleanupService(options, provider, mono, logger);

        using var cts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken,
            cts.Token);

        await service.StartAsync(linkedCts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(140), linkedCts.Token);
        await cts.CancelAsync();

        await service.StopAsync(TestContext.Current.CancellationToken);

        store.CleanupCalls.ShouldBeGreaterThan(0);
    }

    private sealed class FakeCleanupStore : IIdempotencyStore, IIdempotencyCleanupStore
    {
        public int CleanupCalls { get; private set; }

        public Task<int> CleanupAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken)
        {
            CleanupCalls++;
            return Task.FromResult(1);
        }

        public Task<bool> TryBeginAsync(string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task CompleteAsync(string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task FailAsync(string key, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeStoreProvider : IIdempotencyStoreProvider
    {
        private readonly IIdempotencyStore store;

        public FakeStoreProvider(IIdempotencyStore store)
        {
            this.store = store;
        }

        public Task<IReadOnlyList<IIdempotencyStore>> GetAllStoresAsync()
        {
            return Task.FromResult<IReadOnlyList<IIdempotencyStore>>(new[] { store });
        }

        public string GetStoreIdentifier(IIdempotencyStore store)
        {
            return "store-1";
        }

        public IIdempotencyStore? GetStoreByKey(string key)
        {
            return store;
        }
    }

    private sealed class FixedMonotonicClock : IMonotonicClock
    {
        public long Ticks => 0;

        public double Seconds => 0;
    }
}
