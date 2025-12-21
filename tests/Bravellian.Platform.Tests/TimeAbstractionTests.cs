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
