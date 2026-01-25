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
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests;

public class TimeAbstractionTests
{
    /// <summary>When MonotonicClock ticks are read twice, then the second value is not less than the first.</summary>
    /// <intent>Verify monotonic ticks do not move backwards.</intent>
    /// <scenario>Given a new MonotonicClock instance.</scenario>
    /// <behavior>Then the second tick reading is greater than or equal to the first.</behavior>
    [Fact]
    public void MonotonicClock_Ticks_ReturnsIncreasingValues()
    {
        // Arrange
        var clock = new MonotonicClock();

        // Act
        var ticks1 = clock.Ticks;
        var ticks2 = clock.Ticks;

        // Assert
        ticks2.ShouldBeGreaterThanOrEqualTo(ticks1);
    }

    /// <summary>When MonotonicClock seconds are read, then the value is positive.</summary>
    /// <intent>Ensure the monotonic clock exposes a positive elapsed seconds value.</intent>
    /// <scenario>Given a new MonotonicClock instance.</scenario>
    /// <behavior>Then Seconds is greater than zero.</behavior>
    [Fact]
    public void MonotonicClock_Seconds_ReturnsPositiveValue()
    {
        // Arrange
        var clock = new MonotonicClock();

        // Act
        var seconds = clock.Seconds;

        // Assert
        seconds.ShouldBeGreaterThan(0);
    }

    /// <summary>When a deadline is in the future, then Expired returns false.</summary>
    /// <intent>Verify MonoDeadline reports not expired before the deadline.</intent>
    /// <scenario>Given a MonoDeadline one hour in the future and the same clock.</scenario>
    /// <behavior>Then Expired returns false.</behavior>
    [Fact]
    public void MonoDeadline_Expired_ReturnsFalseWhenNotReached()
    {
        // Arrange
        var clock = new MonotonicClock();
        var deadline = MonoDeadline.In(clock, TimeSpan.FromHours(1));

        // Act
        var expired = deadline.Expired(clock);

        // Assert
        expired.ShouldBeFalse();
    }

    /// <summary>When a deadline is in the past, then Expired returns true.</summary>
    /// <intent>Verify MonoDeadline reports expired once the deadline is reached.</intent>
    /// <scenario>Given a MonoDeadline set to one second before the current clock time.</scenario>
    /// <behavior>Then Expired returns true.</behavior>
    [Fact]
    public void MonoDeadline_Expired_ReturnsTrueWhenReached()
    {
        // Arrange
        var clock = new MonotonicClock();
        var deadline = new MonoDeadline(clock.Seconds - 1); // 1 second ago

        // Act
        var expired = deadline.Expired(clock);

        // Assert
        expired.ShouldBeTrue();
    }

    /// <summary>When FakeTimeProvider is advanced, then GetUtcNow reflects the new time.</summary>
    /// <intent>Demonstrate deterministic time control with FakeTimeProvider.</intent>
    /// <scenario>Given a FakeTimeProvider initialized to a fixed instant.</scenario>
    /// <behavior>Then advancing by one hour updates GetUtcNow by one hour.</behavior>
    [Fact]
    public void FakeTimeProvider_CanBeUsedForTesting()
    {
        // Arrange
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        // Act
        var initialTime = fakeTime.GetUtcNow();
        fakeTime.Advance(TimeSpan.FromHours(1));
        var advancedTime = fakeTime.GetUtcNow();

        // Assert
        initialTime.ShouldBe(DateTimeOffset.Parse("2024-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        advancedTime.ShouldBe(DateTimeOffset.Parse("2024-01-01T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>When a fake monotonic clock advances past a deadline, then Expired flips to true.</summary>
    /// <intent>Verify MonoDeadline works with a controllable monotonic clock.</intent>
    /// <scenario>Given a FakeMonotonicClock and a deadline 10 seconds in the future.</scenario>
    /// <behavior>Then Expired is false before the advance and true after advancing 15 seconds.</behavior>
    [Fact]
    public void MonoDeadline_WorksWithFakeMonotonicClock()
    {
        // Arrange
        var fakeClock = new FakeMonotonicClock();
        var deadline = MonoDeadline.In(fakeClock, TimeSpan.FromSeconds(10));

        // Act - time hasn't advanced yet
        var notExpired = deadline.Expired(fakeClock);

        // Advance the fake clock by 15 seconds
        fakeClock.Advance(TimeSpan.FromSeconds(15));
        var expired = deadline.Expired(fakeClock);

        // Assert
        notExpired.ShouldBeFalse();
        expired.ShouldBeTrue();
    }
}
