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

public sealed class OutboxMessage
{
    public Guid Id { get; internal set; }

    public string Payload { get; internal set; }

    public string Topic { get; internal set; }

    public DateTimeOffset CreatedAt { get; internal set; }

    public bool IsProcessed { get; internal set; }

    public DateTimeOffset? ProcessedAt { get; internal set; }

    public string? ProcessedBy { get; internal set; }

    public int RetryCount { get; internal set; }

    public string? LastError { get; internal set; }

    public DateTimeOffset NextAttemptAt { get; internal set; }

    public Guid MessageId { get; internal set; }

    public Guid? CorrelationId { get; internal set; }
}
