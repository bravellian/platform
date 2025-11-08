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
/// Routes fanout operations to the appropriate database based on a routing key.
/// This enables multi-tenant fanout processing where each tenant has their own database.
/// </summary>
public interface IFanoutRouter
{
    /// <summary>
    /// Gets a fanout policy repository for the specified routing key.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The fanout policy repository for the specified key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no repository is found for the key.</exception>
    IFanoutPolicyRepository GetPolicyRepository(string key);

    /// <summary>
    /// Gets a fanout cursor repository for the specified routing key.
    /// </summary>
    /// <param name="key">The routing key (e.g., tenant ID, customer ID).</param>
    /// <returns>The fanout cursor repository for the specified key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no repository is found for the key.</exception>
    IFanoutCursorRepository GetCursorRepository(string key);
}
