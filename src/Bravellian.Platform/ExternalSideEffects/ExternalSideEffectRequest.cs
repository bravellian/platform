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

public sealed record ExternalSideEffectRequest
{
    public ExternalSideEffectRequest(string storeKey, ExternalSideEffectKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeKey);
        StoreKey = storeKey;
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    public string StoreKey { get; }

    public ExternalSideEffectKey Key { get; }

    public string? CorrelationId { get; init; }

    public Guid? OutboxMessageId { get; init; }

    public string? PayloadHash { get; init; }
}
