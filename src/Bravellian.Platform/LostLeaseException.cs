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

using System;

/// <summary>
/// Exception thrown when a lease has been lost.
/// </summary>
public class LostLeaseException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LostLeaseException"/> class.
    /// </summary>
    /// <param name="resourceName">The name of the resource whose lease was lost.</param>
    /// <param name="ownerToken">The owner token of the lost lease.</param>
    public LostLeaseException(string resourceName, Guid ownerToken)
        : base($"Lease for resource '{resourceName}' with owner token '{ownerToken}' has been lost.")
    {
        this.ResourceName = resourceName;
        this.OwnerToken = ownerToken;
    }

    /// <summary>
    /// Gets the name of the resource whose lease was lost.
    /// </summary>
    public string ResourceName { get; }

    /// <summary>
    /// Gets the owner token of the lost lease.
    /// </summary>
    public Guid OwnerToken { get; }
}