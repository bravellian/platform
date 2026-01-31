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

using System.Threading.Tasks;

namespace Bravellian.Platform.Tests;

public class StartupLatchTests
{
    [Fact]
    public void IsReady_IsTrueInitially()
    {
        var latch = new StartupLatch();

        latch.IsReady.ShouldBeTrue();
    }

    [Fact]
    public void IsReady_BecomesFalseAfterRegister()
    {
        var latch = new StartupLatch();

        using var _ = latch.Register("bootstrap");

        latch.IsReady.ShouldBeFalse();
    }

    [Fact]
    public void IsReady_BecomesTrueAfterDispose()
    {
        var latch = new StartupLatch();

        var step = latch.Register("bootstrap");
        step.Dispose();

        latch.IsReady.ShouldBeTrue();
    }

    [Fact]
    public void MultipleSteps_AreRequiredBeforeReady()
    {
        var latch = new StartupLatch();

        var step1 = latch.Register("database");
        var step2 = latch.Register("workers");

        latch.IsReady.ShouldBeFalse();

        step1.Dispose();
        latch.IsReady.ShouldBeFalse();

        step2.Dispose();
        latch.IsReady.ShouldBeTrue();
    }

    [Fact]
    public void DisposingTwice_IsSafe()
    {
        var latch = new StartupLatch();

        var step = latch.Register("bootstrap");
        step.Dispose();
        step.Dispose();

        latch.IsReady.ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrency_SanityCheck()
    {
        var latch = new StartupLatch();
        const int workerCount = 64;

        var tasks = new Task[workerCount];
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var i = 0; i < workerCount; i++)
        {
            var stepName = $"step-{i}";
            tasks[i] = Task.Run(() =>
            {
                using var _ = latch.Register(stepName);
            }, cancellationToken);
        }

        await Task.WhenAll(tasks);

        latch.IsReady.ShouldBeTrue();
    }
}
