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

using Bravellian.Platform.Inbox;
using Bravellian.Platform.Metrics;
using Bravellian.Platform.Outbox;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Bravellian.Platform.Tests;

/// <summary>
/// Tests for Dapper type handlers for strongly-typed ID types.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DapperTypeHandlerTests : SqlServerTestBase
{
    public DapperTypeHandlerTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture sharedFixture)
        : base(testOutputHelper, sharedFixture)
    {
    }

    private async Task CreateTestTableAsync()
    {
        // Create a simple test table with various Guid columns
        var connection = new SqlConnection(ConnectionString);
        // Create a simple test table with various Guid columns
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(@"
            IF OBJECT_ID('TestTable', 'U') IS NOT NULL
                DROP TABLE TestTable;
            
            CREATE TABLE TestTable (
                Id INT PRIMARY KEY IDENTITY(1,1),
                OwnerTokenColumn UNIQUEIDENTIFIER,
                InboxMessageIdColumn UNIQUEIDENTIFIER,
                OutboxMessageIdColumn UNIQUEIDENTIFIER,
                OutboxWorkItemIdColumn UNIQUEIDENTIFIER,
                JoinIdColumn UNIQUEIDENTIFIER,
                InstanceIdColumn UNIQUEIDENTIFIER,
                DatabaseIdColumn UNIQUEIDENTIFIER,
                NullableOwnerTokenColumn UNIQUEIDENTIFIER NULL
            )
        ", commandTimeout: 30).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// When an OwnerToken is inserted and retrieved via Dapper, then it round-trips to the same value.
    /// </summary>
    /// <intent>
    /// Validate the Dapper type handler for OwnerToken.
    /// </intent>
    /// <scenario>
    /// Given a test table and a generated OwnerToken value.
    /// </scenario>
    /// <behavior>
    /// Then the retrieved OwnerToken equals the original value.
    /// </behavior>
    [Fact]
    public async Task OwnerToken_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var ownerToken = OwnerToken.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@OwnerToken, @Guid1, @Guid2, @Guid3, @Guid4, @Guid5, @Guid6)",
            new
            {
                OwnerToken = ownerToken,
                Guid1 = Guid.NewGuid(),
                Guid2 = Guid.NewGuid(),
                Guid3 = Guid.NewGuid(),
                Guid4 = Guid.NewGuid(),
                Guid5 = Guid.NewGuid(),
                Guid6 = Guid.NewGuid(),
            });

        // Act - Retrieve
        var result = await connection.QuerySingleAsync<OwnerToken>(
            "SELECT OwnerTokenColumn FROM TestTable WHERE Id = 1");

        // Assert
        Assert.Equal(ownerToken, result);
    }

    /// <summary>
    /// When a nullable OwnerToken is stored and retrieved, then values round-trip and nulls remain null.
    /// </summary>
    /// <intent>
    /// Validate nullable OwnerToken handling in Dapper.
    /// </intent>
    /// <scenario>
    /// Given a test table and a nullable OwnerToken value inserted once with a value and once with null.
    /// </scenario>
    /// <behavior>
    /// Then the first row returns the original OwnerToken and the second row returns null.
    /// </behavior>
    [Fact]
    public async Task NullableOwnerToken_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var ownerToken = OwnerToken.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert with value
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, NullableOwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@Guid1, @NullableOwnerToken, @Guid2, @Guid3, @Guid4, @Guid5, @Guid6, @Guid7)",
            new
            {
                Guid1 = Guid.NewGuid(),
                NullableOwnerToken = (OwnerToken?)ownerToken,
                Guid2 = Guid.NewGuid(),
                Guid3 = Guid.NewGuid(),
                Guid4 = Guid.NewGuid(),
                Guid5 = Guid.NewGuid(),
                Guid6 = Guid.NewGuid(),
                Guid7 = Guid.NewGuid(),
            });

        // Act - Retrieve with value
        var resultWithValue = await connection.QuerySingleAsync<OwnerToken?>(
            "SELECT NullableOwnerTokenColumn FROM TestTable WHERE Id = 1");

        // Assert - With value
        Assert.NotNull(resultWithValue);
        Assert.Equal(ownerToken, resultWithValue.Value);

        // Act - Insert with null
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, NullableOwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@Guid1, @NullableOwnerToken, @Guid2, @Guid3, @Guid4, @Guid5, @Guid6, @Guid7)",
            new
            {
                Guid1 = Guid.NewGuid(),
                NullableOwnerToken = (OwnerToken?)null,
                Guid2 = Guid.NewGuid(),
                Guid3 = Guid.NewGuid(),
                Guid4 = Guid.NewGuid(),
                Guid5 = Guid.NewGuid(),
                Guid6 = Guid.NewGuid(),
                Guid7 = Guid.NewGuid(),
            });

        // Act - Retrieve with null
        var resultWithNull = await connection.QuerySingleAsync<OwnerToken?>(
            "SELECT NullableOwnerTokenColumn FROM TestTable WHERE Id = 2");

        // Assert - With null
        Assert.Null(resultWithNull);
    }

    /// <summary>
    /// When an InboxMessageIdentifier is inserted and retrieved via Dapper, then it round-trips to the same value.
    /// </summary>
    /// <intent>
    /// Validate the Dapper type handler for InboxMessageIdentifier.
    /// </intent>
    /// <scenario>
    /// Given a test table and a generated InboxMessageIdentifier value.
    /// </scenario>
    /// <behavior>
    /// Then the retrieved InboxMessageIdentifier equals the original value.
    /// </behavior>
    [Fact]
    public async Task InboxMessageIdentifier_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var messageId = InboxMessageIdentifier.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@Guid1, @MessageId, @Guid2, @Guid3, @Guid4, @Guid5, @Guid6)",
            new
            {
                Guid1 = Guid.NewGuid(),
                MessageId = messageId,
                Guid2 = Guid.NewGuid(),
                Guid3 = Guid.NewGuid(),
                Guid4 = Guid.NewGuid(),
                Guid5 = Guid.NewGuid(),
                Guid6 = Guid.NewGuid(),
            });

        // Act - Retrieve
        var result = await connection.QuerySingleAsync<InboxMessageIdentifier>(
            "SELECT InboxMessageIdColumn FROM TestTable WHERE Id = 1");

        // Assert
        Assert.Equal(messageId, result);
    }

    /// <summary>
    /// When an OutboxMessageIdentifier is inserted and retrieved via Dapper, then it round-trips to the same value.
    /// </summary>
    /// <intent>
    /// Validate the Dapper type handler for OutboxMessageIdentifier.
    /// </intent>
    /// <scenario>
    /// Given a test table and a generated OutboxMessageIdentifier value.
    /// </scenario>
    /// <behavior>
    /// Then the retrieved OutboxMessageIdentifier equals the original value.
    /// </behavior>
    [Fact]
    public async Task OutboxMessageIdentifier_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var messageId = OutboxMessageIdentifier.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@Guid1, @Guid2, @MessageId, @Guid3, @Guid4, @Guid5, @Guid6)",
            new
            {
                Guid1 = Guid.NewGuid(),
                Guid2 = Guid.NewGuid(),
                MessageId = messageId,
                Guid3 = Guid.NewGuid(),
                Guid4 = Guid.NewGuid(),
                Guid5 = Guid.NewGuid(),
                Guid6 = Guid.NewGuid(),
            });

        // Act - Retrieve
        var result = await connection.QuerySingleAsync<OutboxMessageIdentifier>(
            "SELECT OutboxMessageIdColumn FROM TestTable WHERE Id = 1");

        // Assert
        Assert.Equal(messageId, result);
    }

    /// <summary>
    /// When an OutboxWorkItemIdentifier is inserted and retrieved via Dapper, then it round-trips to the same value.
    /// </summary>
    /// <intent>
    /// Validate the Dapper type handler for OutboxWorkItemIdentifier.
    /// </intent>
    /// <scenario>
    /// Given a test table and a generated OutboxWorkItemIdentifier value.
    /// </scenario>
    /// <behavior>
    /// Then the retrieved OutboxWorkItemIdentifier equals the original value.
    /// </behavior>
    [Fact]
    public async Task OutboxWorkItemIdentifier_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var workItemId = OutboxWorkItemIdentifier.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@Guid1, @Guid2, @Guid3, @WorkItemId, @Guid4, @Guid5, @Guid6)",
            new
            {
                Guid1 = Guid.NewGuid(),
                Guid2 = Guid.NewGuid(),
                Guid3 = Guid.NewGuid(),
                WorkItemId = workItemId,
                Guid4 = Guid.NewGuid(),
                Guid5 = Guid.NewGuid(),
                Guid6 = Guid.NewGuid(),
            });

        // Act - Retrieve
        var result = await connection.QuerySingleAsync<OutboxWorkItemIdentifier>(
            "SELECT OutboxWorkItemIdColumn FROM TestTable WHERE Id = 1");

        // Assert
        Assert.Equal(workItemId, result);
    }

    /// <summary>
    /// When a JoinIdentifier is inserted and retrieved via Dapper, then it round-trips to the same value.
    /// </summary>
    /// <intent>
    /// Validate the Dapper type handler for JoinIdentifier.
    /// </intent>
    /// <scenario>
    /// Given a test table and a generated JoinIdentifier value.
    /// </scenario>
    /// <behavior>
    /// Then the retrieved JoinIdentifier equals the original value.
    /// </behavior>
    [Fact]
    public async Task JoinIdentifier_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var joinId = JoinIdentifier.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@Guid1, @Guid2, @Guid3, @Guid4, @JoinId, @Guid5, @Guid6)",
            new
            {
                Guid1 = Guid.NewGuid(),
                Guid2 = Guid.NewGuid(),
                Guid3 = Guid.NewGuid(),
                Guid4 = Guid.NewGuid(),
                JoinId = joinId,
                Guid5 = Guid.NewGuid(),
                Guid6 = Guid.NewGuid(),
            });

        // Act - Retrieve
        var result = await connection.QuerySingleAsync<JoinIdentifier>(
            "SELECT JoinIdColumn FROM TestTable WHERE Id = 1");

        // Assert
        Assert.Equal(joinId, result);
    }

    /// <summary>
    /// When an InstanceIdentifier is inserted and retrieved via Dapper, then it round-trips to the same value.
    /// </summary>
    /// <intent>
    /// Validate the Dapper type handler for InstanceIdentifier.
    /// </intent>
    /// <scenario>
    /// Given a test table and a generated InstanceIdentifier value.
    /// </scenario>
    /// <behavior>
    /// Then the retrieved InstanceIdentifier equals the original value.
    /// </behavior>
    [Fact]
    public async Task InstanceIdentifier_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var instanceId = InstanceIdentifier.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@Guid1, @Guid2, @Guid3, @Guid4, @Guid5, @InstanceId, @Guid6)",
            new
            {
                Guid1 = Guid.NewGuid(),
                Guid2 = Guid.NewGuid(),
                Guid3 = Guid.NewGuid(),
                Guid4 = Guid.NewGuid(),
                Guid5 = Guid.NewGuid(),
                InstanceId = instanceId,
                Guid6 = Guid.NewGuid(),
            });

        // Act - Retrieve
        var result = await connection.QuerySingleAsync<InstanceIdentifier>(
            "SELECT InstanceIdColumn FROM TestTable WHERE Id = 1");

        // Assert
        Assert.Equal(instanceId, result);
    }

    /// <summary>
    /// When a DatabaseIdentifier is inserted and retrieved via Dapper, then it round-trips to the same value.
    /// </summary>
    /// <intent>
    /// Validate the Dapper type handler for DatabaseIdentifier.
    /// </intent>
    /// <scenario>
    /// Given a test table and a generated DatabaseIdentifier value.
    /// </scenario>
    /// <behavior>
    /// Then the retrieved DatabaseIdentifier equals the original value.
    /// </behavior>
    [Fact]
    public async Task DatabaseIdentifier_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var databaseId = DatabaseIdentifier.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@Guid1, @Guid2, @Guid3, @Guid4, @Guid5, @Guid6, @DatabaseId)",
            new
            {
                Guid1 = Guid.NewGuid(),
                Guid2 = Guid.NewGuid(),
                Guid3 = Guid.NewGuid(),
                Guid4 = Guid.NewGuid(),
                Guid5 = Guid.NewGuid(),
                Guid6 = Guid.NewGuid(),
                DatabaseId = databaseId,
            });

        // Act - Retrieve
        var result = await connection.QuerySingleAsync<DatabaseIdentifier>(
            "SELECT DatabaseIdColumn FROM TestTable WHERE Id = 1");

        // Assert
        Assert.Equal(databaseId, result);
    }

    /// <summary>
    /// When all strongly-typed IDs are inserted in one row, then Dapper retrieves each value correctly.
    /// </summary>
    /// <intent>
    /// Validate multiple type handlers in a single query.
    /// </intent>
    /// <scenario>
    /// Given a test row containing OwnerToken and various identifier types.
    /// </scenario>
    /// <behavior>
    /// Then each retrieved identifier matches its original value.
    /// </behavior>
    [Fact]
    public async Task AllTypesInSingleQuery_RoundTrip_WorksCorrectly()
    {
        // Arrange
        await CreateTestTableAsync();
        DapperTypeHandlerRegistration.RegisterTypeHandlers();
        var ownerToken = OwnerToken.GenerateNew();
        var inboxMessageId = InboxMessageIdentifier.GenerateNew();
        var outboxMessageId = OutboxMessageIdentifier.GenerateNew();
        var outboxWorkItemId = OutboxWorkItemIdentifier.GenerateNew();
        var joinId = JoinIdentifier.GenerateNew();
        var instanceId = InstanceIdentifier.GenerateNew();
        var databaseId = DatabaseIdentifier.GenerateNew();

        await using var connection = new SqlConnection(ConnectionString);

        // Act - Insert
        await connection.ExecuteAsync(
            "INSERT INTO TestTable (OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn) VALUES (@OwnerToken, @InboxMessageId, @OutboxMessageId, @OutboxWorkItemId, @JoinId, @InstanceId, @DatabaseId)",
            new
            {
                OwnerToken = ownerToken,
                InboxMessageId = inboxMessageId,
                OutboxMessageId = outboxMessageId,
                OutboxWorkItemId = outboxWorkItemId,
                JoinId = joinId,
                InstanceId = instanceId,
                DatabaseId = databaseId,
            });

        // Act - Retrieve
        var result = await connection.QuerySingleAsync<AllTypesRow>(
            "SELECT OwnerTokenColumn, InboxMessageIdColumn, OutboxMessageIdColumn, OutboxWorkItemIdColumn, JoinIdColumn, InstanceIdColumn, DatabaseIdColumn FROM TestTable WHERE Id = 1");

        // Assert
        Assert.Equal(ownerToken, result.OwnerTokenColumn);
        Assert.Equal(inboxMessageId, result.InboxMessageIdColumn);
        Assert.Equal(outboxMessageId, result.OutboxMessageIdColumn);
        Assert.Equal(outboxWorkItemId, result.OutboxWorkItemIdColumn);
        Assert.Equal(joinId, result.JoinIdColumn);
        Assert.Equal(instanceId, result.InstanceIdColumn);
        Assert.Equal(databaseId, result.DatabaseIdColumn);
    }

    private sealed record AllTypesRow(
        OwnerToken OwnerTokenColumn,
        InboxMessageIdentifier InboxMessageIdColumn,
        OutboxMessageIdentifier OutboxMessageIdColumn,
        OutboxWorkItemIdentifier OutboxWorkItemIdColumn,
        JoinIdentifier JoinIdColumn,
        InstanceIdentifier InstanceIdColumn,
        DatabaseIdentifier DatabaseIdColumn);
}
