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

namespace Bravellian.Platform;

/// <summary>
/// Validation helpers for startup check registration.
/// </summary>
public static class StartupCheckValidator
{
    /// <summary>
    /// Validates a startup check instance.
    /// </summary>
    /// <param name="check">The startup check to validate.</param>
    public static void Validate(IStartupCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (string.IsNullOrWhiteSpace(check.Name))
        {
            throw new ArgumentException("Startup check name must be provided.", nameof(check));
        }
    }

    /// <summary>
    /// Validates that startup checks have unique names.
    /// </summary>
    /// <param name="checks">The startup checks to validate.</param>
    public static void ValidateUniqueNames(IEnumerable<IStartupCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in checks)
        {
            Validate(check);

            if (!seen.Add(check.Name))
            {
                throw new InvalidOperationException($"Duplicate startup check name '{check.Name}' was registered.");
            }
        }
    }
}
