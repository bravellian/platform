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
/// A simple console-based outbox handler for development and testing.
/// Writes message details to the console with timestamp information.
/// </summary>
internal class ConsoleOutboxHandler : IOutboxHandler
{
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleOutboxHandler"/> class.
    /// </summary>
    /// <param name="topic">The topic this handler serves.</param>
    /// <param name="timeProvider">The time provider for adding timestamp information.</param>
    public ConsoleOutboxHandler(string topic, TimeProvider timeProvider)
    {
        this.Topic = topic;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Topic { get; }

    /// <inheritdoc />
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var timestamp = this.timeProvider.GetUtcNow();
        System.Console.WriteLine($"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] Handling message {message.Id} for topic '{message.Topic}': {message.Payload}");

        // Simulate some processing time
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }
}
