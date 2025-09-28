Here is a set of instructions for generating C# tests using xUnit.

#### **General Principles**

*   **Framework:** All tests will be written using the xUnit testing framework.
*   **Assertions:** Use the standard assertion library provided by xUnit (`Xunit.Assert`). Do not use any third-party assertion libraries like FluentAssertions.
*   **Test Types:** Tests should be clearly identified as either "Unit" or "Integration" tests. Unit tests must be self-contained and not require any external resources to run.

#### **Test Naming Convention**

*   Test method names should be descriptive and clearly state the scenario being tested and the expected outcome.
*   Use a dot-separated naming convention for test methods (e.g., `MethodName.Scenario.ExpectedBehavior`).
*   Do not use underscores in test method names.
*   Test class names should be the name of the class under test, suffixed with "Tests". A common convention is to name the test project after the project it is testing, with a `.Tests` suffix.

#### **Test Structure**

*   Tests must follow the **Arrange, Act, Assert** pattern.
*   Use comments (`// Arrange`, `// Act`, `// Assert`) to clearly separate the three parts of the test.

#### **Test Categorization**

To distinguish between unit and integration tests, use the `[Trait]` attribute from xUnit.

*   For unit tests, which are self-contained and can run at any time, add the following attribute to the test method or class:
    ```csharp
    [Trait("Category", "Unit")]
    ```
*   For integration tests that rely on external resources (e.g., databases, file systems, network services), use the following attribute:
    ```csharp
    [Trait("Category", "Integration")]
    ```
