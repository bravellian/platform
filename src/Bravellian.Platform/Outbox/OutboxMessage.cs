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

public sealed record OutboxMessage
{
    public Guid Id { get; internal init; }

    public required string Payload { get; internal init; }

    public required string Topic { get; internal init; }

    public DateTimeOffset CreatedAt { get; internal init; }

    public bool IsProcessed { get; internal init; }

    public DateTimeOffset? ProcessedAt { get; internal init; }

    public string? ProcessedBy { get; internal init; }

    public int RetryCount { get; internal init; }

    public string? LastError { get; internal init; }

    public DateTimeOffset NextAttemptAt { get; internal init; }

    public Guid MessageId { get; internal init; }

    public string? CorrelationId { get; internal init; }

    public DateTimeOffset? DueTimeUtc { get; internal init; }
}
