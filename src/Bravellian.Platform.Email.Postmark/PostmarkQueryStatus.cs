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

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Postmark query status.
/// </summary>
public enum PostmarkQueryStatus
{
    /// <summary>
    /// The lookup was successful.
    /// </summary>
    Found = 0,

    /// <summary>
    /// The requested item was not found.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// The lookup failed.
    /// </summary>
    Error = 2,
}
