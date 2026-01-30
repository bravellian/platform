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

using System;
using System.Collections.Generic;
using System.Threading;

namespace Bravellian.Platform;

/// <summary>
/// Default implementation of <see cref="IStartupLatch"/>.
/// </summary>
public sealed class StartupLatch : IStartupLatch
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> pendingSteps = new(StringComparer.Ordinal);
    private int pendingCount;

    /// <inheritdoc />
    public bool IsReady => Volatile.Read(ref pendingCount) == 0;

    /// <inheritdoc />
    public IDisposable Register(string stepName)
    {
        ArgumentNullException.ThrowIfNull(stepName);

        lock (gate)
        {
            if (pendingSteps.TryGetValue(stepName, out var count))
            {
                pendingSteps[stepName] = count + 1;
            }
            else
            {
                pendingSteps[stepName] = 1;
            }

            pendingCount++;
        }

        return new StepRegistration(this, stepName);
    }

    private void Release(string stepName)
    {
        lock (gate)
        {
            if (!pendingSteps.TryGetValue(stepName, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                pendingSteps.Remove(stepName);
            }
            else
            {
                pendingSteps[stepName] = count - 1;
            }

            pendingCount--;
        }
    }

    private sealed class StepRegistration : IDisposable
    {
        private readonly StartupLatch latch;
        private readonly string stepName;
        private int disposed;

        public StepRegistration(StartupLatch latch, string stepName)
        {
            this.latch = latch;
            this.stepName = stepName;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            latch.Release(stepName);
        }
    }
}
