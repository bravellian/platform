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

using System;

namespace Bravellian.Platform;

/// <summary>
/// Represents a latch that tracks named startup steps until all are completed.
/// </summary>
public interface IStartupLatch
{
    /// <summary>
    /// Gets a value indicating whether all registered startup steps are complete.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Registers a named startup step and returns a disposable that clears the step when disposed.
    /// </summary>
    /// <param name="stepName">The name of the startup step.</param>
    /// <returns>An <see cref="IDisposable"/> that removes the step when disposed.</returns>
    IDisposable Register(string stepName);
}
