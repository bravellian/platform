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

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Composes navigation links from registered modules.
/// </summary>
public sealed class ModuleNavigationService
{
    private readonly IEnumerable<IFullStackModule> modules;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleNavigationService"/> class.
    /// </summary>
    /// <param name="modules">The module instances.</param>
    public ModuleNavigationService(IEnumerable<IFullStackModule> modules)
    {
        this.modules = modules;
    }

    /// <summary>
    /// Builds a flattened list of navigation entries.
    /// </summary>
    public IReadOnlyList<ModuleNavigationEntry> BuildNavigation()
    {
        var entries = new List<ModuleNavigationEntry>();
        foreach (var module in modules)
        {
            var metadata = module as INavigationModuleMetadata;
            foreach (var link in module.GetNavLinks())
            {
                entries.Add(new ModuleNavigationEntry(
                    module.Key,
                    link.Path,
                    link.Title,
                    link.Order,
                    metadata?.NavigationGroup ?? "Primary",
                    metadata?.NavigationOrder ?? 0,
                    link.Icon));
            }
        }

        return entries
            .OrderBy(e => e.GroupOrder)
            .ThenBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ModuleKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.LinkOrder)
            .ToArray();
    }
}
