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
    private static readonly ReaderWriterLockSlim RegistryLock = new();

    public static void Register(string moduleKey, IEnumerable<IModuleEngineDescriptor> descriptors)
    {
        RegistryLock.EnterWriteLock();
        try
        {
            var list = Engines.GetOrAdd(moduleKey, _ => new List<IModuleEngineDescriptor>());
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
        finally
        {
            RegistryLock.ExitWriteLock();
        }
    }

    public static IReadOnlyCollection<IModuleEngineDescriptor> GetEngines()
    {
        RegistryLock.EnterReadLock();
        try
        {
            // Take a snapshot of all registered engines.
            return Engines.Values.SelectMany(list => list).ToArray();
        }
        finally
        {
            RegistryLock.ExitReadLock();
        }
    }

    public static IModuleEngineDescriptor? FindWebhookEngine(string provider, string eventType)
    {
        RegistryLock.EnterReadLock();
        try
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
        finally
        {
            RegistryLock.ExitReadLock();
        }
    }

    public static IModuleEngineDescriptor? FindById(string moduleKey, string engineId)
    {
        RegistryLock.EnterReadLock();
        try
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
        finally
        {
            RegistryLock.ExitReadLock();
        }
    }

    public static void Reset()
    {
        RegistryLock.EnterWriteLock();
        try
        {
            Engines.Clear();
        }
        finally
        {
            RegistryLock.ExitWriteLock();
        }
    }
}
