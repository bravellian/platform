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

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Postmark outbound message details.
/// </summary>
public sealed record PostmarkOutboundMessageDetails
{
    /// <summary>
    /// Gets the Postmark message identifier.
    /// </summary>
    [JsonPropertyName("MessageID")]
    public string? MessageId { get; init; }

    /// <summary>
    /// Gets the outbound message status.
    /// </summary>
    [JsonPropertyName("Status")]
    public string? Status { get; init; }

    /// <summary>
    /// Gets the metadata attached to the outbound message.
    /// </summary>
    [JsonPropertyName("Metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
