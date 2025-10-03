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

namespace Bravellian.Platform;

/// <summary>
/// Configuration options for a fanout topic that define its schedule and behavior.
/// Each topic/work key combination gets its own recurring job with these settings.
/// </summary>
public sealed class FanoutTopicOptions
{
    /// <summary>Gets the fanout topic name (e.g., "etl", "reports").</summary>
    public required string FanoutTopic { get; init; }

    /// <summary>Gets the optional work key to filter planning (e.g., "payments", "vendors").</summary>
    public string? WorkKey { get; init; }

    /// <summary>Gets the cron schedule for the fanout (e.g., "*/5 * * * *" for every 5 minutes).</summary>
    public string Cron { get; init; } = "*/5 * * * *";

    /// <summary>Gets the default cadence in seconds if cron is not used.</summary>
    public int DefaultEverySeconds { get; init; } = 300;

    /// <summary>Gets the jitter in seconds to prevent thundering herd problems.</summary>
    public int JitterSeconds { get; init; } = 60;

    /// <summary>Gets the duration to hold the coordination lease.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(90);
}
