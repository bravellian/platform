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
/// Configuration options for webhook processing.
/// </summary>
public sealed class WebhookProcessorOptions
{
    /// <summary>
    /// Gets or sets the maximum number of messages to claim per iteration.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the lease duration in seconds for claimed messages.
    /// </summary>
    public int LeaseSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum number of attempts before poisoning a message.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the base backoff used for exponential retry delays.
    /// </summary>
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets the maximum backoff delay for retries.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the behavior when no handler is registered for an event type.
    /// </summary>
    public WebhookMissingHandlerBehavior MissingHandlerBehavior { get; set; } = WebhookMissingHandlerBehavior.Complete;
}
