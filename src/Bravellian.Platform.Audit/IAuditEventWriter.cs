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

namespace Bravellian.Platform.Audit;

/// <summary>
/// Writes audit events.
/// </summary>
public interface IAuditEventWriter
{
    /// <summary>
    /// Writes an audit event to the underlying store.
    /// </summary>
    /// <param name="auditEvent">Audit event to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}