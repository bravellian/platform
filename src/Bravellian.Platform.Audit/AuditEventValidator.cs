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
/// Validates audit events.
/// </summary>
public static class AuditEventValidator
{
    /// <summary>
    /// Validates the supplied audit event.
    /// </summary>
    /// <param name="auditEvent">Audit event to validate.</param>
    /// <param name="options">Validation options.</param>
    /// <returns>Validation result.</returns>
    public static AuditValidationResult Validate(AuditEvent auditEvent, AuditValidationOptions? options = null)
    {
        if (auditEvent is null)
        {
            throw new ArgumentNullException(nameof(auditEvent));
        }

        options ??= new AuditValidationOptions();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(auditEvent.Name))
        {
            errors.Add("Event name is required.");
        }

        if (string.IsNullOrWhiteSpace(auditEvent.DisplayMessage))
        {
            errors.Add("Display message is required.");
        }

        if (auditEvent.Anchors is null || auditEvent.Anchors.Count == 0)
        {
            errors.Add("At least one anchor is required.");
        }

        if (auditEvent.DataJson is not null && auditEvent.DataJson.Length > options.MaxDataJsonLength)
        {
            errors.Add($"DataJson exceeds maximum length of {options.MaxDataJsonLength} characters.");
        }

        return errors.Count == 0
            ? AuditValidationResult.Success()
            : new AuditValidationResult(errors);
    }
}