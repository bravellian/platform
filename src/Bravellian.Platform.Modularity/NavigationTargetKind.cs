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
/// Enumerates the supported navigation targets that hosts can map to runtime concepts.
/// </summary>
public enum NavigationTargetKind
{
    /// <summary>
    /// Navigate to an in-app route.
    /// </summary>
    Route = 0,
    /// <summary>
    /// Open a modal or dialog surface.
    /// </summary>
    Dialog = 1,
    /// <summary>
    /// Render a component in the host UI.
    /// </summary>
    Component = 2,
    /// <summary>
    /// Navigate to an external URL.
    /// </summary>
    External = 3
}
