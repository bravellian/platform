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

namespace Bravellian.Platform.Tests;

public class ExceptionFilterTests
{
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

    [Fact]
    public void IsCatchable_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsCatchable(null!))
            .ParamName.ShouldBe("exception");
    }

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

    [Fact]
    public void IsCritical_WithRegularException_ReturnsFalse()
    {
        // Arrange
        var exception = new ArgumentException("Invalid argument");

        // Act
        var result = ExceptionFilter.IsCritical(exception);

        // Assert
        result.ShouldBeFalse("regular exceptions are not critical");
    }

    [Fact]
    public void IsCritical_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsCritical(null!))
            .ParamName.ShouldBe("exception");
    }

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

    [Fact]
    public void IsExpected_WithMultipleTypesAndMatch_ReturnsTrue()
    {
        // Arrange
        var exception = new ArgumentNullException("param");

        // Act
        var result = ExceptionFilter.IsExpected(
            exception,
            typeof(InvalidOperationException),
            typeof(ArgumentException),
            typeof(FormatException));

        // Assert
        result.ShouldBeTrue("ArgumentNullException derives from ArgumentException");
    }

    [Fact]
    public void IsExpected_WithDerivedType_ReturnsTrue()
    {
        // Arrange
        var exception = new ArgumentNullException("param"); // Derives from ArgumentException

        // Act
        var result = ExceptionFilter.IsExpected(exception, typeof(ArgumentException));

        // Assert
        result.ShouldBeTrue("derived exception types should match base types");
    }

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

    [Fact]
    public void IsExpected_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsExpected(null!, typeof(Exception)))
            .ParamName.ShouldBe("exception");
    }

    [Fact]
    public void IsExpected_WithNullExpectedTypes_ThrowsArgumentNullException()
    {
        // Arrange
        var exception = new Exception("Test");

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsExpected(exception, null!))
            .ParamName.ShouldBe("expectedTypes");
    }

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

    [Fact]
    public void IsExpected_WithNullTypeInArray_IgnoresNullAndContinues()
    {
        // Arrange
        var exception = new ArgumentException("Test");

        // Act
        var result = ExceptionFilter.IsExpected(
            exception,
            null!,
            typeof(ArgumentException),
            null!);

        // Assert
        result.ShouldBeTrue("should skip null types and find matching type");
    }

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

    [Fact]
    public void IsCatchableAndExpected_WithNullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => ExceptionFilter.IsCatchableAndExpected(null!, typeof(Exception)));
    }

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
            exceptionsCaught.Add("Caught: " + ex.Message);
        }

        exceptionsCaught.Count.ShouldBe(1);
        exceptionsCaught[0].ShouldBe("Caught: Catchable");
    }

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

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(TimeoutException))]
    public void IsCatchable_WithVariousCommonExceptions_ReturnsTrue(Type exceptionType)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test message")!;

        // Act
        var result = ExceptionFilter.IsCatchable(exception);

        // Assert
        result.ShouldBeTrue($"{exceptionType.Name} should be catchable");
    }

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
}
