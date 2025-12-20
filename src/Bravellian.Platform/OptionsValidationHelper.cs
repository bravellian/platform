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

/// <summary>
/// Internal helper for validating options across multiple DI extension methods.
/// </summary>
internal static class OptionsValidationHelper
{
    /// <summary>
    /// Validates options using the provided validator and throws if validation fails.
    /// </summary>
    /// <typeparam name="TOptions">The options type to validate.</typeparam>
    /// <param name="options">The options instance to validate.</param>
    /// <param name="validator">The validator to use.</param>
    /// <exception cref="ArgumentNullException">Thrown if options or validator is null.</exception>
    /// <exception cref="OptionsValidationException">Thrown if validation fails.</exception>
    internal static void ValidateAndThrow<TOptions>(TOptions options, IValidateOptions<TOptions> validator)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);

        var validationResult = validator.Validate(Options.DefaultName, options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(TOptions), validationResult.Failures);
        }
    }
}
