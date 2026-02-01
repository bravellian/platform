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
    /// <summary>When is Ready Is True Initially, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for is Ready Is True Initially.</intent>
    /// <scenario>Given is Ready Is True Initially.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void IsReady_IsTrueInitially()
    {
        var latch = new StartupLatch();

        latch.IsReady.ShouldBeTrue();
    }

    /// <summary>When is Ready Becomes False After Register, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for is Ready Becomes False After Register.</intent>
    /// <scenario>Given is Ready Becomes False After Register.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void IsReady_BecomesFalseAfterRegister()
    {
        var latch = new StartupLatch();

        using var _ = latch.Register("bootstrap");

        latch.IsReady.ShouldBeFalse();
    }

    /// <summary>When is Ready Becomes True After Dispose, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for is Ready Becomes True After Dispose.</intent>
    /// <scenario>Given is Ready Becomes True After Dispose.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void IsReady_BecomesTrueAfterDispose()
    {
        var latch = new StartupLatch();

        var step = latch.Register("bootstrap");
        step.Dispose();

        latch.IsReady.ShouldBeTrue();
    }

    /// <summary>When multiple Steps Are Required Before Ready, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for multiple Steps Are Required Before Ready.</intent>
    /// <scenario>Given multiple Steps Are Required Before Ready.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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

    /// <summary>When disposing Twice Is Safe, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for disposing Twice Is Safe.</intent>
    /// <scenario>Given disposing Twice Is Safe.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
    [Fact]
    public void DisposingTwice_IsSafe()
    {
        var latch = new StartupLatch();

        var step = latch.Register("bootstrap");
        step.Dispose();
        step.Dispose();

        latch.IsReady.ShouldBeTrue();
    }

    /// <summary>When concurrency Sanity Check, then it behaves as expected.</summary>
    /// <intent>Document expected behavior for concurrency Sanity Check.</intent>
    /// <scenario>Given concurrency Sanity Check.</scenario>
    /// <behavior>Then the operation matches the expected outcome.</behavior>
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
