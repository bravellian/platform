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

internal sealed class PostgresIdempotencyOptionsValidator : IValidateOptions<PostgresIdempotencyOptions>
{
    public ValidateOptionsResult Validate(string? name, PostgresIdempotencyOptions options)
    {
        if (options == null)
        {
            return ValidateOptionsResult.Fail("Options are required.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return ValidateOptionsResult.Fail("ConnectionString must be provided.");
        }

        if (string.IsNullOrWhiteSpace(options.SchemaName))
        {
            return ValidateOptionsResult.Fail("SchemaName must be provided.");
        }

        if (string.IsNullOrWhiteSpace(options.TableName))
        {
            return ValidateOptionsResult.Fail("TableName must be provided.");
        }

        if (!IsValidLockDuration(options.LockDuration))
        {
            return ValidateOptionsResult.Fail("LockDuration must be positive or Timeout.InfiniteTimeSpan.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsValidLockDuration(TimeSpan duration)
    {
        return duration == Timeout.InfiniteTimeSpan || duration > TimeSpan.Zero;
    }
}
