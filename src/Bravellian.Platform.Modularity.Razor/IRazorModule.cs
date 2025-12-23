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

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Module that provides Razor Pages UI on top of module engines.
/// </summary>
public interface IRazorModule : IModuleDefinition
{
    /// <summary>
    /// Name of the Razor Pages area.
    /// </summary>
    string AreaName { get; }

    /// <summary>
    /// Configures Razor Pages conventions for the module.
    /// </summary>
    /// <param name="options">The Razor pages options.</param>
    void ConfigureRazorPages(RazorPagesOptions options);
}
