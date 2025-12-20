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


using System.Diagnostics;

namespace Bravellian.Platform.Tests.TestUtilities;

/// <summary>
/// A fake implementation of IMonotonicClock for testing purposes.
/// Allows controlled advancement of monotonic time.
/// </summary>
public class FakeMonotonicClock : IMonotonicClock
{
    private double currentSeconds;

    public FakeMonotonicClock(double startSeconds = 1000.0)
    {
        currentSeconds = startSeconds;
    }

    public long Ticks => (long)(currentSeconds * Stopwatch.Frequency);

    public double Seconds => currentSeconds;

    public void Advance(TimeSpan timeSpan)
    {
        currentSeconds += timeSpan.TotalSeconds;
    }
}
