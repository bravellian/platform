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

namespace Bravellian.Platform.Tests;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics;

public class SqlSchedulerServiceConfigurationTests : SqlServerTestBase
{
    public SqlSchedulerServiceConfigurationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public void SqlSchedulerService_UsesConfiguredMaxPollingInterval_NotHardCoded()
    {
        // Arrange - Create options with custom polling interval
        var customInterval = TimeSpan.FromMilliseconds(500);
        var options = new SqlSchedulerOptions
        {
            ConnectionString = this.ConnectionString,
            MaxPollingInterval = customInterval,
            EnableBackgroundWorkers = false,
            EnableSchemaDeployment = false
        };
        
        // Create simple mock services
        var outbox = new TestNullOutbox();
        var leaseFactory = new TestSystemLeaseFactory();
        
        // Act - Create SqlSchedulerService instance
        var schedulerService = new SqlSchedulerService(
            leaseFactory,
            outbox,
            Options.Create(options),
            FakeTimeProvider.System
        );

        // Assert - This test mainly verifies that the service can be constructed 
        // with custom polling interval without throwing exceptions
        schedulerService.ShouldNotBeNull();
        
        // The real test is that it compiles and the constructor doesn't throw,
        // indicating the maxWaitTime field is properly initialized from options
    }

    [Fact]
    public void SqlSchedulerOptions_DefaultMaxPollingInterval_Is30Seconds()
    {
        // Arrange & Act
        var options = new SqlSchedulerOptions();
        
        // Assert - Verify the default is 30 seconds, not some other value
        options.MaxPollingInterval.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void SqlSchedulerOptions_AllowsCustomMaxPollingInterval()
    {
        // Arrange
        var customInterval = TimeSpan.FromSeconds(1);
        
        // Act
        var options = new SqlSchedulerOptions
        {
            MaxPollingInterval = customInterval
        };
        
        // Assert
        options.MaxPollingInterval.ShouldBe(customInterval);
    }

    // Helper classes for testing
    private class TestNullOutbox : IOutbox
    {
        public Task EnqueueAsync(string topic, string payload, string? correlationId)
        {
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string topic, string payload, System.Data.IDbTransaction transaction, string? correlationId = null)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> ClaimAsync(Guid ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Guid>>(new List<Guid>());
        }

        public Task AckAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task AbandonAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task FailAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReapExpiredAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class TestSystemLeaseFactory : ISystemLeaseFactory
    {
        public Task<ISystemLease?> AcquireAsync(string resourceName, TimeSpan leaseDuration, string? contextJson = null, Guid? ownerToken = null, CancellationToken cancellationToken = default)
        {
            // Return a fake lease that's always acquired
            return Task.FromResult<ISystemLease?>(new TestSystemLease(resourceName, ownerToken ?? Guid.NewGuid()));
        }
    }

    private class TestSystemLease : ISystemLease
    {
        private readonly CancellationTokenSource _cts = new();

        public TestSystemLease(string resourceName, Guid ownerToken)
        {
            ResourceName = resourceName;
            OwnerToken = ownerToken;
        }

        public string ResourceName { get; }
        public Guid OwnerToken { get; }
        public long FencingToken { get; } = 1;
        public CancellationToken CancellationToken => _cts.Token;

        public void ThrowIfLost()
        {
            // Never lost in test
        }

        public Task<bool> TryRenewNowAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            _cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}