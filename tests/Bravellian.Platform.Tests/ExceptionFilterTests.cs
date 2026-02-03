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

#pragma warning disable CA2201
namespace Bravellian.Platform.Tests;

public class ExceptionFilterTests
{
    /// <summary>
    /// When IsCatchable is given a regular exception, then it returns true.
    /// </summary>
    /// <intent>
    /// Verify non-critical exceptions are considered catchable.
    /// </intent>
    /// <scenario>
    /// Given an InvalidOperationException instance.
    /// </scenario>
    /// <behavior>
    /// Then IsCatchable returns true.
    /// </behavior>
    [Fact]
    public void IsCatchable_WithRegularException_ReturnsTrue()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");

        // Act
        var result = ExceptionFilter.IsCatchable(exception);

        // Assert
        result.ShouldBeTrue("regular exceptions should be catchable");
    }

    /// <summary>
    /// When IsCatchable is given an OutOfMemoryException, then it returns false.
    /// </summary>
    /// <intent>
    /// Ensure critical exceptions are not marked catchable.
    /// </intent>
    /// <scenario>
    /// Given an OutOfMemoryException instance.
    /// </scenario>
    /// <behavior>
    /// Then IsCatchable returns false.
    /// </behavior>
    [Fact]
    public void IsCatchable_WithOutOfMemoryException_ReturnsFalse()
    {
        // Arrange
        var exception = new OutOfMemoryException("Out of memory");

        // Act
        var result = ExceptionFilter.IsCatchable(exception);

        // Assert
        result.ShouldBeFalse("OutOfMemoryException is critical and should not be caught");
    }

    /// <summary>
    /// When IsCatchable is given a StackOverflowException, then it returns false.
    /// </summary>
    /// <intent>
    /// Ensure stack overflow is treated as non-catchable.
    /// </intent>
    /// <scenario>
    /// Given a StackOverflowException instance.
    /// </scenario>
    /// <behavior>
    /// Then IsCatchable returns false.
    /// </behavior>
    [Fact]
    public void IsCatchable_WithStackOverflowException_ReturnsFalse()
    {
        // Arrange
        var exception = new StackOverflowException("Stack overflow");

        // Act
        var result = ExceptionFilter.IsCatchable(exception);

        // Assert
        result.ShouldBeFalse("StackOverflowException is critical and should not be caught");
    }

    /// <summary>
    /// When IsCatchable is called with a null exception, then it throws ArgumentNullException.
    /// </summary>
    /// <intent>
    /// Guard against null exception inputs.
    /// </intent>
    /// <scenario>
    /// Given a null exception argument.
    /// </scenario>
    /// <behavior>
    /// Then an ArgumentNullException is thrown with parameter name "exception".
    /// </behavior>
    [Fact]
    public void IsCatchable_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsCatchable(null!))
            .ParamName.ShouldBe("exception");
    }

    /// <summary>
    /// When IsCritical is given an OutOfMemoryException, then it returns true.
    /// </summary>
    /// <intent>
    /// Classify out-of-memory conditions as critical.
    /// </intent>
    /// <scenario>
    /// Given an OutOfMemoryException instance.
    /// </scenario>
    /// <behavior>
    /// Then IsCritical returns true.
    /// </behavior>
    [Fact]
    public void IsCritical_WithOutOfMemoryException_ReturnsTrue()
    {
        // Arrange
        var exception = new OutOfMemoryException();

        // Act
        var result = ExceptionFilter.IsCritical(exception);

        // Assert
        result.ShouldBeTrue("OutOfMemoryException is a critical exception");
    }

    /// <summary>
    /// When IsCritical is given a StackOverflowException, then it returns true.
    /// </summary>
    /// <intent>
    /// Classify stack overflow conditions as critical.
    /// </intent>
    /// <scenario>
    /// Given a StackOverflowException instance.
    /// </scenario>
    /// <behavior>
    /// Then IsCritical returns true.
    /// </behavior>
    [Fact]
    public void IsCritical_WithStackOverflowException_ReturnsTrue()
    {
        // Arrange
        var exception = new StackOverflowException();

        // Act
        var result = ExceptionFilter.IsCritical(exception);

        // Assert
        result.ShouldBeTrue("StackOverflowException is a critical exception");
    }

    /// <summary>
    /// When IsCritical is given a regular exception, then it returns false.
    /// </summary>
    /// <intent>
    /// Ensure non-critical exceptions are not flagged as critical.
    /// </intent>
    /// <scenario>
    /// Given an ArgumentException instance.
    /// </scenario>
    /// <behavior>
    /// Then IsCritical returns false.
    /// </behavior>
    [Fact]
    public void IsCritical_WithRegularException_ReturnsFalse()
    {
        // Arrange
        var exception = CreateArgumentException("argument", "Invalid argument");

        // Act
        var result = ExceptionFilter.IsCritical(exception);

        // Assert
        result.ShouldBeFalse("regular exceptions are not critical");
    }

    /// <summary>
    /// When IsCritical is called with a null exception, then it throws ArgumentNullException.
    /// </summary>
    /// <intent>
    /// Guard against null exception inputs for critical checks.
    /// </intent>
    /// <scenario>
    /// Given a null exception argument.
    /// </scenario>
    /// <behavior>
    /// Then an ArgumentNullException is thrown with parameter name "exception".
    /// </behavior>
    [Fact]
    public void IsCritical_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsCritical(null!))
            .ParamName.ShouldBe("exception");
    }

    /// <summary>
    /// When IsExpected is given a matching exception type, then it returns true.
    /// </summary>
    /// <intent>
    /// Confirm exact type matches are treated as expected.
    /// </intent>
    /// <scenario>
    /// Given an InvalidOperationException and an expected type of InvalidOperationException.
    /// </scenario>
    /// <behavior>
    /// Then IsExpected returns true.
    /// </behavior>
    [Fact]
    public void IsExpected_WithMatchingType_ReturnsTrue()
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act
        var result = ExceptionFilter.IsExpected(exception, typeof(InvalidOperationException));

        // Assert
        result.ShouldBeTrue("exception type matches expected type");
    }

    /// <summary>
    /// When IsExpected is given multiple expected types including a base type, then it returns true for a derived exception.
    /// </summary>
    /// <intent>
    /// Validate derived exceptions match any compatible expected type.
    /// </intent>
    /// <scenario>
    /// Given an ArgumentNullException and expected types that include ArgumentException.
    /// </scenario>
    /// <behavior>
    /// Then IsExpected returns true.
    /// </behavior>
    [Fact]
    public void IsExpected_WithMultipleTypesAndMatch_ReturnsTrue()
    {
        // Arrange
        var exception = CreateArgumentNullException("param");

        // Act
        var result = ExceptionFilter.IsExpected(
            exception,
            typeof(InvalidOperationException),
            typeof(ArgumentException),
            typeof(FormatException));

        // Assert
        result.ShouldBeTrue("ArgumentNullException derives from ArgumentException");
    }

    /// <summary>
    /// When IsExpected is given a derived exception and a base expected type, then it returns true.
    /// </summary>
    /// <intent>
    /// Confirm base expected types match derived exceptions.
    /// </intent>
    /// <scenario>
    /// Given an ArgumentNullException and expected type ArgumentException.
    /// </scenario>
    /// <behavior>
    /// Then IsExpected returns true.
    /// </behavior>
    [Fact]
    public void IsExpected_WithDerivedType_ReturnsTrue()
    {
        // Arrange
        var exception = CreateArgumentNullException("param"); // Derives from ArgumentException

        // Act
        var result = ExceptionFilter.IsExpected(exception, typeof(ArgumentException));

        // Assert
        result.ShouldBeTrue("derived exception types should match base types");
    }

    /// <summary>
    /// When IsExpected is given a non-matching expected type, then it returns false.
    /// </summary>
    /// <intent>
    /// Ensure unrelated exception types are not marked expected.
    /// </intent>
    /// <scenario>
    /// Given an InvalidOperationException and expected type ArgumentException.
    /// </scenario>
    /// <behavior>
    /// Then IsExpected returns false.
    /// </behavior>
    [Fact]
    public void IsExpected_WithNonMatchingType_ReturnsFalse()
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act
        var result = ExceptionFilter.IsExpected(exception, typeof(ArgumentException));

        // Assert
        result.ShouldBeFalse("exception type does not match expected type");
    }

    /// <summary>
    /// When IsExpected is called with a null exception, then it throws ArgumentNullException.
    /// </summary>
    /// <intent>
    /// Guard against null exception inputs for expected checks.
    /// </intent>
    /// <scenario>
    /// Given a null exception argument.
    /// </scenario>
    /// <behavior>
    /// Then an ArgumentNullException is thrown with parameter name "exception".
    /// </behavior>
    [Fact]
    public void IsExpected_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsExpected(null!, typeof(Exception)))
            .ParamName.ShouldBe("exception");
    }

    /// <summary>
    /// When IsExpected is called with null expected types, then it throws ArgumentNullException.
    /// </summary>
    /// <intent>
    /// Guard against null expected type arrays.
    /// </intent>
    /// <scenario>
    /// Given a non-null exception and a null expectedTypes array.
    /// </scenario>
    /// <behavior>
    /// Then an ArgumentNullException is thrown with parameter name "expectedTypes".
    /// </behavior>
    [Fact]
    public void IsExpected_WithNullExpectedTypes_ThrowsArgumentNullException()
    {
        // Arrange
        var exception = new Exception("Test");

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsExpected(exception, null!))
            .ParamName.ShouldBe("expectedTypes");
    }

    /// <summary>
    /// When IsExpected is called with no expected types, then it returns false.
    /// </summary>
    /// <intent>
    /// Ensure empty expected type lists do not match any exception.
    /// </intent>
    /// <scenario>
    /// Given an InvalidOperationException and an empty expected type list.
    /// </scenario>
    /// <behavior>
    /// Then IsExpected returns false.
    /// </behavior>
    [Fact]
    public void IsExpected_WithEmptyExpectedTypes_ReturnsFalse()
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act
        var result = ExceptionFilter.IsExpected(exception);

        // Assert
        result.ShouldBeFalse("no expected types were provided");
    }

    /// <summary>
    /// When IsExpected encounters null entries in expected types, then it ignores them and still matches valid types.
    /// </summary>
    /// <intent>
    /// Verify null expected types do not break matching.
    /// </intent>
    /// <scenario>
    /// Given an ArgumentException and expected types containing nulls and ArgumentException.
    /// </scenario>
    /// <behavior>
    /// Then IsExpected returns true.
    /// </behavior>
    [Fact]
    public void IsExpected_WithNullTypeInArray_IgnoresNullAndContinues()
    {
        // Arrange
        var exception = CreateArgumentException("argument", "Test");

        // Act
        var result = ExceptionFilter.IsExpected(
            exception,
            null!,
            typeof(ArgumentException),
            null!);

        // Assert
        result.ShouldBeTrue("should skip null types and find matching type");
    }

    /// <summary>
    /// When an exception is both catchable and expected, then IsCatchableAndExpected returns true.
    /// </summary>
    /// <intent>
    /// Validate combined catchable and expected filtering logic.
    /// </intent>
    /// <scenario>
    /// Given an InvalidOperationException and expected type InvalidOperationException.
    /// </scenario>
    /// <behavior>
    /// Then IsCatchableAndExpected returns true.
    /// </behavior>
    [Fact]
    public void IsCatchableAndExpected_WithCatchableAndExpected_ReturnsTrue()
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act
        var result = ExceptionFilter.IsCatchableAndExpected(
            exception,
            typeof(InvalidOperationException));

        // Assert
        result.ShouldBeTrue("exception is both catchable and expected");
    }

    /// <summary>
    /// When an exception is critical even if expected, then IsCatchableAndExpected returns false.
    /// </summary>
    /// <intent>
    /// Ensure critical exceptions are never treated as catchable.
    /// </intent>
    /// <scenario>
    /// Given an OutOfMemoryException and expected type OutOfMemoryException.
    /// </scenario>
    /// <behavior>
    /// Then IsCatchableAndExpected returns false.
    /// </behavior>
    [Fact]
    public void IsCatchableAndExpected_WithCriticalButExpected_ReturnsFalse()
    {
        // Arrange
        var exception = new OutOfMemoryException();

        // Act
        var result = ExceptionFilter.IsCatchableAndExpected(
            exception,
            typeof(OutOfMemoryException));

        // Assert
        result.ShouldBeFalse("exception is critical even though it's expected");
    }

    /// <summary>
    /// When an exception is catchable but not expected, then IsCatchableAndExpected returns false.
    /// </summary>
    /// <intent>
    /// Ensure expected-type filtering is enforced even for catchable exceptions.
    /// </intent>
    /// <scenario>
    /// Given an InvalidOperationException and an expected type of ArgumentException.
    /// </scenario>
    /// <behavior>
    /// Then IsCatchableAndExpected returns false.
    /// </behavior>
    [Fact]
    public void IsCatchableAndExpected_WithCatchableButNotExpected_ReturnsFalse()
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act
        var result = ExceptionFilter.IsCatchableAndExpected(
            exception,
            typeof(ArgumentException));

        // Assert
        result.ShouldBeFalse("exception is catchable but not in expected types");
    }

    /// <summary>
    /// When IsCatchableAndExpected is called with a null exception, then it throws ArgumentNullException.
    /// </summary>
    /// <intent>
    /// Guard against null exception inputs for combined filtering.
    /// </intent>
    /// <scenario>
    /// Given a null exception argument.
    /// </scenario>
    /// <behavior>
    /// Then an ArgumentNullException is thrown.
    /// </behavior>
    [Fact]
    public void IsCatchableAndExpected_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsCatchableAndExpected(null!, typeof(Exception)));
    }

    /// <summary>
    /// When IsCatchable is used in a real catch filter, then it catches the regular exception.
    /// </summary>
    /// <intent>
    /// Demonstrate catch-filter usage with the exception filter.
    /// </intent>
    /// <scenario>
    /// Given a thrown InvalidOperationException inside a try/catch with ExceptionFilter.IsCatchable.
    /// </scenario>
    /// <behavior>
    /// Then the exception is caught and recorded in the list.
    /// </behavior>
    [Fact]
    public void ExceptionFilter_InRealCatchBlock_WorksCorrectly()
    {
        // Arrange
        var exceptionsCaught = new System.Collections.Generic.List<string>();

        // Act & Assert - Test with catchable exception
        try
        {
            throw new InvalidOperationException("Catchable");
        }
        catch (Exception ex) when (ExceptionFilter.IsCatchable(ex))
        {
            exceptionsCaught.Add("Caught: " + ex.ToString());
        }

        exceptionsCaught.Count.ShouldBe(1);
        exceptionsCaught[0].ShouldContain("Caught:");
        exceptionsCaught[0].ShouldContain("Catchable");
    }

    /// <summary>
    /// When a critical exception is thrown, then the catch filter does not intercept it.
    /// </summary>
    /// <intent>
    /// Ensure critical exceptions bypass catchable filters.
    /// </intent>
    /// <scenario>
    /// Given an OutOfMemoryException thrown inside a try/catch with ExceptionFilter.IsCatchable.
    /// </scenario>
    /// <behavior>
    /// Then the filter does not catch the exception and the flag remains false.
    /// </behavior>
    [Fact]
    public void ExceptionFilter_WithCriticalException_DoesNotCatch()
    {
        // Arrange
        var wasCaught = false;

        // Act & Assert - Critical exception should propagate
        try
        {
            try
            {
#pragma warning disable MA0012 // Do not raise reserved exception type
                throw new OutOfMemoryException("Critical");
#pragma warning restore MA0012 // Do not raise reserved exception type
            }
            catch (Exception ex) when (ExceptionFilter.IsCatchable(ex))
            {
                wasCaught = true;
            }
        }
        catch (OutOfMemoryException)
        {
            // Expected - exception propagated correctly
        }

        wasCaught.ShouldBeFalse("critical exception should not be caught by the filter");
    }

    /// <summary>
    /// When IsCatchable is evaluated for common exception types, then it returns true.
    /// </summary>
    /// <intent>
    /// Validate catchable classification for typical application exceptions.
    /// </intent>
    /// <scenario>
    /// Given an exception instance created from each common exception type in the data set.
    /// </scenario>
    /// <behavior>
    /// Then IsCatchable returns true for each provided type.
    /// </behavior>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(TimeoutException))]
    public void IsCatchable_WithVariousCommonExceptions_ReturnsTrue(Type exceptionType)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(exceptionType);
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test message")!;

        // Act
        var result = ExceptionFilter.IsCatchable(exception);

        // Assert
        result.ShouldBeTrue($"{exceptionType.Name} should be catchable");
    }

    /// <summary>
    /// When IsExpected is given an OperationCanceledException and matching expected type, then it returns true.
    /// </summary>
    /// <intent>
    /// Confirm cancellation exceptions are matched when expected.
    /// </intent>
    /// <scenario>
    /// Given an OperationCanceledException and expected type OperationCanceledException.
    /// </scenario>
    /// <behavior>
    /// Then IsExpected returns true.
    /// </behavior>
    [Fact]
    public void IsExpected_WithOperationCanceledException_WorksCorrectly()
    {
        // Arrange
        var exception = new OperationCanceledException("Operation was canceled");

        // Act
        var result = ExceptionFilter.IsExpected(exception, typeof(OperationCanceledException));

        // Assert
        result.ShouldBeTrue("OperationCanceledException should match expected type");
    }

    /// <summary>
    /// When evaluating a catchable SQL-like exception versus a critical one, then only the catchable exception returns true.
    /// </summary>
    /// <intent>
    /// Demonstrate real-world filtering that excludes critical exceptions.
    /// </intent>
    /// <scenario>
    /// Given an InvalidOperationException representing a SQL error and an OutOfMemoryException representing a critical error.
    /// </scenario>
    /// <behavior>
    /// Then IsCatchableAndExpected returns true for the SQL-like exception and false for the critical exception.
    /// </behavior>
    [Fact]
    public void IsCatchableAndExpected_RealWorldScenario_SqlExceptions()
    {
        // This test demonstrates a real-world pattern where you want to catch
        // only specific database-related exceptions while avoiding critical ones

        // Arrange
        var sqlException = new InvalidOperationException("Database timeout"); // Simulating SQL exception
        var criticalException = new OutOfMemoryException();

        // Act
        var shouldCatchSql = ExceptionFilter.IsCatchableAndExpected(
            sqlException,
            typeof(InvalidOperationException));

        var shouldCatchCritical = ExceptionFilter.IsCatchableAndExpected(
            criticalException,
            typeof(OutOfMemoryException));

        // Assert
        shouldCatchSql.ShouldBeTrue("SQL exceptions should be caught");
        shouldCatchCritical.ShouldBeFalse("critical exceptions should never be caught");
    }

    private static ArgumentException CreateArgumentException(string parameterName, string message)
    {
        return new ArgumentException(message, parameterName);
    }

    private static ArgumentNullException CreateArgumentNullException(string parameterName)
    {
        return new ArgumentNullException(parameterName);
    }
}
#pragma warning restore CA2201

