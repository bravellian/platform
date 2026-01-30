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

internal sealed class InMemoryFanoutPolicyRepository : IFanoutPolicyRepository
{
    private const int DefaultEverySeconds = 300;
    private const int DefaultJitterSeconds = 60;
    private readonly Dictionary<string, (int EverySeconds, int JitterSeconds)> policies = new(StringComparer.OrdinalIgnoreCase);

    public Task<(int everySeconds, int jitterSeconds)> GetCadenceAsync(string fanoutTopic, string workKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanoutTopic);
        ArgumentException.ThrowIfNullOrWhiteSpace(workKey);

        var key = $"{fanoutTopic}:{workKey}";
        return Task.FromResult(policies.TryGetValue(key, out var policy)
            ? (policy.EverySeconds, policy.JitterSeconds)
            : (DefaultEverySeconds, DefaultJitterSeconds));
    }

    public Task SetCadenceAsync(string fanoutTopic, string workKey, int everySeconds, int jitterSeconds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fanoutTopic);
        ArgumentException.ThrowIfNullOrWhiteSpace(workKey);

        var key = $"{fanoutTopic}:{workKey}";
        policies[key] = (everySeconds, jitterSeconds);
        return Task.CompletedTask;
    }
}
