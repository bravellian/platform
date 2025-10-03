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

namespace Bravellian.Platform.Tests.TestUtilities;

using Microsoft.Extensions.Logging;

/// <summary>
/// A test logger implementation that outputs log messages to xUnit test output.
/// This logger can be used for any type T and outputs formatted log messages
/// including exceptions to the test output helper.
/// </summary>
/// <typeparam name="T">The type for which this logger is created.</typeparam>
public class TestLogger<T> : ILogger<T>
{
    private readonly ITestOutputHelper testOutputHelper;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestLogger{T}"/> class.
    /// </summary>
    /// <param name="testOutputHelper">The test output helper to write log messages to.</param>
    public TestLogger(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        this.testOutputHelper.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception != null)
        {
            this.testOutputHelper.WriteLine($"Exception: {exception}");
        }
    }
}
