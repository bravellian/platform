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
/// Generic UI engine contract that operates on DTOs and produces view models.
/// </summary>
/// <typeparam name="TInput">Input DTO.</typeparam>
/// <typeparam name="TViewModel">View model output.</typeparam>
public interface IUiEngine<TInput, TViewModel>
{
    /// <summary>
    /// Executes the engine using the provided command DTO.
    /// </summary>
    /// <param name="command">Command DTO.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A view model and any navigation tokens emitted.</returns>
    Task<UiEngineResult<TViewModel>> ExecuteAsync(TInput command, CancellationToken cancellationToken);
}
