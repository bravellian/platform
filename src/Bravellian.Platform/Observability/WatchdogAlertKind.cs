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

/// <summary>
/// Defines the kinds of alerts that can be raised by the watchdog.
/// </summary>
public enum WatchdogAlertKind
{
    /// <summary>
    /// A scheduled job is overdue beyond the configured threshold.
    /// </summary>
    OverdueJob,

    /// <summary>
    /// An inbox message is stuck beyond the configured threshold.
    /// </summary>
    StuckInbox,

    /// <summary>
    /// An outbox message is stuck beyond the configured threshold.
    /// </summary>
    StuckOutbox,

    /// <summary>
    /// A processor loop is not running or has been idle beyond the configured threshold.
    /// </summary>
    ProcessorNotRunning,

    /// <summary>
    /// The watchdog heartbeat is stale.
    /// </summary>
    HeartbeatStale,
}
