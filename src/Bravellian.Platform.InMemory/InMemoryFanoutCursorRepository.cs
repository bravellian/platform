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

internal sealed class InMemoryFanoutCursorRepository : IFanoutCursorRepository
{
    private readonly Dictionary<string, DateTimeOffset> cursors = new(StringComparer.OrdinalIgnoreCase);

    public Task<DateTimeOffset?> GetLastAsync(string fanoutTopic, string workKey, string shardKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanoutTopic);
        ArgumentException.ThrowIfNullOrWhiteSpace(workKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(shardKey);

        var key = $"{fanoutTopic}:{workKey}:{shardKey}";
        return Task.FromResult(cursors.TryGetValue(key, out var last) ? (DateTimeOffset?)last : null);
    }

    public Task MarkCompletedAsync(string fanoutTopic, string workKey, string shardKey, DateTimeOffset completedAt, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanoutTopic);
        ArgumentException.ThrowIfNullOrWhiteSpace(workKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(shardKey);

        var key = $"{fanoutTopic}:{workKey}:{shardKey}";
        cursors[key] = completedAt;
        return Task.CompletedTask;
    }
}
