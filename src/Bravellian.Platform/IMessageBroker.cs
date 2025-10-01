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
/// Provides an abstraction for sending messages to a message broker.
/// This interface allows the outbox processor to be decoupled from
/// specific message broker implementations.
/// </summary>
public interface IMessageBroker
{
    /// <summary>
    /// Sends a message to the specified topic through the message broker.
    /// </summary>
    /// <param name="message">The outbox message to send.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous send operation. 
    /// Returns true if the message was sent successfully, false otherwise.</returns>
    Task<bool> SendMessageAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}