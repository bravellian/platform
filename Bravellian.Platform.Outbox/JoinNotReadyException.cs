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

/// <summary>
/// Exception thrown when a join is not ready to be completed (not all steps finished).
/// This causes the message to be abandoned and retried later.
/// </summary>
public class JoinNotReadyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JoinNotReadyException"/> class.
    /// </summary>
    public JoinNotReadyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinNotReadyException"/> class.
    /// </summary>
    public JoinNotReadyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinNotReadyException"/> class.
    /// </summary>
    public JoinNotReadyException()
    {
    }
}
