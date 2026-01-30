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
/// Represents a startup check that runs during application initialization.
/// </summary>
public interface IStartupCheck
{
    /// <summary>
    /// Gets the stable, unique identifier for the check.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the execution order for the check. Lower values run first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Gets a value indicating whether a failure should block startup. Defaults to true by convention.
    /// </summary>
    bool IsCritical { get; }

    /// <summary>
    /// Executes the startup check. Throw to indicate failure.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ExecuteAsync(CancellationToken ct);
}
