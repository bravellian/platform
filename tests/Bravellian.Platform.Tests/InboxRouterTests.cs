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

    [Fact]
    public void InboxRouter_WithValidKey_ReturnsInbox()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                TableName = "Inbox",
            },
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "dbo",
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
                SchemaName = "dbo",
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

    [Fact]
    public void InboxRouter_WithInvalidKey_ThrowsException()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                TableName = "Inbox",
            },
        };

        var loggerFactory = new TestLoggerFactory(testOutputHelper);
        var provider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);
        var router = new InboxRouter(provider);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => router.GetInbox("NonExistentTenant"));
    }

    [Fact]
    public void InboxRouter_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                TableName = "Inbox",
            },
        };

        var loggerFactory = new TestLoggerFactory(testOutputHelper);
        var provider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);
        var router = new InboxRouter(provider);

        // Act & Assert
        Should.Throw<ArgumentException>(() => router.GetInbox(string.Empty));
    }

    [Fact]
    public void InboxRouter_WithNullProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new InboxRouter(null!));
    }
}
