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
/// Handles outbox messages for a specific topic.
/// Implementations can perform local work (email, reports) or forward to brokers.
/// </summary>
public interface IOutboxHandler
{
    /// <summary>Topic this handler serves (e.g., "Email.Send").</summary>
    string Topic { get; }

    /// <summary>Perform the work. Throw for transient/permanent failures (we'll backoff or dead-letter).</summary>
    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}