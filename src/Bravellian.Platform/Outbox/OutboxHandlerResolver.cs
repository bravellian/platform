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
/// Default implementation of IOutboxHandlerResolver that maps handlers by topic.
/// </summary>
internal sealed class OutboxHandlerResolver : IOutboxHandlerResolver
{
    private readonly Dictionary<string, IOutboxHandler> byTopic;

    public OutboxHandlerResolver(IEnumerable<IOutboxHandler> handlers)
    {
        byTopic = new Dictionary<string, IOutboxHandler>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            if (byTopic.TryGetValue(handler.Topic, out var existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate outbox handler registration for topic '{handler.Topic}'. " +
                    $"Existing handler: {existing.GetType().Name}, New handler: {handler.GetType().Name}.");
            }

            byTopic.Add(handler.Topic, handler);
        }
    }

    public bool TryGet(string topic, out IOutboxHandler handler)
        => byTopic.TryGetValue(topic, out handler!);
}
