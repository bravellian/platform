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
/// Repository for tracking the last completion timestamp for each fanout slice.
/// This enables resumable processing and prevents duplicate work by tracking progress per shard.
/// </summary>
public interface IFanoutCursorRepository
{
    /// <summary>
    /// Gets the last completion timestamp for a specific fanout slice.
    /// Returns null if the slice has never been completed.
    /// </summary>
    /// <param name="fanoutTopic">The fanout topic name.</param>
    /// <param name="workKey">The work key within the topic.</param>
    /// <param name="shardKey">The shard key that identifies the partition of work.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The last completion time, or null if never completed.</returns>
    Task<DateTimeOffset?> GetLastAsync(string fanoutTopic, string workKey, string shardKey, CancellationToken ct);

    /// <summary>
    /// Records the completion timestamp for a fanout slice.
    /// This is typically called by downstream handlers after successful processing.
    /// </summary>
    /// <param name="fanoutTopic">The fanout topic name.</param>
    /// <param name="workKey">The work key within the topic.</param>
    /// <param name="shardKey">The shard key that identifies the partition of work.</param>
    /// <param name="completedAt">The timestamp when processing was completed.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    Task MarkCompletedAsync(string fanoutTopic, string workKey, string shardKey, DateTimeOffset completedAt, CancellationToken ct);
}
