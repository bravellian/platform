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
/// Resolves optional partition keys for webhook requests.
/// </summary>
public interface IWebhookPartitionResolver
{
    /// <summary>
    /// Resolves a partition key for the webhook envelope.
    /// </summary>
    /// <param name="envelope">Webhook envelope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The partition key, or <c>null</c> if none applies.</returns>
    Task<string?> ResolvePartitionKeyAsync(WebhookEnvelope envelope, CancellationToken cancellationToken);
}
