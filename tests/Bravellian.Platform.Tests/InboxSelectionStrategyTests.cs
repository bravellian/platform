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

public class InboxSelectionStrategyTests
{
    /// <summary>When SelectNext is called repeatedly, then the round-robin strategy cycles through stores and wraps.</summary>
    /// <intent>Verify round-robin selection advances in order across inbox stores.</intent>
    /// <scenario>Given three MockInboxWorkStore instances and successive SelectNext calls.</scenario>
    /// <behavior>Then the strategy returns store1, store2, store3, then wraps to store1.</behavior>
    [Fact]
    public void RoundRobin_CyclesThroughStoresEvenly()
    {
        // Arrange
        var store1 = new MockInboxWorkStore("Store1");
        var store2 = new MockInboxWorkStore("Store2");
        var store3 = new MockInboxWorkStore("Store3");
        var stores = new List<IInboxWorkStore> { store1, store2, store3 };

        var strategy = new RoundRobinInboxSelectionStrategy();

        // Act & Assert - Should cycle through all stores
        var selected1 = strategy.SelectNext(stores, null, 0);
        selected1.ShouldBe(store1);

        var selected2 = strategy.SelectNext(stores, store1, 5);
        selected2.ShouldBe(store2);

        var selected3 = strategy.SelectNext(stores, store2, 5);
        selected3.ShouldBe(store3);

        // Should wrap around to first store
        var selected4 = strategy.SelectNext(stores, store3, 5);
        selected4.ShouldBe(store1);
    }

    /// <summary>When the round-robin strategy is given no stores, then it returns null.</summary>
    /// <intent>Ensure empty store lists are handled safely.</intent>
    /// <scenario>Given an empty list of IInboxWorkStore and a RoundRobinInboxSelectionStrategy.</scenario>
    /// <behavior>Then SelectNext returns null.</behavior>
    [Fact]
    public void RoundRobin_HandlesEmptyStoreList()
    {
        // Arrange
        var strategy = new RoundRobinInboxSelectionStrategy();
        var stores = new List<IInboxWorkStore>();

        // Act
        var selected = strategy.SelectNext(stores, null, 0);

        // Assert
        selected.ShouldBeNull();
    }

    /// <summary>When Reset is called, then round-robin selection starts from the first store again.</summary>
    /// <intent>Validate reset clears the round-robin cursor.</intent>
    /// <scenario>Given a strategy that has already advanced to a later store.</scenario>
    /// <behavior>Then SelectNext returns the first store after Reset.</behavior>
    [Fact]
    public void RoundRobin_ResetReturnsToFirstStore()
    {
        // Arrange
        var store1 = new MockInboxWorkStore("Store1");
        var store2 = new MockInboxWorkStore("Store2");
        var stores = new List<IInboxWorkStore> { store1, store2 };

        var strategy = new RoundRobinInboxSelectionStrategy();

        // Act - Move to second store
        strategy.SelectNext(stores, null, 0);
        strategy.SelectNext(stores, store1, 5);

        // Reset
        strategy.Reset();

        // Assert - Should be back to first store
        var selected = strategy.SelectNext(stores, null, 0);
        selected.ShouldBe(store1);
    }

    /// <summary>When messages are processed from a store, then the drain-first strategy stays on that store.</summary>
    /// <intent>Ensure drain-first keeps selecting the active store while work is processed.</intent>
    /// <scenario>Given two MockInboxWorkStore instances and non-zero processed counts.</scenario>
    /// <behavior>Then SelectNext returns the current store repeatedly.</behavior>
    [Fact]
    public void DrainFirst_StaysOnStoreWithMessages()
    {
        // Arrange
        var store1 = new MockInboxWorkStore("Store1");
        var store2 = new MockInboxWorkStore("Store2");
        var stores = new List<IInboxWorkStore> { store1, store2 };

        var strategy = new DrainFirstInboxSelectionStrategy();

        // Act - First selection
        var selected1 = strategy.SelectNext(stores, null, 0);
        selected1.ShouldBe(store1);

        // Act - Store1 still has messages (count > 0), should stay on it
        var selected2 = strategy.SelectNext(stores, store1, 5);
        selected2.ShouldBe(store1);

        var selected3 = strategy.SelectNext(stores, store1, 3);
        selected3.ShouldBe(store1);
    }

    /// <summary>When a store processes zero messages, then the drain-first strategy advances to the next store.</summary>
    /// <intent>Verify drain-first moves on when the current store is empty.</intent>
    /// <scenario>Given two MockInboxWorkStore instances and zero processed counts.</scenario>
    /// <behavior>Then SelectNext advances to the next store and wraps when needed.</behavior>
    [Fact]
    public void DrainFirst_MovesToNextStoreWhenEmpty()
    {
        // Arrange
        var store1 = new MockInboxWorkStore("Store1");
        var store2 = new MockInboxWorkStore("Store2");
        var stores = new List<IInboxWorkStore> { store1, store2 };

        var strategy = new DrainFirstInboxSelectionStrategy();

        // Act - First selection
        var selected1 = strategy.SelectNext(stores, null, 0);
        selected1.ShouldBe(store1);

        // Act - Store1 is empty (count = 0), should move to store2
        var selected2 = strategy.SelectNext(stores, store1, 0);
        selected2.ShouldBe(store2);

        // Act - Store2 still has messages, should stay on it
        var selected3 = strategy.SelectNext(stores, store2, 10);
        selected3.ShouldBe(store2);

        // Act - Store2 is empty, should wrap back to store1
        var selected4 = strategy.SelectNext(stores, store2, 0);
        selected4.ShouldBe(store1);
    }

    /// <summary>When the drain-first strategy is given no stores, then it returns null.</summary>
    /// <intent>Ensure empty store lists are handled safely in drain-first selection.</intent>
    /// <scenario>Given an empty list of IInboxWorkStore and a DrainFirstInboxSelectionStrategy.</scenario>
    /// <behavior>Then SelectNext returns null.</behavior>
    [Fact]
    public void DrainFirst_HandlesEmptyStoreList()
    {
        // Arrange
        var strategy = new DrainFirstInboxSelectionStrategy();
        var stores = new List<IInboxWorkStore>();

        // Act
        var selected = strategy.SelectNext(stores, null, 0);

        // Assert
        selected.ShouldBeNull();
    }

    /// <summary>When Reset is called, then drain-first selection starts from the first store again.</summary>
    /// <intent>Validate reset clears the drain-first cursor.</intent>
    /// <scenario>Given a strategy that has advanced to the second store.</scenario>
    /// <behavior>Then SelectNext returns the first store after Reset.</behavior>
    [Fact]
    public void DrainFirst_ResetReturnsToFirstStore()
    {
        // Arrange
        var store1 = new MockInboxWorkStore("Store1");
        var store2 = new MockInboxWorkStore("Store2");
        var stores = new List<IInboxWorkStore> { store1, store2 };

        var strategy = new DrainFirstInboxSelectionStrategy();

        // Act - Move to second store
        strategy.SelectNext(stores, null, 0);
        strategy.SelectNext(stores, store1, 0);

        // Reset
        strategy.Reset();

        // Assert - Should be back to first store
        var selected = strategy.SelectNext(stores, null, 0);
        selected.ShouldBe(store1);
    }
}

