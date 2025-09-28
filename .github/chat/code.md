Here is a set of instructions for generating C# code that adheres to our team's conventions.

#### **General Principles**

*   **Language and Framework:** All code should be written in the latest stable version of C# for the .NET platform.
*   **Clarity and Simplicity:** Prioritize writing code that is clear, simple, and easy to understand. Avoid overly complex or "clever" solutions when a more straightforward one exists.
*   **Modern C# Features:** Utilize modern C# features and syntax where appropriate, such as file-scoped namespaces, `using` declarations, and pattern matching.

#### **Naming and Style Conventions**

*   **No Underscores:** Do not use underscores (`_`) in method names, variable names, or class names. The only exception is for private backing fields, which should be prefixed with an underscore (e.g., `_fieldName`).
*   **Casing:** Follow standard C# casing conventions:
    *   **PascalCase** for class names, method names, property names, and record names.
    *   **camelCase** for local variables and method parameters.
*   **Implicit vs. Explicit Typing:** Use `var` for local variable declarations when the type is obvious from the right-hand side of the assignment. Otherwise, use the explicit type name for clarity.
*   **File Organization:** Each class, interface, struct, or record should be in its own file. The filename should match the type name (e.g., `MyClass.cs`).

#### **Commenting and Documentation**

*   **Code Comments:**
    *   Add comments to explain the "why" behind a piece of code, not the "what." Focus on complex logic, business rules, or non-obvious workarounds.
    *   Avoid excessive or redundant comments that simply restate what the code does. A clean, well-named method often requires no comments.
    *   Use `//` for single-line comments.

*   **XML Documentation Comments:**
    *   Generate XML documentation (`///`) for all public and protected members (classes, methods, properties).
    *   The `<summary>` tag should provide a clear and concise description of the member's purpose.
    *   Use `<param>` tags to describe each parameter for a method.
    *   Use `<returns>` tags to describe the method's return value.
    *   Keep documentation concise and to the point. Avoid overly verbose explanations.

**Example of Good Documentation:**
```csharp
/// <summary>
/// Calculates the total price for a given order.
/// </summary>
/// <param name="orderId">The unique identifier of the order.</param>
/// <returns>The calculated total price of the order.</returns>
public decimal CalculateTotalPrice(int orderId)
{
    // Implementation details...
}
```
