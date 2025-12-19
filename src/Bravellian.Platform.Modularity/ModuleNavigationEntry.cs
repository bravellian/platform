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
/// Flattened navigation entry.
/// </summary>
/// <param name="ModuleKey">Module key.</param>
/// <param name="RelativePath">Path relative to the module root.</param>
/// <param name="Title">Display title.</param>
/// <param name="LinkOrder">Order within the module.</param>
/// <param name="Group">Navigation group.</param>
/// <param name="GroupOrder">Group order.</param>
/// <param name="Icon">Optional icon identifier.</param>
public sealed record ModuleNavigationEntry(
    string ModuleKey,
    string RelativePath,
    string Title,
    int LinkOrder,
    string Group,
    int GroupOrder,
    string? Icon)
{
    /// <summary>
    /// Full navigation path combining module key and relative path.
    /// </summary>
    public string FullPath => $"/{ModuleKey}{RelativePath}";
}
