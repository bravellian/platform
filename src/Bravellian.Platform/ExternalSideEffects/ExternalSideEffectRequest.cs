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
/// Describes an external side-effect request.
/// </summary>
public sealed record ExternalSideEffectRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalSideEffectRequest"/> class.
    /// </summary>
    /// <param name="storeKey">The store key used to resolve persistence.</param>
    /// <param name="key">The external side-effect key.</param>
    public ExternalSideEffectRequest(string storeKey, ExternalSideEffectKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeKey);
        StoreKey = storeKey;
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    /// <summary>
    /// Gets the store key for persistence.
    /// </summary>
    public string StoreKey { get; }

    /// <summary>
    /// Gets the external side-effect key.
    /// </summary>
    public ExternalSideEffectKey Key { get; }

    /// <summary>
    /// Gets the correlation identifier.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the outbox message identifier.
    /// </summary>
    public Guid? OutboxMessageId { get; init; }

    /// <summary>
    /// Gets the payload hash used for idempotency.
    /// </summary>
    public string? PayloadHash { get; init; }
}
