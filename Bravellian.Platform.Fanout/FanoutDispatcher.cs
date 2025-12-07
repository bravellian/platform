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
/// Default implementation of IFanoutDispatcher that uses the Outbox for reliable delivery.
/// Each fanout slice becomes an outbox message with topic "fanout:{fanoutTopic}:{workKey}".
/// </summary>
internal sealed class FanoutDispatcher : IFanoutDispatcher
{
    private readonly IOutbox outbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="FanoutDispatcher"/> class.
    /// </summary>
    /// <param name="outbox">The outbox service for reliable message delivery.</param>
    public FanoutDispatcher(IOutbox outbox)
    {
        this.outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    /// <inheritdoc/>
    public async Task<int> DispatchAsync(IEnumerable<FanoutSlice> slices, CancellationToken ct)
    {
        var count = 0;
        foreach (var slice in slices)
        {
            var topic = $"fanout:{slice.fanoutTopic}:{slice.workKey}";
            var payload = JsonSerializer.Serialize(slice);

            await outbox.EnqueueAsync(topic, payload, slice.correlationId, ct).ConfigureAwait(false);
            count++;
        }

        return count;
    }
}
