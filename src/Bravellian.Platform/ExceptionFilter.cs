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

namespace Bravellian.Platform;
/// <summary>
/// Provides utility methods for filtering exceptions in catch blocks.
/// This helper is designed to prevent catching critical exceptions that should
/// terminate the application, such as <see cref="OutOfMemoryException"/> and
/// <see cref="StackOverflowException"/>.
/// </summary>
/// <remarks>
/// <para>
/// When catching generic exceptions, it's a best practice to avoid catching
/// critical exceptions that indicate fundamental failures in the application's
/// execution environment. These exceptions typically cannot be recovered from
/// and attempting to catch them may hide serious issues.
/// </para>
/// <para>
/// Usage example with exception filter:
/// <code>
/// try
/// {
///     // Your code here
/// }
/// catch (Exception ex) when (ExceptionFilter.IsCatchable(ex))
/// {
///     // Handle recoverable exceptions
///     _logger.LogError(ex, "An error occurred during processing");
/// }
/// </code>
/// </para>
/// <para>
/// For more specific exception handling where you want to catch only certain types:
/// <code>
/// try
/// {
///     // Your code here
/// }
/// catch (Exception ex) when (ExceptionFilter.IsExpected(ex, typeof(InvalidOperationException), typeof(ArgumentException)))
/// {
///     // Handle only expected exception types
///     _logger.LogWarning(ex, "Expected error occurred");
/// }
/// </code>
/// </para>
/// </remarks>
public static class ExceptionFilter
{
    /// <summary>
    /// Determines whether an exception should be caught by generic exception handlers.
    /// Returns <c>false</c> for critical exceptions that should not be caught and
    /// should instead terminate the application.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception is catchable (non-critical); otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method returns <c>false</c> for the following critical exception types:
    /// <list type="bullet">
    /// <item><description><see cref="OutOfMemoryException"/> - Indicates the application has exhausted available memory</description></item>
    /// <item><description><see cref="StackOverflowException"/> - Indicates a stack overflow, typically from infinite recursion</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Note: <see cref="ThreadAbortException"/> is not included in this list as it does not exist
    /// in .NET Core/.NET 5+ runtimes. Thread.Abort() is not supported in these platforms.
    /// </para>
    /// <para>
    /// Use this method in exception filters to ensure critical exceptions propagate:
    /// <code>
    /// catch (Exception ex) when (ExceptionFilter.IsCatchable(ex))
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="exception"/> is null.
    /// </exception>
    public static bool IsCatchable(Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        return !IsCritical(exception);
    }

    /// <summary>
    /// Determines whether an exception is considered critical and should not be caught.
    /// Critical exceptions indicate fundamental failures that cannot be recovered from.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the exception is critical and should not be caught; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The following exception types are considered critical:
    /// <list type="bullet">
    /// <item><description><see cref="OutOfMemoryException"/> - Application has exhausted memory</description></item>
    /// <item><description><see cref="StackOverflowException"/> - Stack overflow from infinite recursion or deep call stacks</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static bool IsCritical(Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        return exception is OutOfMemoryException or StackOverflowException;
    }

    /// <summary>
    /// Determines whether an exception is one of the expected types.
    /// This is useful for catching only specific exception types while letting others propagate.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <param name="expectedTypes">The exception types that are expected and should be caught.</param>
    /// <returns>
    /// <c>true</c> if the exception is one of the expected types; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method checks if the exception is assignable to any of the provided types,
    /// allowing for catching derived exception types as well.
    /// </para>
    /// <para>
    /// Usage example:
    /// <code>
    /// try
    /// {
    ///     await ProcessDataAsync();
    /// }
    /// catch (Exception ex) when (ExceptionFilter.IsExpected(ex, typeof(SqlException), typeof(TimeoutException)))
    /// {
    ///     // Handle database-related errors
    ///     await HandleDatabaseErrorAsync(ex);
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="exception"/> or <paramref name="expectedTypes"/> is null.
    /// </exception>
    public static bool IsExpected(Exception exception, params Type[] expectedTypes)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        if (expectedTypes == null)
        {
            throw new ArgumentNullException(nameof(expectedTypes));
        }

        var exceptionType = exception.GetType();
        return expectedTypes
            .Where(expectedType => expectedType != null)
            .Any(expectedType => expectedType.IsAssignableFrom(exceptionType));
    }

    /// <summary>
    /// Determines whether an exception should be caught, combining both catchability check
    /// (excluding critical exceptions) and expected type check.
    /// </summary>
    /// <param name="exception">The exception to evaluate.</param>
    /// <param name="expectedTypes">The exception types that are expected and should be caught.</param>
    /// <returns>
    /// <c>true</c> if the exception is both catchable (non-critical) and one of the expected types;
    /// otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is equivalent to: <c>IsCatchable(exception) &amp;&amp; IsExpected(exception, expectedTypes)</c>
    /// </para>
    /// <para>
    /// Usage example:
    /// <code>
    /// try
    /// {
    ///     await ProcessAsync();
    /// }
    /// catch (Exception ex) when (ExceptionFilter.IsCatchableAndExpected(ex, typeof(InvalidOperationException)))
    /// {
    ///     // Handle only non-critical InvalidOperationExceptions
    ///     _logger.LogWarning(ex, "Invalid operation");
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="exception"/> or <paramref name="expectedTypes"/> is null.
    /// </exception>
    public static bool IsCatchableAndExpected(Exception exception, params Type[] expectedTypes)
    {
        return IsCatchable(exception) && IsExpected(exception, expectedTypes);
    }
}
