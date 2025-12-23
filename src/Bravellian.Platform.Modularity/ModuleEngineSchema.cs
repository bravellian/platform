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
/// Declares schema hints for engine inputs or outputs.
/// </summary>
/// <param name="Name">Schema name or role (e.g., "command", "payload").</param>
/// <param name="ClrType">Associated CLR type for serialization.</param>
/// <param name="Notes">Additional notes or validation hints.</param>
public sealed record ModuleEngineSchema(string Name, Type ClrType, string? Notes = null);
