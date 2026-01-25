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

public sealed record ExternalSideEffectRecord
{
    public Guid Id { get; init; }

    public required string OperationName { get; init; }

    public required string IdempotencyKey { get; init; }

    public ExternalSideEffectStatus Status { get; init; }

    public int AttemptCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastUpdatedAt { get; init; }

    public DateTimeOffset? LastAttemptAt { get; init; }

    public DateTimeOffset? LastExternalCheckAt { get; init; }

    public DateTimeOffset? LockedUntil { get; init; }

    public Guid? LockedBy { get; init; }

    public string? CorrelationId { get; init; }

    public Guid? OutboxMessageId { get; init; }

    public string? ExternalReferenceId { get; init; }

    public string? ExternalStatus { get; init; }

    public string? LastError { get; init; }

    public string? PayloadHash { get; init; }
}
