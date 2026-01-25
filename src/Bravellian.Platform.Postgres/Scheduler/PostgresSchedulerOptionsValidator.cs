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

internal sealed class PostgresSchedulerOptionsValidator : IValidateOptions<PostgresSchedulerOptions>
{
    private static readonly TimeSpan MinPollingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxPollingInterval = TimeSpan.FromMinutes(15);

    public ValidateOptionsResult Validate(string? name, PostgresSchedulerOptions options)
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

        if (string.IsNullOrWhiteSpace(options.JobsTableName))
        {
            failures.Add("JobsTableName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.JobRunsTableName))
        {
            failures.Add("JobRunsTableName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.TimersTableName))
        {
            failures.Add("TimersTableName is required.");
        }

        if (options.MaxPollingInterval < MinPollingInterval || options.MaxPollingInterval > MaxPollingInterval)
        {
            failures.Add($"MaxPollingInterval must be between {MinPollingInterval} and {MaxPollingInterval}.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}





