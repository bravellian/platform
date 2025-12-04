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


using System.Text.RegularExpressions;

namespace Bravellian.Platform.Semaphore;
/// <summary>
/// Validator for semaphore parameters.
/// </summary>
internal static partial class SemaphoreValidator
{
    private const int MaxNameLength = 200;
    private const int MaxOwnerIdLength = 200;

    [GeneratedRegex("^[a-zA-Z0-9\\-_:/.]{1,200}$", RegexOptions.Compiled | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex NamePattern();

    /// <summary>
    /// Validates a semaphore name.
    /// </summary>
    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"Semaphore name cannot exceed {MaxNameLength} characters.", nameof(name));
        }

        if (!NamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                "Semaphore name can only contain letters, digits, dash, underscore, colon, slash, and period.",
                nameof(name));
        }
    }

    /// <summary>
    /// Validates an owner ID.
    /// </summary>
    public static void ValidateOwnerId(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (ownerId.Length > MaxOwnerIdLength)
        {
            throw new ArgumentException($"Owner ID cannot exceed {MaxOwnerIdLength} characters.", nameof(ownerId));
        }
    }

    /// <summary>
    /// Validates a TTL against configured bounds.
    /// </summary>
    public static void ValidateTtl(int ttlSeconds, int minTtl, int maxTtl)
    {
        if (ttlSeconds < minTtl)
        {
            throw new ArgumentException($"TTL must be at least {minTtl} seconds.", nameof(ttlSeconds));
        }

        if (ttlSeconds > maxTtl)
        {
            throw new ArgumentException($"TTL cannot exceed {maxTtl} seconds.", nameof(ttlSeconds));
        }
    }

    /// <summary>
    /// Validates a semaphore limit.
    /// </summary>
    public static void ValidateLimit(int limit, int maxLimit)
    {
        if (limit < 1)
        {
            throw new ArgumentException("Limit must be at least 1.", nameof(limit));
        }

        if (limit > maxLimit)
        {
            throw new ArgumentException($"Limit cannot exceed {maxLimit}.", nameof(limit));
        }
    }
}
