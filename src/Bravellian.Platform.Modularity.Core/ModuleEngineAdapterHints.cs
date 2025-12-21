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
/// Adapter-level hints used by hosts to wire up engines to transports.
/// </summary>
/// <param name="RequiresRawRequestBody">True if the adapter must expose the raw request body to the engine.</param>
/// <param name="RequiresRawHeaders">True if the adapter must expose raw headers.</param>
/// <param name="SupportsChallengeResponses">True if the adapter should support verification/challenge responses.</param>
/// <param name="RequiresAuthenticatedUser">True if the adapter must enforce authentication.</param>
/// <param name="RequiresTenantContext">True if the adapter must enforce tenancy.</param>
public sealed record ModuleEngineAdapterHints(
    bool RequiresRawRequestBody = false,
    bool RequiresRawHeaders = false,
    bool SupportsChallengeResponses = false,
    bool RequiresAuthenticatedUser = false,
    bool RequiresTenantContext = false);
