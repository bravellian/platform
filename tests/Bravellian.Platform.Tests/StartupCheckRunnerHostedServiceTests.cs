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

using Microsoft.Extensions.Logging;
using Shouldly;

#pragma warning disable CA1861
namespace Bravellian.Platform.Tests;

public class StartupCheckRunnerHostedServiceTests
{
    [Fact]
    public async Task StartAsync_ExecutesChecksInOrder()
    {
        var execution = new List<string>();
        var checks = new IStartupCheck[]
        {
            new RecordingCheck("b", 1, execution),
            new RecordingCheck("c", 0, execution),
            new RecordingCheck("a", 0, execution),
        };

        var runner = new StartupCheckRunnerHostedService(
            checks,
            new StartupLatch(),
            new CapturingLogger<StartupCheckRunnerHostedService>());

        await runner.StartAsync(CancellationToken.None);
        await runner.Completion;

        execution.ShouldBe(new[] { "a", "c", "b" });
    }

    [Fact]
    public async Task StartAsync_ThrowsWhenDuplicateNames()
    {
        var execution = new List<string>();
        var checks = new IStartupCheck[]
        {
            new RecordingCheck("dup", 0, execution),
            new RecordingCheck("dup", 1, execution),
        };

        var runner = new StartupCheckRunnerHostedService(
            checks,
            new StartupLatch(),
            new CapturingLogger<StartupCheckRunnerHostedService>());

        await Should.ThrowAsync<InvalidOperationException>(() => runner.StartAsync(CancellationToken.None));

        execution.Count.ShouldBe(0);
    }

    [Fact]
    public async Task StartAsync_CriticalFailureStopsFurtherChecks()
    {
        var execution = new List<string>();
        var checks = new IStartupCheck[]
        {
            new RecordingCheck("first", 0, execution, throws: true, isCritical: true),
            new RecordingCheck("second", 1, execution),
        };

        var runner = new StartupCheckRunnerHostedService(
            checks,
            new StartupLatch(),
            new CapturingLogger<StartupCheckRunnerHostedService>());

        await runner.StartAsync(CancellationToken.None);
        await Should.ThrowAsync<InvalidOperationException>(() => runner.Completion);

        execution.ShouldBe(new[] { "first" });
    }

    [Fact]
    public async Task StartAsync_NonCriticalFailureLogsAndContinues()
    {
        var execution = new List<string>();
        var logger = new CapturingLogger<StartupCheckRunnerHostedService>();
        var checks = new IStartupCheck[]
        {
            new RecordingCheck("first", 0, execution, throws: true, isCritical: false),
            new RecordingCheck("second", 1, execution),
        };

        var runner = new StartupCheckRunnerHostedService(
            checks,
            new StartupLatch(),
            logger);

        await runner.StartAsync(CancellationToken.None);
        await runner.Completion;

        execution.ShouldBe(new[] { "first", "second" });
        logger.Entries.Any(entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("non-critical", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    private sealed class RecordingCheck : IStartupCheck
    {
        private readonly List<string> execution;
        private readonly bool throws;

        public RecordingCheck(string name, int order, List<string> execution, bool throws = false, bool isCritical = true)
        {
            Name = name;
            Order = order;
            this.execution = execution;
            this.throws = throws;
            IsCritical = isCritical;
        }

        public string Name { get; }

        public int Order { get; }

        public bool IsCritical { get; }

        public Task ExecuteAsync(CancellationToken ct)
        {
            execution.Add(Name);

            if (throws)
            {
                throw new InvalidOperationException($"check {Name} failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
#pragma warning restore CA1861
