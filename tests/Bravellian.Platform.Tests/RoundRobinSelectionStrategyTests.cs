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

    /// <summary>When SelectNext is called with no stores, then it returns null.</summary>
    /// <intent>Verify the round-robin strategy handles empty inputs safely.</intent>
    /// <scenario>Given an empty store list and a new RoundRobinOutboxSelectionStrategy instance.</scenario>
    /// <behavior>Then SelectNext returns null.</behavior>
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

    /// <summary>When SelectNext is called repeatedly, then it cycles through stores in order and wraps around.</summary>
    /// <intent>Ensure round-robin selection advances deterministically through the store list.</intent>
    /// <scenario>Given three mock stores and successive SelectNext calls with the previously selected store.</scenario>
    /// <behavior>Then the strategy returns store1, store2, store3, then wraps back to store1.</behavior>
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

    /// <summary>When Reset is called, then the next selection starts from the first store.</summary>
    /// <intent>Validate that reset clears the round-robin cursor.</intent>
    /// <scenario>Given a strategy that has already advanced through mock stores.</scenario>
    /// <behavior>Then SelectNext returns the first store after Reset.</behavior>
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
