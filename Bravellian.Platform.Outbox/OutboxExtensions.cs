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
/// Extension methods for <see cref="IOutbox"/> to simplify common operations.
/// </summary>
public static class OutboxExtensions
{
    /// <summary>
    /// Enqueues a join.wait message to orchestrate fan-in behavior for the specified join.
    /// This is a convenience method that creates and serializes the JoinWaitPayload automatically.
    /// </summary>
    /// <param name="outbox">The outbox instance.</param>
    /// <param name="joinId">The join identifier to wait for.</param>
    /// <param name="failIfAnyStepFailed">Whether the join should fail if any step failed. Default is true.</param>
    /// <param name="onCompleteTopic">Optional topic to enqueue when the join completes successfully.</param>
    /// <param name="onCompletePayload">Optional payload to enqueue when the join completes successfully.</param>
    /// <param name="onFailTopic">Optional topic to enqueue when the join fails.</param>
    /// <param name="onFailPayload">Optional payload to enqueue when the join fails.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task EnqueueJoinWaitAsync(
        this IOutbox outbox,
        Bravellian.Platform.Outbox.JoinIdentifier joinId,
        bool failIfAnyStepFailed = true,
        string? onCompleteTopic = null,
        string? onCompletePayload = null,
        string? onFailTopic = null,
        string? onFailPayload = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new JoinWaitPayload
        {
            JoinId = joinId,
            FailIfAnyStepFailed = failIfAnyStepFailed,
            OnCompleteTopic = onCompleteTopic,
            OnCompletePayload = onCompletePayload,
            OnFailTopic = onFailTopic,
            OnFailPayload = onFailPayload
        };

        await outbox.EnqueueAsync(
            "join.wait",
            JsonSerializer.Serialize(payload),
            cancellationToken).ConfigureAwait(false);
    }
}
