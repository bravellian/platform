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
/// Represents an inbound message for processing through the Inbox Handler system.
/// </summary>
public sealed record InboxMessage
{
    public string MessageId { get; internal init; } = string.Empty;

    public string Source { get; internal init; } = string.Empty;

    public string Topic { get; internal init; } = string.Empty;

    public string Payload { get; internal init; } = string.Empty;

    public byte[]? Hash { get; internal init; }

    public int Attempt { get; internal init; }

    public DateTimeOffset FirstSeenUtc { get; internal init; }

    public DateTimeOffset LastSeenUtc { get; internal init; }

    public DateTimeOffset? DueTimeUtc { get; internal init; }
}
