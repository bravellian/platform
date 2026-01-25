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

namespace Bravellian.Platform;

internal sealed class PostgresOutboxOptionsValidator : IValidateOptions<PostgresOutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, PostgresOutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("ConnectionString is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SchemaName))
        {
            failures.Add("SchemaName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.TableName))
        {
            failures.Add("TableName is required.");
        }

        if (options.RetentionPeriod <= TimeSpan.Zero)
        {
            failures.Add("RetentionPeriod must be greater than zero.");
        }

        if (options.EnableAutomaticCleanup && options.CleanupInterval <= TimeSpan.Zero)
        {
            failures.Add("CleanupInterval must be greater than zero when automatic cleanup is enabled.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            failures.Add("LeaseDuration must be greater than zero.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}





