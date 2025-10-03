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

namespace Bravellian.Platform;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Singleton service that provides coordination for database schema deployment completion.
/// This class is separate from the background service to ensure proper dependency injection lifecycle management.
/// </summary>
internal sealed class DatabaseSchemaCompletion : IDatabaseSchemaCompletion
{
    private readonly TaskCompletionSource<bool> completionSource = new();

    /// <summary>
    /// Gets a task that completes when database schema deployment is finished.
    /// </summary>
    public Task SchemaDeploymentCompleted => this.completionSource.Task;

    /// <summary>
    /// Marks the schema deployment as completed successfully.
    /// </summary>
    public void SetCompleted()
    {
        this.completionSource.SetResult(true);
    }

    /// <summary>
    /// Marks the schema deployment as cancelled.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that was triggered.</param>
    public void SetCancelled(CancellationToken cancellationToken)
    {
        this.completionSource.SetCanceled(cancellationToken);
    }

    /// <summary>
    /// Marks the schema deployment as failed with an exception.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    public void SetException(System.Exception exception)
    {
        this.completionSource.SetException(exception);
    }
}
