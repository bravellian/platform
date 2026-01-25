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


using System.Data;
using Bravellian.Platform.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.Tests;

public class PlatformRegistrationTests
{
    /// <summary>When a single database is registered via AddPlatformMultiDatabaseWithList, then core configuration and time abstractions are registered.</summary>
    /// <intent>Verify the multi-database list registration sets configuration, discovery, and timing services.</intent>
    /// <scenario>Given a service collection configured with one PlatformDatabase entry.</scenario>
    /// <behavior>Then PlatformConfiguration, IPlatformDatabaseDiscovery, TimeProvider, and IMonotonicClock resolve with expected flags.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithList_SingleDatabase_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - Test that single database scenarios work with multi-database code
        services.AddPlatformMultiDatabaseWithList(new[]
        {
            new PlatformDatabase { Name = "default", ConnectionString = "Server=localhost;Database=Test;", SchemaName = "infra" },
        });

        // Assert
        // Should register configuration
        var config = GetRequiredService<PlatformConfiguration>(services);
        Assert.NotNull(config);
        Assert.Equal(PlatformEnvironmentStyle.MultiDatabaseNoControl, config.EnvironmentStyle);
        Assert.False(config.UsesDiscovery);

        // Should register discovery
        var discovery = GetRequiredService<IPlatformDatabaseDiscovery>(services);
        Assert.NotNull(discovery);

        // Should register time abstractions
        var timeProvider = GetRequiredService<TimeProvider>(services);
        Assert.NotNull(timeProvider);

        var clock = GetRequiredService<IMonotonicClock>(services);
        Assert.NotNull(clock);
    }

    /// <summary>When AddPlatformMultiDatabaseWithList is called twice, then it throws an InvalidOperationException.</summary>
    /// <intent>Prevent duplicate multi-database list registration.</intent>
    /// <scenario>Given a service collection that already called AddPlatformMultiDatabaseWithList once.</scenario>
    /// <behavior>Then the second call throws and the message mentions it was already called.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithList_CalledTwice_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPlatformMultiDatabaseWithList(new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db1;" },
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPlatformMultiDatabaseWithList(new[]
            {
                new PlatformDatabase { Name = "db2", ConnectionString = "Server=localhost;Database=Db2;" },
            }));

        Assert.Contains("already been called", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>When multiple databases are registered via AddPlatformMultiDatabaseWithList, then platform configuration and discovery are registered.</summary>
    /// <intent>Ensure list-based registration sets multi-database configuration without discovery mode.</intent>
    /// <scenario>Given two PlatformDatabase entries passed to AddPlatformMultiDatabaseWithList.</scenario>
    /// <behavior>Then PlatformConfiguration is set for multi-database without control and discovery resolves.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithList_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var databases = new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db1;" },
            new PlatformDatabase { Name = "db2", ConnectionString = "Server=localhost;Database=Db2;" },
        };

        // Act
        services.AddPlatformMultiDatabaseWithList(databases);

        // Assert
        var config = GetRequiredService<PlatformConfiguration>(services);
        Assert.NotNull(config);
        Assert.Equal(PlatformEnvironmentStyle.MultiDatabaseNoControl, config.EnvironmentStyle);
        Assert.False(config.UsesDiscovery);

        var discovery = GetRequiredService<IPlatformDatabaseDiscovery>(services);
        Assert.NotNull(discovery);
    }

    /// <summary>When AddPlatformMultiDatabaseWithList receives an empty list, then it throws an ArgumentException.</summary>
    /// <intent>Guard against registering multi-database services without any databases.</intent>
    /// <scenario>Given an empty PlatformDatabase array passed to AddPlatformMultiDatabaseWithList.</scenario>
    /// <behavior>Then an ArgumentException is thrown with a message indicating the list must not be empty.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithList_EmptyList_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        var databases = Array.Empty<PlatformDatabase>();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => services.AddPlatformMultiDatabaseWithList(databases));

        Assert.Contains("must not be empty", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>When AddPlatformMultiDatabaseWithList receives duplicate database names, then it throws an ArgumentException.</summary>
    /// <intent>Ensure database identifiers are unique in list-based registration.</intent>
    /// <scenario>Given two PlatformDatabase entries with the same Name value.</scenario>
    /// <behavior>Then AddPlatformMultiDatabaseWithList throws during discovery setup.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithList_DuplicateNames_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        var databases = new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db1;" },
            new PlatformDatabase { Name = "db1", ConnectionString = "Server=localhost;Database=Db2;" },
        };

        // Act & Assert - Should throw during ListBasedDatabaseDiscovery construction
        Assert.Throws<ArgumentException>(
            () => services.AddPlatformMultiDatabaseWithList(databases));
    }

    /// <summary>When AddPlatformMultiDatabaseWithDiscovery is used, then configuration is set for discovery-based multi-database mode.</summary>
    /// <intent>Verify discovery-based registration flips the UsesDiscovery flag.</intent>
    /// <scenario>Given a service collection with a test IPlatformDatabaseDiscovery implementation.</scenario>
    /// <behavior>Then PlatformConfiguration indicates multi-database without control and UsesDiscovery is true.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithDiscovery_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Register a test discovery implementation
        services.AddSingleton<IPlatformDatabaseDiscovery>(new TestDatabaseDiscovery());

        // Act
        services.AddPlatformMultiDatabaseWithDiscovery();

        // Assert
        var config = GetRequiredService<PlatformConfiguration>(services);
        Assert.NotNull(config);
        Assert.Equal(PlatformEnvironmentStyle.MultiDatabaseNoControl, config.EnvironmentStyle);
        Assert.True(config.UsesDiscovery);
    }

    /// <summary>When ListBasedDatabaseDiscovery is queried, then it returns the configured databases as-is.</summary>
    /// <intent>Confirm list-based discovery surfaces the configured database metadata.</intent>
    /// <scenario>Given a ListBasedDatabaseDiscovery initialized with two database entries.</scenario>
    /// <behavior>Then DiscoverDatabasesAsync returns both entries with matching names and connection strings.</behavior>
    [Fact]
    public async Task ListBasedDatabaseDiscovery_ReturnsConfiguredDatabasesAsync()
    {
        // Arrange
        var databases = new[]
        {
            new PlatformDatabase { Name = "db1", ConnectionString = "conn1", SchemaName = "infra" },
            new PlatformDatabase { Name = "db2", ConnectionString = "conn2", SchemaName = "custom" },
        };

        var discovery = new ListBasedDatabaseDiscovery(databases);

        // Act
        var result = await discovery.DiscoverDatabasesAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, db => string.Equals(db.Name, "db1", StringComparison.Ordinal) && string.Equals(db.ConnectionString, "conn1", StringComparison.Ordinal));
        Assert.Contains(result, db => string.Equals(db.Name, "db2", StringComparison.Ordinal) && string.Equals(db.ConnectionString, "conn2", StringComparison.Ordinal));
    }

    private static T GetRequiredService<T>(IServiceCollection services)
        where T : notnull
    {
        using var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<T>();
    }

    // Test discovery implementation
    private class TestDatabaseDiscovery : IPlatformDatabaseDiscovery
    {
        public Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
        {
            var databases = new[]
            {
                new PlatformDatabase { Name = "test1", ConnectionString = "conn1" },
                new PlatformDatabase { Name = "test2", ConnectionString = "conn2" },
            };

            return Task.FromResult<IReadOnlyCollection<PlatformDatabase>>(databases);
        }
    }

    /// <summary>When control-plane discovery registration finds a direct IOutboxStore registration, then it throws an InvalidOperationException.</summary>
    /// <intent>Prevent mixing direct outbox store registrations with platform discovery mode.</intent>
    /// <scenario>Given a service collection with a discovery implementation and a dummy IOutboxStore registered.</scenario>
    /// <behavior>Then AddPlatformMultiDatabaseWithControlPlaneAndDiscovery throws with a message about direct stores.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlaneAndDiscovery_ThrowsWhenDirectOutboxStoreRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPlatformDatabaseDiscovery>(new TestDatabaseDiscovery());
        services.AddSingleton<IOutboxStore>(new DummyOutboxStore());

        var options = new PlatformControlPlaneOptions
        {
            ConnectionString = "Server=localhost;Database=ControlPlane;",
            SchemaName = "infra",
            EnableSchemaDeployment = false,
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(options));

        Assert.Contains("Direct IOutboxStore registrations are not supported", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>When control-plane discovery registration finds a direct IInboxWorkStore registration, then it throws an InvalidOperationException.</summary>
    /// <intent>Prevent mixing direct inbox store registrations with platform discovery mode.</intent>
    /// <scenario>Given a service collection with a discovery implementation and a dummy IInboxWorkStore registered.</scenario>
    /// <behavior>Then AddPlatformMultiDatabaseWithControlPlaneAndDiscovery throws with a message about direct stores.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlaneAndDiscovery_ThrowsWhenDirectInboxStoreRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPlatformDatabaseDiscovery>(new TestDatabaseDiscovery());
        services.AddSingleton<IInboxWorkStore>(new DummyInboxWorkStore());

        var options = new PlatformControlPlaneOptions
        {
            ConnectionString = "Server=localhost;Database=ControlPlane;",
            SchemaName = "infra",
            EnableSchemaDeployment = false,
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(options));

        Assert.Contains("Direct IInboxWorkStore registrations are not supported", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>When control-plane discovery registration finds a direct IOutbox registration, then it throws an InvalidOperationException.</summary>
    /// <intent>Ensure direct outbox services are not registered alongside platform discovery.</intent>
    /// <scenario>Given a service collection with a discovery implementation and a dummy IOutbox registered.</scenario>
    /// <behavior>Then AddPlatformMultiDatabaseWithControlPlaneAndDiscovery throws with a message about direct outboxes.</behavior>
    [Fact]
    public void AddPlatformMultiDatabaseWithControlPlaneAndDiscovery_ThrowsWhenDirectOutboxRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPlatformDatabaseDiscovery>(new TestDatabaseDiscovery());
        services.AddSingleton<IOutbox>(new DummyOutbox());

        var options = new PlatformControlPlaneOptions
        {
            ConnectionString = "Server=localhost;Database=ControlPlane;",
            SchemaName = "infra",
            EnableSchemaDeployment = false,
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(options));

        Assert.Contains("Direct IOutbox registrations are not supported", ex.Message, StringComparison.Ordinal);
    }

    private sealed class DummyOutboxStore : IOutboxStore
    {
        public Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DummyInboxWorkStore : IInboxWorkStore
    {
        public Task<IReadOnlyList<string>> ClaimAsync(Bravellian.Platform.OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AckAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AbandonAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, string? lastError = null, TimeSpan? delay = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task FailAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<string> messageIds, string error, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ReapExpiredAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InboxMessage> GetAsync(string messageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DummyOutbox : IOutbox
    {
        public Task EnqueueAsync(string topic, string payload, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueAsync(string topic, string payload, string? correlationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueAsync(string topic, string payload, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(Bravellian.Platform.OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AckAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AbandonAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task FailAsync(Bravellian.Platform.OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReapExpiredAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JoinIdentifier> StartJoinAsync(long tenantId, int expectedSteps, string? metadata, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AttachMessageToJoinAsync(Bravellian.Platform.Outbox.JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReportStepCompletedAsync(Bravellian.Platform.Outbox.JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReportStepFailedAsync(Bravellian.Platform.Outbox.JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
