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
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

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