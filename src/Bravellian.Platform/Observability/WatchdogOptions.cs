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

namespace Bravellian.Platform.Observability;

using System;

/// <summary>
/// Configuration options for the watchdog service.
/// </summary>
public sealed class WatchdogOptions
{
    /// <summary>
    /// Gets or sets the period between watchdog scans. Default: 15 seconds.
    /// Jitter of ±10% is applied automatically.
    /// </summary>
    public TimeSpan ScanPeriod { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets the period between heartbeat emissions. Default: 30 seconds.
    /// </summary>
    public TimeSpan HeartbeatPeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the heartbeat timeout threshold. If exceeded, health becomes Unhealthy. Default: 90 seconds.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Gets or sets the threshold for overdue jobs. Default: 30 seconds.
    /// </summary>
    public TimeSpan JobOverdueThreshold { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the threshold for stuck inbox messages. Default: 5 minutes.
    /// </summary>
    public TimeSpan InboxStuckThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the threshold for stuck outbox messages. Default: 5 minutes.
    /// </summary>
    public TimeSpan OutboxStuckThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the threshold for idle processors. Default: 1 minute.
    /// </summary>
    public TimeSpan ProcessorIdleThreshold { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the cooldown period for alert re-emission per key. Default: 2 minutes.
    /// </summary>
    public TimeSpan AlertCooldown { get; set; } = TimeSpan.FromMinutes(2);
}
