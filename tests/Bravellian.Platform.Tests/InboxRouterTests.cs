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

namespace Bravellian.Platform.Tests;

public class InboxRouterTests
{
    private readonly ITestOutputHelper testOutputHelper;

    public InboxRouterTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    /// <summary>When a configured tenant key is requested, then the router returns an inbox instance.</summary>
    /// <intent>Verify inbox routing by string key for configured stores.</intent>
    /// <scenario>Given a ConfiguredInboxWorkStoreProvider built from two SqlInboxOptions and a test logger.</scenario>
    /// <behavior>Then GetInbox returns a non-null inbox for the matching tenant.</behavior>
    [Fact]
    public void InboxRouter_WithValidKey_ReturnsInbox()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "infra",
                TableName = "Inbox",
            },
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "infra",
                TableName = "Inbox",
            },
        };

        var loggerFactory = new TestLoggerFactory(testOutputHelper);
        var provider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);
        var router = new InboxRouter(provider);

        // Act
        var inbox = router.GetInbox("Tenant1");

        // Assert
        inbox.ShouldNotBeNull();
    }

    /// <summary>When a Guid tenant key is requested, then the router returns an inbox instance.</summary>
    /// <intent>Verify inbox routing by Guid key converted to string.</intent>
    /// <scenario>Given a ConfiguredInboxWorkStoreProvider configured with a Guid-based connection string.</scenario>
    /// <behavior>Then GetInbox(Guid) returns a non-null inbox.</behavior>
    [Fact]
    public void InboxRouter_WithGuidKey_ReturnsInbox()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = $"Server=localhost;Database={tenantId};",
                SchemaName = "infra",
                TableName = "Inbox",
            },
        };

        var loggerFactory = new TestLoggerFactory(testOutputHelper);
        var provider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);
        var router = new InboxRouter(provider);

        // Act
        var inbox = router.GetInbox(tenantId);

        // Assert
        inbox.ShouldNotBeNull();
    }

    /// <summary>When an unknown tenant key is requested, then the router throws an InvalidOperationException.</summary>
    /// <intent>Ensure the router fails fast for missing tenant entries.</intent>
    /// <scenario>Given a provider with one configured tenant and a non-existent tenant key.</scenario>
    /// <behavior>Then GetInbox throws InvalidOperationException.</behavior>
    [Fact]
    public void InboxRouter_WithInvalidKey_ThrowsException()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "infra",
                TableName = "Inbox",
            },
        };

        var loggerFactory = new TestLoggerFactory(testOutputHelper);
        var provider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);
        var router = new InboxRouter(provider);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => router.GetInbox("NonExistentTenant"));
    }

    /// <summary>When an empty tenant key is requested, then the router throws an ArgumentException.</summary>
    /// <intent>Validate tenant key input guarding.</intent>
    /// <scenario>Given a router configured with a single tenant and an empty key input.</scenario>
    /// <behavior>Then GetInbox throws ArgumentException.</behavior>
    [Fact]
    public void InboxRouter_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "infra",
                TableName = "Inbox",
            },
        };

        var loggerFactory = new TestLoggerFactory(testOutputHelper);
        var provider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);
        var router = new InboxRouter(provider);

        // Act & Assert
        Should.Throw<ArgumentException>(() => router.GetInbox(string.Empty));
    }

    /// <summary>When the router is constructed with a null provider, then it throws an ArgumentNullException.</summary>
    /// <intent>Ensure InboxRouter requires a non-null store provider.</intent>
    /// <scenario>Given an attempt to construct InboxRouter with a null IInboxWorkStoreProvider.</scenario>
    /// <behavior>Then the constructor throws ArgumentNullException.</behavior>
    [Fact]
    public void InboxRouter_WithNullProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new InboxRouter(null!));
    }
}
