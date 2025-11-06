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

using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Data.SqlClient;
using Dapper;

public class RoundRobinSelectionStrategyTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly List<IOutboxStore> mockStores;

    public RoundRobinSelectionStrategyTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;

        // Create mock stores for testing
        this.mockStores = new List<IOutboxStore>
        {
            new MockOutboxStore("Store1"),
            new MockOutboxStore("Store2"),
            new MockOutboxStore("Store3"),
        };
    }

    [Fact]
    public void SelectNext_WithNoStores_ReturnsNull()
    {
        // Arrange
        var strategy = new RoundRobinOutboxSelectionStrategy();
        var emptyStores = new List<IOutboxStore>();

        // Act
        var result = strategy.SelectNext(emptyStores, null, 0);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void SelectNext_CyclesThroughStores()
    {
        // Arrange
        var strategy = new RoundRobinOutboxSelectionStrategy();

        // Act & Assert - Should cycle through all stores in order
        var first = strategy.SelectNext(this.mockStores, null, 0);
        first.ShouldBe(this.mockStores[0]);

        var second = strategy.SelectNext(this.mockStores, first, 5);
        second.ShouldBe(this.mockStores[1]);

        var third = strategy.SelectNext(this.mockStores, second, 5);
        third.ShouldBe(this.mockStores[2]);

        // Should wrap around to the first store
        var fourth = strategy.SelectNext(this.mockStores, third, 5);
        fourth.ShouldBe(this.mockStores[0]);
    }

    [Fact]
    public void Reset_ResetsToFirstStore()
    {
        // Arrange
        var strategy = new RoundRobinOutboxSelectionStrategy();

        // Advance through some stores
        strategy.SelectNext(this.mockStores, null, 0);
        strategy.SelectNext(this.mockStores, this.mockStores[0], 5);

        // Act
        strategy.Reset();

        // Assert - Should start from the first store again
        var result = strategy.SelectNext(this.mockStores, null, 0);
        result.ShouldBe(this.mockStores[0]);
    }

}

public class DrainFirstSelectionStrategyTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly List<IOutboxStore> mockStores;

    public DrainFirstSelectionStrategyTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;

        // Create mock stores for testing
        this.mockStores = new List<IOutboxStore>
        {
            new MockOutboxStore("Store1"),
            new MockOutboxStore("Store2"),
            new MockOutboxStore("Store3"),
        };
    }

    [Fact]
    public void SelectNext_WithNoStores_ReturnsNull()
    {
        // Arrange
        var strategy = new DrainFirstOutboxSelectionStrategy();
        var emptyStores = new List<IOutboxStore>();

        // Act
        var result = strategy.SelectNext(emptyStores, null, 0);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void SelectNext_SticksToSameStoreWhenMessagesProcessed()
    {
        // Arrange
        var strategy = new DrainFirstOutboxSelectionStrategy();

        // Act & Assert
        var first = strategy.SelectNext(this.mockStores, null, 0);
        first.ShouldBe(this.mockStores[0]);

        // Keep processing the same store as long as it has messages
        var second = strategy.SelectNext(this.mockStores, first, 10); // Processed 10 messages
        second.ShouldBe(this.mockStores[0]); // Should stay on Store1

        var third = strategy.SelectNext(this.mockStores, second, 5); // Processed 5 messages
        third.ShouldBe(this.mockStores[0]); // Should still be Store1
    }

    [Fact]
    public void SelectNext_MovesToNextStoreWhenNoMessagesProcessed()
    {
        // Arrange
        var strategy = new DrainFirstOutboxSelectionStrategy();

        // Act & Assert
        var first = strategy.SelectNext(this.mockStores, null, 0);
        first.ShouldBe(this.mockStores[0]);

        // No messages processed, should move to next store
        var second = strategy.SelectNext(this.mockStores, first, 0);
        second.ShouldBe(this.mockStores[1]);

        // Still no messages, move to next
        var third = strategy.SelectNext(this.mockStores, second, 0);
        third.ShouldBe(this.mockStores[2]);

        // Wrap around
        var fourth = strategy.SelectNext(this.mockStores, third, 0);
        fourth.ShouldBe(this.mockStores[0]);
    }

    [Fact]
    public void SelectNext_MixedBehavior()
    {
        // Arrange
        var strategy = new DrainFirstOutboxSelectionStrategy();

        // Act & Assert - Simulate realistic behavior
        var first = strategy.SelectNext(this.mockStores, null, 0);
        first.ShouldBe(this.mockStores[0]);

        // Process some messages from Store1
        var second = strategy.SelectNext(this.mockStores, first, 10);
        second.ShouldBe(this.mockStores[0]);

        // Store1 is now empty
        var third = strategy.SelectNext(this.mockStores, second, 0);
        third.ShouldBe(this.mockStores[1]);

        // Process messages from Store2
        var fourth = strategy.SelectNext(this.mockStores, third, 20);
        fourth.ShouldBe(this.mockStores[1]);

        // Store2 is empty
        var fifth = strategy.SelectNext(this.mockStores, fourth, 0);
        fifth.ShouldBe(this.mockStores[2]);
    }

}
