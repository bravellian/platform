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

namespace Bravellian.Platform.Idempotency;

/// <summary>
/// Stores idempotency keys to prevent duplicate operations.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to begin processing for the provided key.
    /// </summary>
    /// <param name="key">Stable idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when processing may proceed.</returns>
    Task<bool> TryBeginAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the provided key as completed.
    /// </summary>
    /// <param name="key">Stable idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the provided key as failed and eligible for retry.
    /// </summary>
    /// <param name="key">Stable idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FailAsync(string key, CancellationToken cancellationToken);
}
