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

namespace Bravellian.Platform.Email.Tests;

public sealed class InMemoryEmailOutboxStoreTests
{
    [Fact]
    public async Task Enqueue_MarksMessageKey()
    {
        var store = new InMemoryEmailOutboxStore();
        var message = EmailFixtures.CreateMessage(messageKey: "key-1");
        var item = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            message.MessageKey,
            message,
            DateTimeOffset.UtcNow,
            null,
            0);

        var alreadyEnqueued = await store.AlreadyEnqueuedAsync(message.MessageKey, "postmark", CancellationToken.None);
        alreadyEnqueued.ShouldBeFalse();

        await store.EnqueueAsync(item, CancellationToken.None);

        var enqueuedAfter = await store.AlreadyEnqueuedAsync(message.MessageKey, "postmark", CancellationToken.None);
        enqueuedAfter.ShouldBeTrue();
    }

    [Fact]
    public async Task Dequeue_ReturnsPendingItemsInOrder()
    {
        var store = new InMemoryEmailOutboxStore();
        var first = EmailFixtures.CreateMessage("Hello 1", messageKey: "key-1");
        var second = EmailFixtures.CreateMessage("Hello 2", messageKey: "key-2");
        var firstItem = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            first.MessageKey,
            first,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            0);
        var secondItem = new EmailOutboxItem(
            Guid.NewGuid(),
            "postmark",
            second.MessageKey,
            second,
            DateTimeOffset.UtcNow,
            null,
            0);

        await store.EnqueueAsync(secondItem, CancellationToken.None);
        await store.EnqueueAsync(firstItem, CancellationToken.None);

        var batch = await store.DequeueAsync(10, CancellationToken.None);

        batch.Count.ShouldBe(2);
        batch[0].Id.ShouldBe(firstItem.Id);
        batch[0].AttemptCount.ShouldBe(1);
    }
}
