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

using Bravellian.Platform.Email;

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Validates outbound email messages against Postmark requirements.
/// </summary>
public interface IPostmarkEmailValidator
{
    /// <summary>
    /// Validates the provided message.
    /// </summary>
    /// <param name="message">Outbound email message.</param>
    /// <returns>Validation result.</returns>
    ValidationResult Validate(OutboundEmailMessage message);
}
