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

namespace Bravellian.Platform.Correlation;

/// <summary>
/// Sets the current correlation context for the lifetime of a scope.
/// </summary>
public sealed class CorrelationScope : IDisposable
{
    private readonly ICorrelationContextAccessor accessor;
    private readonly CorrelationContext? previous;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationScope"/> class.
    /// </summary>
    /// <param name="accessor">Accessor to update.</param>
    /// <param name="context">Correlation context to set.</param>
    public CorrelationScope(ICorrelationContextAccessor accessor, CorrelationContext context)
    {
        this.accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _ = context ?? throw new ArgumentNullException(nameof(context));

        previous = accessor.Current;
        accessor.Current = context;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        accessor.Current = previous;
        disposed = true;
    }
}
