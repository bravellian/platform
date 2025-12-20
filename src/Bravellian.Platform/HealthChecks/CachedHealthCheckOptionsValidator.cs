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

namespace Bravellian.Platform.HealthChecks;

internal sealed class CachedHealthCheckOptionsValidator : IValidateOptions<CachedHealthCheckOptions>
{
    public ValidateOptionsResult Validate(string? name, CachedHealthCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.HealthyCacheDuration < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("HealthyCacheDuration cannot be negative.");
        }

        if (options.DegradedCacheDuration < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("DegradedCacheDuration cannot be negative.");
        }

        if (options.UnhealthyCacheDuration < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("UnhealthyCacheDuration cannot be negative.");
        }

        return ValidateOptionsResult.Success;
    }
}
