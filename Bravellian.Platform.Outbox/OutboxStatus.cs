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
/// Defines the possible status values for outbox messages in the work queue pattern.
/// </summary>
internal static class OutboxStatus
{
    /// <summary>
    /// Message is ready to be claimed and processed.
    /// </summary>
    public const byte Ready = 0;

    /// <summary>
    /// Message has been claimed and is currently being processed.
    /// </summary>
    public const byte InProgress = 1;

    /// <summary>
    /// Message has been successfully processed.
    /// </summary>
    public const byte Done = 2;

    /// <summary>
    /// Message processing has permanently failed.
    /// </summary>
    public const byte Failed = 3;
}
