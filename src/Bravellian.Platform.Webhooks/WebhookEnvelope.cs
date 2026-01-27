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

namespace Bravellian.Platform.Webhooks;

/// <summary>
/// Raw webhook request representation captured by the ingress layer.
/// </summary>
/// <param name="Provider">Webhook provider identifier.</param>
/// <param name="ReceivedAtUtc">UTC timestamp when the webhook was received.</param>
/// <param name="Method">HTTP method used for the request.</param>
/// <param name="Path">Request path.</param>
/// <param name="QueryString">Raw query string including leading '?' if present.</param>
/// <param name="Headers">Request headers.</param>
/// <param name="ContentType">Request content type.</param>
/// <param name="BodyBytes">Raw request body bytes.</param>
/// <param name="RemoteIpAddress">Remote IP address if captured by the ingress layer.</param>
public sealed record WebhookEnvelope(
    string Provider,
    DateTimeOffset ReceivedAtUtc,
    string Method,
    string Path,
    string QueryString,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    byte[] BodyBytes,
    string? RemoteIpAddress);
