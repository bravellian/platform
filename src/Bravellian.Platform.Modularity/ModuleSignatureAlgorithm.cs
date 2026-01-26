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

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Enumerates supported signature algorithms for webhook verification.
/// </summary>
public enum ModuleSignatureAlgorithm
{
    /// <summary>
    /// No signature verification is required.
    /// </summary>
    None = 0,
    /// <summary>
    /// HMAC SHA-256 signature.
    /// </summary>
    HmacSha256 = 1,
    /// <summary>
    /// HMAC SHA-512 signature.
    /// </summary>
    HmacSha512 = 2,
    /// <summary>
    /// RSA SHA-256 signature.
    /// </summary>
    RsaSha256 = 3
}
