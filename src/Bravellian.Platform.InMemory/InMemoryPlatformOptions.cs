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
/// Configuration options for registering the in-memory platform stack in one call.
/// </summary>
public sealed class InMemoryPlatformOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether scheduler background workers should run.
    /// Defaults to true.
    /// </summary>
    public bool EnableSchedulerWorkers { get; set; } = true;

    /// <summary>Optional outbox options customization.</summary>
    public Action<InMemoryOutboxOptions>? ConfigureOutbox { get; set; }

    /// <summary>Optional inbox options customization.</summary>
    public Action<InMemoryInboxOptions>? ConfigureInbox { get; set; }

    /// <summary>Optional scheduler options customization.</summary>
    public Action<InMemorySchedulerOptions>? ConfigureScheduler { get; set; }

    /// <summary>Optional fanout options customization.</summary>
    public Action<InMemoryFanoutOptions>? ConfigureFanout { get; set; }
}
