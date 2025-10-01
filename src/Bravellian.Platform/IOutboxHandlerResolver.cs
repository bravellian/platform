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

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Resolves outbox handlers by topic name.
/// </summary>
public interface IOutboxHandlerResolver
{
    /// <summary>
    /// Attempts to get a handler for the specified topic.
    /// </summary>
    /// <param name="topic">The topic name.</param>
    /// <param name="handler">The handler if found.</param>
    /// <returns>True if a handler was found, false otherwise.</returns>
    bool TryGet(string topic, out IOutboxHandler handler);
}

/// <summary>
/// Default implementation of IOutboxHandlerResolver that maps handlers by topic.
/// </summary>
public sealed class OutboxHandlerResolver : IOutboxHandlerResolver
{
    private readonly IReadOnlyDictionary<string, IOutboxHandler> _byTopic;

    public OutboxHandlerResolver(IEnumerable<IOutboxHandler> handlers)
        => _byTopic = handlers.ToDictionary(h => h.Topic, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string topic, out IOutboxHandler handler)
        => _byTopic.TryGetValue(topic, out handler!);
}