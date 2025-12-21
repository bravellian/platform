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
/// Declares the actions and events an engine can process.
/// </summary>
/// <param name="Actions">Named commands/actions the engine supports.</param>
/// <param name="Events">Events emitted by the engine.</param>
/// <param name="SupportsAsync">Indicates async execution is supported.</param>
/// <param name="SupportsStreaming">Indicates streaming updates are supported.</param>
public sealed record ModuleEngineCapabilities(
    IReadOnlyCollection<string> Actions,
    IReadOnlyCollection<string> Events,
    bool SupportsAsync = true,
    bool SupportsStreaming = false);
