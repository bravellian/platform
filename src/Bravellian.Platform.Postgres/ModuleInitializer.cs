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

using System.Runtime.CompilerServices;

namespace Bravellian.Platform;

/// <summary>
/// Module initializer that ensures Dapper type handlers are registered
/// as soon as the assembly is loaded.
/// </summary>
internal static class ModuleInitializer
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute is appropriate here to ensure Dapper type handlers are registered before any code uses them
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Register Dapper type handlers for all strongly-typed IDs
        // This ensures handlers are available before any code runs
        PostgresDapperTypeHandlerRegistration.RegisterTypeHandlers();
    }
#pragma warning restore CA2255
}





