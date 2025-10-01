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
/// A simple message broker implementation that writes messages to the console.
/// This is intended for development, testing, and as a default fallback when
/// no specific message broker implementation is configured.
/// </summary>
internal class ConsoleMessageBroker : IMessageBroker
{
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleMessageBroker"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for adding timestamp information.</param>
    public ConsoleMessageBroker(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<bool> SendMessageAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        // In a real implementation, you would have your message broker client code here.
        var timestamp = this.timeProvider.GetUtcNow();
        System.Console.WriteLine($"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] Sending message {message.Id} to topic {message.Topic}");

        await Task.Delay(100, cancellationToken).ConfigureAwait(false); // Simulate network latency
        return true; // Assume it was sent successfully
    }
}