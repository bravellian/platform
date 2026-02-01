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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Webhooks.AspNetCore.Tests;

public sealed class WebhookProcessingHostedServiceTests
{
    /// <summary>When hosted Service Invokes Processor Async, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for hosted Service Invokes Processor Async.</intent>
    /// <scenario>Given hosted Service Invokes Processor Async.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public async Task HostedServiceInvokesProcessorAsync()
    {
        var processor = new RecordingProcessor();
        var options = Options.Create(new WebhookProcessingOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            BatchSize = 10,
            MaxAttempts = 3,
        });
        using var service = new WebhookProcessingHostedService(processor, options, NullLogger<WebhookProcessingHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        var completed = await processor.FirstCall.Task.WaitAsync(TimeSpan.FromSeconds(1), Xunit.TestContext.Current.CancellationToken);
        completed.ShouldBeTrue();

        await service.StopAsync(CancellationToken.None);
        processor.CallCount.ShouldBeGreaterThan(0);
    }

    private sealed class RecordingProcessor : IWebhookProcessor
    {
        private int callCount;
        private int signaled;

        public TaskCompletionSource<bool> FirstCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => callCount;

        public Task<int> RunOnceAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            if (Interlocked.Exchange(ref signaled, 1) == 0)
            {
                FirstCall.TrySetResult(true);
            }

            return Task.FromResult(1);
        }
    }
}
