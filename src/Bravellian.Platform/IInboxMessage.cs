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
public interface IInboxMessage
{
    /// <summary>
    /// The unique identifier of the message.
    /// </summary>
    string MessageId { get; }

    /// <summary>
    /// The source system or component that sent the message.
    /// </summary>
    string Source { get; }

    /// <summary>
    /// The topic used to resolve an IInboxHandler for this message.
    /// </summary>
    string Topic { get; }

    /// <summary>
    /// The raw payload content (JSON/string).
    /// </summary>
    string Payload { get; }

    /// <summary>
    /// Optional content hash for deduplication verification.
    /// </summary>
    byte[]? Hash { get; }

    /// <summary>
    /// Current attempt count for this message.
    /// </summary>
    int Attempt { get; }

    /// <summary>
    /// When this message was first seen by the system.
    /// </summary>
    DateTimeOffset FirstSeenUtc { get; }

    /// <summary>
    /// When this message was last updated/seen by the system.
    /// </summary>
    DateTimeOffset LastSeenUtc { get; }
}