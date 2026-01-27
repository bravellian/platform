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

namespace Bravellian.Platform.Email.Tests;

internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly Lock sync = new();
    private readonly Dictionary<string, IdempotencyState> states = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> TryBeginAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }

        lock (sync)
        {
            if (states.TryGetValue(key, out var state) && state != IdempotencyState.Failed)
            {
                return Task.FromResult(false);
            }

            states[key] = IdempotencyState.InProgress;
            return Task.FromResult(true);
        }
    }

    public Task CompleteAsync(string key, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            states[key] = IdempotencyState.Completed;
        }

        return Task.CompletedTask;
    }

    public Task FailAsync(string key, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            states[key] = IdempotencyState.Failed;
        }

        return Task.CompletedTask;
    }

    private enum IdempotencyState
    {
        Failed = 0,
        InProgress = 1,
        Completed = 2
    }
}

