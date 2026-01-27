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

namespace Bravellian.Platform.ExactlyOnce;

/// <summary>
/// Resolves the stable idempotency key for an item.
/// </summary>
/// <typeparam name="TItem">Item type.</typeparam>
public interface IExactlyOnceKeyResolver<in TItem>
{
    /// <summary>
    /// Gets the stable idempotency key for the provided item.
    /// </summary>
    /// <param name="item">Item being processed.</param>
    /// <returns>Stable idempotency key.</returns>
    string GetKey(TItem item);
}
