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
/// Identifies an external side-effect operation and idempotency key.
/// </summary>
public sealed record ExternalSideEffectKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalSideEffectKey"/> class.
    /// </summary>
    /// <param name="operationName">The operation name.</param>
    /// <param name="idempotencyKey">The idempotency key.</param>
    public ExternalSideEffectKey(string operationName, string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        OperationName = operationName;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// Gets the operation name.
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// Gets the idempotency key.
    /// </summary>
    public string IdempotencyKey { get; }
}
