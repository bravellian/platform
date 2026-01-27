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

    /// <summary>When SelectNext is called with no stores, then it returns null.</summary>
    /// <intent>Verify drain-first selection handles empty inputs safely.</intent>
    /// <scenario>Given an empty store list and a new DrainFirstOutboxSelectionStrategy.</scenario>
    /// <behavior>Then SelectNext returns null.</behavior>
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

    /// <summary>When a store processes messages, then SelectNext keeps returning the same store.</summary>
    /// <intent>Ensure drain-first strategy sticks to the current store while work is processed.</intent>
    /// <scenario>Given three mock stores and non-zero processed counts for the current store.</scenario>
    /// <behavior>Then SelectNext continues to return the first store.</behavior>
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

    /// <summary>When no messages are processed, then SelectNext advances to the next store.</summary>
    /// <intent>Verify drain-first advances when a store has no work.</intent>
    /// <scenario>Given three mock stores and zero processed counts on each call.</scenario>
    /// <behavior>Then SelectNext moves to the next store and wraps around.</behavior>
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

    /// <summary>When processed counts vary across calls, then SelectNext sticks to busy stores and advances when empty.</summary>
    /// <intent>Validate mixed drain-first behavior across multiple stores.</intent>
    /// <scenario>Given three mock stores with alternating non-zero and zero processed counts.</scenario>
    /// <behavior>Then SelectNext stays on active stores and moves on when processed count is zero.</behavior>
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

