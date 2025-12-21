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

using System.Collections.Concurrent;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Registry for transport-agnostic module engines.
/// </summary>
internal static class ModuleEngineRegistry
{
    private static readonly ConcurrentDictionary<string, List<ModuleEngineDescriptor>> Engines = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string moduleKey, IEnumerable<ModuleEngineDescriptor> descriptors)
    {
        var list = Engines.GetOrAdd(moduleKey, _ => new List<ModuleEngineDescriptor>());
        lock (list)
        {
            list.AddRange(descriptors);
        }
    }

    public static IReadOnlyCollection<ModuleEngineDescriptor> GetEngines()
    {
        return Engines.Values.SelectMany(x => x).ToArray();
    }

    public static ModuleEngineDescriptor? FindWebhookEngine(string provider, string eventType)
    {
        return GetEngines()
            .Where(e => e.Manifest.Kind == EngineKind.Webhook)
            .SelectMany(descriptor => descriptor.Manifest.WebhookMetadata?.Select(meta => (descriptor, meta))
                                   ?? Array.Empty<(ModuleEngineDescriptor descriptor, ModuleEngineWebhookMetadata meta)>())
            .FirstOrDefault(pair => string.Equals(pair.meta.Provider, provider, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(pair.meta.EventType, eventType, StringComparison.OrdinalIgnoreCase))
            .descriptor;
    }

    public static ModuleEngineDescriptor? FindById(string moduleKey, string engineId)
    {
        return GetEngines().FirstOrDefault(e => string.Equals(e.ModuleKey, moduleKey, StringComparison.OrdinalIgnoreCase)
                                             && string.Equals(e.Manifest.Id, engineId, StringComparison.OrdinalIgnoreCase));
    }

    public static void Reset()
    {
        Engines.Clear();
    }
}
