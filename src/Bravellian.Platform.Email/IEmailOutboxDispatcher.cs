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

namespace Bravellian.Platform.Email;

/// <summary>
/// Dispatches queued email outbox items.
/// </summary>
public interface IEmailOutboxDispatcher
{
    /// <summary>
    /// Dispatches a batch of queued outbox items.
    /// </summary>
    /// <param name="maxBatchSize">Maximum batch size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dispatch result.</returns>
    Task<EmailOutboxDispatchResult> DispatchAsync(int maxBatchSize, CancellationToken cancellationToken);
}
