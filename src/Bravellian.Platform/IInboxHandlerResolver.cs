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
/// Resolves inbox handlers by topic name.
/// </summary>
public interface IInboxHandlerResolver
{
    /// <summary>
    /// Gets a handler for the specified topic.
    /// </summary>
    /// <param name="topic">The topic name.</param>
    /// <returns>The handler for the topic.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no handler is registered for the topic.</exception>
    IInboxHandler GetHandler(string topic);
}

/// <summary>
/// Default implementation of IInboxHandlerResolver that maps handlers by topic.
/// </summary>
public sealed class InboxHandlerResolver : IInboxHandlerResolver
{
    private readonly IReadOnlyDictionary<string, IInboxHandler> byTopic;

    public InboxHandlerResolver(IEnumerable<IInboxHandler> handlers)
        => this.byTopic = handlers.ToDictionary(h => h.Topic, StringComparer.OrdinalIgnoreCase);

    public IInboxHandler GetHandler(string topic)
    {
        if (!this.byTopic.TryGetValue(topic, out var handler))
        {
            throw new InvalidOperationException($"No handler registered for topic '{topic}'");
        }

        return handler;
    }
}
