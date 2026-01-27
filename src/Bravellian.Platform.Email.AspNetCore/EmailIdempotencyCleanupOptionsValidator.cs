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

using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Email.AspNetCore;

internal sealed class EmailIdempotencyCleanupOptionsValidator : IValidateOptions<EmailIdempotencyCleanupOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailIdempotencyCleanupOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Options are required.");
        }

        var failures = new List<string>();

        if (options.RetentionPeriod <= TimeSpan.Zero)
        {
            failures.Add("RetentionPeriod must be greater than zero.");
        }

        if (options.CleanupInterval <= TimeSpan.Zero)
        {
            failures.Add("CleanupInterval must be greater than zero.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
