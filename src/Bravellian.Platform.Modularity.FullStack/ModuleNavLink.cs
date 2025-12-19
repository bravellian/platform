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

using System.Text;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Simple navigation link.
/// </summary>
/// <param name="Title">Display text.</param>
/// <param name="Path">Path relative to the module root.</param>
/// <param name="Order">Order within the module's navigation.</param>
/// <param name="Icon">Optional icon identifier.</param>
public sealed record ModuleNavLink(string Title, string Path, int Order = 0, string? Icon = null)
{
    /// <summary>
    /// Creates a normalized navigation link.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="path">The relative path.</param>
    /// <param name="order">The order.</param>
    /// <param name="icon">The icon identifier.</param>
    /// <returns>The navigation link.</returns>
    public static ModuleNavLink Create(string title, string path, int order = 0, string? icon = null)
    {
        return new ModuleNavLink(title, NormalizePath(path), order, icon);
    }

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var trimmed = path.Trim();
        
        // Collapse consecutive slashes using a single-pass algorithm
        var builder = new StringBuilder(trimmed.Length);
        var lastWasSlash = false;
        
        foreach (var c in trimmed)
        {
            if (c == '/')
            {
                if (!lastWasSlash)
                {
                    builder.Append(c);
                    lastWasSlash = true;
                }
            }
            else
            {
                builder.Append(c);
                lastWasSlash = false;
            }
        }
        
        var normalized = builder.ToString();
        
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            normalized = $"/{normalized}";
        }

        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }
}
