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
/// Payload for join.wait messages that orchestrate fan-in behavior.
/// </summary>
public sealed record JoinWaitPayload
{
    /// <summary>
    /// Gets or sets the join identifier to wait for.
    /// </summary>
    public JoinId JoinId { get; set; }

    /// <summary>
    /// Gets or sets whether the join should fail if any step failed.
    /// Default is true.
    /// </summary>
    public bool FailIfAnyStepFailed { get; set; } = true;

    /// <summary>
    /// Gets or sets the topic to enqueue when the join completes successfully.
    /// </summary>
    public string? OnCompleteTopic { get; set; }

    /// <summary>
    /// Gets or sets the payload to enqueue when the join completes successfully.
    /// </summary>
    public string? OnCompletePayload { get; set; }

    /// <summary>
    /// Gets or sets the topic to enqueue when the join fails.
    /// </summary>
    public string? OnFailTopic { get; set; }

    /// <summary>
    /// Gets or sets the payload to enqueue when the join fails.
    /// </summary>
    public string? OnFailPayload { get; set; }
}
