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
/// Registration helpers for background modules.
/// </summary>
public static class BackgroundModuleRegistry
{
    /// <summary>
    /// Registers a background module type.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    public static void RegisterBackgroundModule<T>() where T : class, IBackgroundModule, new()
    {
        ModuleRegistry.RegisterModuleType(typeof(T), ModuleCategory.Background);
    }

    /// <summary>
    /// Gets a snapshot of all registered background module types.
    /// </summary>
    /// <returns>A read-only collection of registered background module types.</returns>
    public static IReadOnlyCollection<Type> GetRegisteredModuleTypes()
    {
        return ModuleRegistry.GetRegisteredTypes(ModuleCategory.Background);
    }

    /// <summary>
    /// Clears all registered module types and instances across all categories.
    /// </summary>
    /// <remarks>
    /// This method is intended for testing purposes only. It should not be used in production code
    /// as it affects global state that may be shared across different parts of the application.
    /// Tests using this method should not be run in parallel to avoid race conditions.
    /// </remarks>
    internal static void Reset()
    {
        ModuleRegistry.Reset();
    }
}
