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
/// Represents the result of validating an audit event.
/// </summary>
public sealed record AuditValidationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditValidationResult"/> record.
    /// </summary>
    /// <param name="errors">Validation errors.</param>
    public AuditValidationResult(IReadOnlyList<string> errors)
    {
        Errors = errors ?? throw new ArgumentNullException(nameof(errors));
    }

    /// <summary>
    /// Gets a value indicating whether the validation succeeded.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <returns>Successful validation result.</returns>
    public static AuditValidationResult Success() => new(Array.Empty<string>());
}
