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

using Bravellian.Platform.ExactlyOnce;
using Bravellian.Platform.Tests.TestUtilities;
using Shouldly;
using Xunit;

namespace Bravellian.Platform.Tests;

public sealed class ExactlyOnceExecutorTests
{
    /// <summary>When execute Async Suppresses Duplicate Without Executing, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for execute Async Suppresses Duplicate Without Executing.</intent>
    /// <scenario>Given execute Async Suppresses Duplicate Without Executing.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task ExecuteAsync_SuppressesDuplicateWithoutExecuting()
    {
        var store = new InMemoryIdempotencyStore();
        await store.CompleteAsync("key-1", CancellationToken.None);
        var executor = new ExactlyOnceExecutor<string>(store, new StringKeyResolver());
        var executed = false;

        var result = await executor.ExecuteAsync(
            "key-1",
            (item, _) =>
            {
                executed = true;
                return Task.FromResult(ExactlyOnceExecutionResult.Success());
            },
            CancellationToken.None);

        result.Outcome.ShouldBe(ExactlyOnceOutcome.Suppressed);
        executed.ShouldBeFalse();
    }

    /// <summary>When execute Async Success Completes Idempotency, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for execute Async Success Completes Idempotency.</intent>
    /// <scenario>Given execute Async Success Completes Idempotency.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task ExecuteAsync_Success_CompletesIdempotency()
    {
        var store = new InMemoryIdempotencyStore();
        var executor = new ExactlyOnceExecutor<string>(store, new StringKeyResolver());

        var result = await executor.ExecuteAsync(
            "key-2",
            (item, _) => Task.FromResult(ExactlyOnceExecutionResult.Success()),
            CancellationToken.None);

        result.Outcome.ShouldBe(ExactlyOnceOutcome.Completed);
        store.GetState("key-2").ShouldBe(InMemoryIdempotencyStore.IdempotencyState.Completed);
    }

    /// <summary>When execute Async Transient Failure Fails Idempotency, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for execute Async Transient Failure Fails Idempotency.</intent>
    /// <scenario>Given execute Async Transient Failure Fails Idempotency.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task ExecuteAsync_TransientFailure_FailsIdempotency()
    {
        var store = new InMemoryIdempotencyStore();
        var executor = new ExactlyOnceExecutor<string>(store, new StringKeyResolver());

        var result = await executor.ExecuteAsync(
            "key-3",
            (item, _) => Task.FromResult(ExactlyOnceExecutionResult.TransientFailure(errorMessage: "boom")),
            CancellationToken.None);

        result.Outcome.ShouldBe(ExactlyOnceOutcome.Retry);
        store.GetState("key-3").ShouldBe(InMemoryIdempotencyStore.IdempotencyState.Failed);
    }

    /// <summary>When execute Async Permanent Failure Completes Idempotency, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for execute Async Permanent Failure Completes Idempotency.</intent>
    /// <scenario>Given execute Async Permanent Failure Completes Idempotency.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task ExecuteAsync_PermanentFailure_CompletesIdempotency()
    {
        var store = new InMemoryIdempotencyStore();
        var executor = new ExactlyOnceExecutor<string>(store, new StringKeyResolver());

        var result = await executor.ExecuteAsync(
            "key-4",
            (item, _) => Task.FromResult(ExactlyOnceExecutionResult.PermanentFailure(errorMessage: "boom")),
            CancellationToken.None);

        result.Outcome.ShouldBe(ExactlyOnceOutcome.FailedPermanent);
        store.GetState("key-4").ShouldBe(InMemoryIdempotencyStore.IdempotencyState.Completed);
    }

    /// <summary>When execute Async Probe Confirmation Completes After Failure, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for execute Async Probe Confirmation Completes After Failure.</intent>
    /// <scenario>Given execute Async Probe Confirmation Completes After Failure.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task ExecuteAsync_ProbeConfirmation_CompletesAfterFailure()
    {
        var store = new InMemoryIdempotencyStore();
        var executor = new ExactlyOnceExecutor<string>(store, new StringKeyResolver(), new ConfirmingProbe());

        var result = await executor.ExecuteAsync(
            "key-5",
            (item, _) => Task.FromResult(ExactlyOnceExecutionResult.TransientFailure()),
            CancellationToken.None);

        result.Outcome.ShouldBe(ExactlyOnceOutcome.Completed);
        store.GetState("key-5").ShouldBe(InMemoryIdempotencyStore.IdempotencyState.Completed);
    }

    private sealed class StringKeyResolver : IExactlyOnceKeyResolver<string>
    {
        public string GetKey(string item)
        {
            return item;
        }
    }

    private sealed class ConfirmingProbe : IExactlyOnceProbe<string>
    {
        public Task<ExactlyOnceProbeResult> ProbeAsync(string item, CancellationToken cancellationToken)
        {
            return Task.FromResult(ExactlyOnceProbeResult.Confirmed());
        }
    }
}
