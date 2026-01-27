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
/// Defines a serializer for correlation contexts.
/// </summary>
public interface ICorrelationSerializer
{
    /// <summary>
    /// Serializes a correlation context into a dictionary suitable for headers or metadata.
    /// </summary>
    /// <param name="context">Correlation context to serialize.</param>
    /// <returns>Serialized dictionary.</returns>
    IReadOnlyDictionary<string, string> Serialize(CorrelationContext context);

    /// <summary>
    /// Deserializes a correlation context from a dictionary.
    /// </summary>
    /// <param name="values">Dictionary containing correlation metadata.</param>
    /// <returns>Deserialized correlation context, or <c>null</c> when missing.</returns>
    CorrelationContext? Deserialize(IReadOnlyDictionary<string, string> values);
}