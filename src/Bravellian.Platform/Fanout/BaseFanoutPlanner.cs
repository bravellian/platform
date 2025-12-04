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
/// Base implementation of IFanoutPlanner that provides common cadence and cursor-aware logic.
/// Application code only needs to implement the candidate enumeration logic.
/// </summary>
public abstract class BaseFanoutPlanner : IFanoutPlanner
{
    private readonly IFanoutPolicyRepository policyRepository;
    private readonly IFanoutCursorRepository cursorRepository;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseFanoutPlanner"/> class.
    /// </summary>
    /// <param name="policyRepository">Repository for fanout policies and cadence settings.</param>
    /// <param name="cursorRepository">Repository for tracking completion cursors.</param>
    /// <param name="timeProvider">Time provider for current timestamp operations.</param>
    protected BaseFanoutPlanner(
        IFanoutPolicyRepository policyRepository,
        IFanoutCursorRepository cursorRepository,
        TimeProvider timeProvider)
    {
        this.policyRepository = policyRepository ?? throw new ArgumentNullException(nameof(policyRepository));
        this.cursorRepository = cursorRepository ?? throw new ArgumentNullException(nameof(cursorRepository));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Enumerates all candidate shard/work key combinations for the given fanout topic.
    /// This is the only method application code needs to implement.
    /// </summary>
    /// <param name="fanoutTopic">The fanout topic to enumerate candidates for.</param>
    /// <param name="workKey">Optional work key filter (null means enumerate all work keys).</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>All possible (ShardKey, WorkKey) combinations that could need processing.</returns>
    protected abstract IAsyncEnumerable<(string ShardKey, string WorkKey)> EnumerateCandidatesAsync(
        string fanoutTopic,
        string? workKey,
        CancellationToken ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FanoutSlice>> GetDueSlicesAsync(string fanoutTopic, string? workKey, CancellationToken ct)
    {
        var list = new List<FanoutSlice>();

        await foreach (var (shardKey, wk) in EnumerateCandidatesAsync(fanoutTopic, workKey, ct).ConfigureAwait(false))
        {
            var (everySeconds, jitterSeconds) = await policyRepository.GetCadenceAsync(fanoutTopic, wk, ct).ConfigureAwait(false);
            var lastCompleted = await cursorRepository.GetLastAsync(fanoutTopic, wk, shardKey, ct).ConfigureAwait(false);

            var now = timeProvider.GetUtcNow();
            var spacing = TimeSpan.FromSeconds(everySeconds + Random.Shared.Next(0, jitterSeconds <= 0 ? 1 : jitterSeconds));

            if (lastCompleted is null || (now - lastCompleted) >= spacing)
            {
                list.Add(new FanoutSlice(fanoutTopic, shardKey, wk, windowStart: lastCompleted));
            }
        }

        return list;
    }
}
