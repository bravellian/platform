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
using System.Collections.Generic;

/// <summary>
/// Represents a snapshot of the watchdog state at a point in time.
/// </summary>
/// <param name="LastScanAt">The timestamp of the last watchdog scan.</param>
/// <param name="LastHeartbeatAt">The timestamp of the last heartbeat.</param>
/// <param name="ActiveAlerts">The list of currently active alerts.</param>
public sealed record WatchdogSnapshot(
    DateTimeOffset LastScanAt,
    DateTimeOffset LastHeartbeatAt,
    IReadOnlyList<ActiveAlert> ActiveAlerts);
