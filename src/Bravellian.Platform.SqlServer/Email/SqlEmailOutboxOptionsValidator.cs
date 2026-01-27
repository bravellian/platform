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

internal sealed class SqlEmailOutboxOptionsValidator : IValidateOptions<SqlEmailOutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, SqlEmailOutboxOptions options)
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

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
