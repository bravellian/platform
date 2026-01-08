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

using System.Threading;

namespace Bravellian.Platform;

/// <summary>
/// Provides a thread-safe registry for one-time operations keyed by string.
/// </summary>
public sealed class OnceExecutionRegistry
{
    private readonly Lock syncRoot = new();
    private readonly HashSet<string> executedKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether the specified key has been marked as executed.
    /// </summary>
    /// <param name="key">The operation key.</param>
    /// <returns><see langword="true"/> if the key has already been marked; otherwise, <see langword="false"/>.</returns>
    public bool HasRun(string key)
    {
        var normalizedKey = NormalizeKey(key);

        using (syncRoot.EnterScope())
        {
            return executedKeys.Contains(normalizedKey);
        }
    }

    /// <summary>
    /// Checks if the specified key has been marked, and if not, marks it as executed.
    /// </summary>
    /// <param name="key">The operation key.</param>
    /// <returns>
    /// <see langword="true"/> if the key was already marked; otherwise, <see langword="false"/>,
    /// indicating the caller should perform the operation.
    /// </returns>
    public bool CheckAndMark(string key)
    {
        var normalizedKey = NormalizeKey(key);

        using (syncRoot.EnterScope())
        {
            if (executedKeys.Contains(normalizedKey))
            {
                return true;
            }

            executedKeys.Add(normalizedKey);
            return false;
        }
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Execution key must not be null or whitespace.", nameof(key));
        }

        return key.Trim();
    }
}
