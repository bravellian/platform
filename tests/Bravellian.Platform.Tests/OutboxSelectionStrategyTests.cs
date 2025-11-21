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

public class RoundRobinSelectionStrategyTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly List<IOutboxStore> mockStores;

    public RoundRobinSelectionStrategyTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;

        // Create mock stores for testing
        mockStores = new List<IOutboxStore>
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
        var first = strategy.SelectNext(mockStores, null, 0);
        first.ShouldBe(mockStores[0]);

        var second = strategy.SelectNext(mockStores, first, 5);
        second.ShouldBe(mockStores[1]);

        var third = strategy.SelectNext(mockStores, second, 5);
        third.ShouldBe(mockStores[2]);

        // Should wrap around to the first store
        var fourth = strategy.SelectNext(mockStores, third, 5);
        fourth.ShouldBe(mockStores[0]);
    }

    [Fact]
    public void Reset_ResetsToFirstStore()
    {
        // Arrange
        var strategy = new RoundRobinOutboxSelectionStrategy();

        // Advance through some stores
        strategy.SelectNext(mockStores, null, 0);
        strategy.SelectNext(mockStores, mockStores[0], 5);

        // Act
        strategy.Reset();

        // Assert - Should start from the first store again
        var result = strategy.SelectNext(mockStores, null, 0);
        result.ShouldBe(mockStores[0]);
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
        mockStores = new List<IOutboxStore>
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
        var first = strategy.SelectNext(mockStores, null, 0);
        first.ShouldBe(mockStores[0]);

        // Keep processing the same store as long as it has messages
        var second = strategy.SelectNext(mockStores, first, 10); // Processed 10 messages
        second.ShouldBe(mockStores[0]); // Should stay on Store1

        var third = strategy.SelectNext(mockStores, second, 5); // Processed 5 messages
        third.ShouldBe(mockStores[0]); // Should still be Store1
    }

    [Fact]
    public void SelectNext_MovesToNextStoreWhenNoMessagesProcessed()
    {
        // Arrange
        var strategy = new DrainFirstOutboxSelectionStrategy();

        // Act & Assert
        var first = strategy.SelectNext(mockStores, null, 0);
        first.ShouldBe(mockStores[0]);

        // No messages processed, should move to next store
        var second = strategy.SelectNext(mockStores, first, 0);
        second.ShouldBe(mockStores[1]);

        // Still no messages, move to next
        var third = strategy.SelectNext(mockStores, second, 0);
        third.ShouldBe(mockStores[2]);

        // Wrap around
        var fourth = strategy.SelectNext(mockStores, third, 0);
        fourth.ShouldBe(mockStores[0]);
    }

    [Fact]
    public void SelectNext_MixedBehavior()
    {
        // Arrange
        var strategy = new DrainFirstOutboxSelectionStrategy();

        // Act & Assert - Simulate realistic behavior
        var first = strategy.SelectNext(mockStores, null, 0);
        first.ShouldBe(mockStores[0]);

        // Process some messages from Store1
        var second = strategy.SelectNext(mockStores, first, 10);
        second.ShouldBe(mockStores[0]);

        // Store1 is now empty
        var third = strategy.SelectNext(mockStores, second, 0);
        third.ShouldBe(mockStores[1]);

        // Process messages from Store2
        var fourth = strategy.SelectNext(mockStores, third, 20);
        fourth.ShouldBe(mockStores[1]);

        // Store2 is empty
        var fifth = strategy.SelectNext(mockStores, fourth, 0);
        fifth.ShouldBe(mockStores[2]);
    }

}
