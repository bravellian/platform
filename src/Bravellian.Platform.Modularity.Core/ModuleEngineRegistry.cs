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
    
    // Single lock protects registry operations without disposal requirements.
    private static readonly Lock RegistryLock = new();
    private static IModuleEngineDescriptor[]? CachedSnapshot;

    public static void Register(string moduleKey, IEnumerable<IModuleEngineDescriptor> descriptors)
    {
        lock (RegistryLock)
        {
            var list = Engines.GetOrAdd(moduleKey, _ => new List<IModuleEngineDescriptor>());
            foreach (var descriptor in descriptors)
            {
                ValidateWebhookMetadataUniqueness(descriptor);
                var exists = list.Any(existing => string.Equals(existing.ModuleKey, descriptor.ModuleKey, StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(existing.Manifest.Id, descriptor.Manifest.Id, StringComparison.OrdinalIgnoreCase)
                                                && existing.ContractType == descriptor.ContractType);
                if (!exists)
                {
                    list.Add(descriptor);
                }
            }

            CachedSnapshot = null;
        }
    }

    private static void ValidateWebhookMetadataUniqueness(IModuleEngineDescriptor descriptor)
    {
        var metadata = descriptor.Manifest.WebhookMetadata;
        if (metadata is null)
        {
            return;
        }

        var seen = new HashSet<(string Provider, string EventType)>(new WebhookMetadataKeyComparer());

        foreach (var entry in metadata)
        {
            if (!seen.Add((entry.Provider, entry.EventType)))
            {
                throw new InvalidOperationException(
                    $"Engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' declares duplicate webhook metadata for provider '{entry.Provider}' and event '{entry.EventType}'.");
            }

            foreach (var existingList in Engines.Values)
            {
                foreach (var existing in existingList)
                {
                    if (existing.Manifest.WebhookMetadata is null)
                    {
                        continue;
                    }

                    foreach (var existingEntry in existing.Manifest.WebhookMetadata)
                    {
                        if (string.Equals(existingEntry.Provider, entry.Provider, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(existingEntry.EventType, entry.EventType, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                $"Webhook provider '{entry.Provider}' and event '{entry.EventType}' are already handled by engine '{existing.ModuleKey}/{existing.Manifest.Id}'.");
                        }
                    }
                }
            }
        }
    }

    private sealed class WebhookMetadataKeyComparer : IEqualityComparer<(string Provider, string EventType)>
    {
        public bool Equals((string Provider, string EventType) x, (string Provider, string EventType) y)
        {
            return string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.EventType, y.EventType, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Provider, string EventType) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Provider ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.EventType ?? string.Empty));
        }
    }

    public static IReadOnlyCollection<IModuleEngineDescriptor> GetEngines()
    {
        lock (RegistryLock)
        {
            if (CachedSnapshot is { } cached)
            {
                return cached;
            }

            // Snapshot the engine lists while holding the lock to avoid races with registry mutations.
            var lists = Engines.Values.ToArray();
            cached = lists.SelectMany(list => list).ToArray();
            CachedSnapshot = cached;
            return cached;
        }
    }

    public static IModuleEngineDescriptor? FindWebhookEngine(string provider, string eventType)
    {
        lock (RegistryLock)
        {
            // Search for webhook engine matching the provider and event type.
            foreach (var list in Engines.Values)
            {
                foreach (var descriptor in list)
                {
                    if (descriptor.Manifest.Kind != EngineKind.Webhook)
                    {
                        continue;
                    }

                    var metadataCollection = descriptor.Manifest.WebhookMetadata;
                    if (metadataCollection == null)
                    {
                        continue;
                    }

                    foreach (var meta in metadataCollection)
                    {
                        if (string.Equals(meta.Provider, provider, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(meta.EventType, eventType, StringComparison.OrdinalIgnoreCase))
                        {
                            return descriptor;
                        }
                    }
                }
            }

            return null;
        }
    }

    public static IModuleEngineDescriptor? FindById(string moduleKey, string engineId)
    {
        lock (RegistryLock)
        {
            // Narrow the lookup to the specific moduleKey.
            if (!Engines.TryGetValue(moduleKey, out var list))
            {
                return null;
            }

            foreach (var descriptor in list)
            {
                if (string.Equals(descriptor.Manifest.Id, engineId, StringComparison.OrdinalIgnoreCase))
                {
                    return descriptor;
                }
            }

            return null;
        }
    }

    public static void Reset()
    {
        lock (RegistryLock)
        {
            Engines.Clear();
            CachedSnapshot = null;
        }
    }
}
