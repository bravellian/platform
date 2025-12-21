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
    private static readonly ConcurrentDictionary<string, List<IModuleEngineDescriptor>> Engines = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string moduleKey, IEnumerable<IModuleEngineDescriptor> descriptors)
    {
        var list = Engines.GetOrAdd(moduleKey, _ => new List<IModuleEngineDescriptor>());
        lock (list)
        {
            foreach (var descriptor in descriptors)
            {
                var exists = list.Any(existing => string.Equals(existing.ModuleKey, descriptor.ModuleKey, StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(existing.Manifest.Id, descriptor.Manifest.Id, StringComparison.OrdinalIgnoreCase)
                                                && existing.ContractType == descriptor.ContractType);
                if (!exists)
                {
                    list.Add(descriptor);
                }
            }
        }
    }

    public static IReadOnlyCollection<IModuleEngineDescriptor> GetEngines()
    {
        // Take a thread-safe snapshot of all registered engines by copying each list under its lock.
        var snapshots = new List<IModuleEngineDescriptor[]>();

        foreach (var list in Engines.Values)
        {
            lock (list)
            {
                snapshots.Add(list.ToArray());
            }
        }

        return snapshots.SelectMany(x => x).ToArray();
    }

    public static IModuleEngineDescriptor? FindWebhookEngine(string provider, string eventType)
    {
        return GetEngines()
            .Where(e => e.Manifest.Kind == EngineKind.Webhook)
            .SelectMany(descriptor => descriptor.Manifest.WebhookMetadata?.Select(meta => (descriptor, meta))
                                   ?? Array.Empty<(IModuleEngineDescriptor descriptor, ModuleEngineWebhookMetadata meta)>())
            .FirstOrDefault(pair => string.Equals(pair.meta.Provider, provider, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(pair.meta.EventType, eventType, StringComparison.OrdinalIgnoreCase))
            .descriptor;
    }

    public static IModuleEngineDescriptor? FindById(string moduleKey, string engineId)
    {
        return GetEngines().FirstOrDefault(e => string.Equals(e.ModuleKey, moduleKey, StringComparison.OrdinalIgnoreCase)
                                             && string.Equals(e.Manifest.Id, engineId, StringComparison.OrdinalIgnoreCase));
    }

    public static void Reset()
    {
        Engines.Clear();
    }
}
