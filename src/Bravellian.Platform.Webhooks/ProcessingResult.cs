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

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Result of processing a webhook event.
/// </summary>
/// <param name="Status">Final processing status.</param>
/// <param name="AttemptCount">Attempt count for the processing run.</param>
/// <param name="ErrorMessage">Optional error message.</param>
public sealed record ProcessingResult(
    WebhookEventStatus Status,
    int AttemptCount,
    string? ErrorMessage);
