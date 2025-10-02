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
/// Handles inbound messages for a specific topic.
/// Implementations can perform local work or transform/forward messages.
/// </summary>
public interface IInboxHandler
{
    /// <summary>
    /// The topic this handler serves (e.g., "InvoiceImported", "fanout:etl:payments").
    /// </summary>
    string Topic { get; }

    /// <summary>
    /// Process the inbound message. 
    /// Throw for transient failures (will be retried) or permanent failures (will be marked as dead).
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(InboxMessage message, CancellationToken cancellationToken);
}