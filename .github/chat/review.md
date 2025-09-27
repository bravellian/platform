When reviewing the selected C# code, please analyze it based on the following priorities, providing feedback in a clear and constructive manner.

#### **Priority 1: Correctness and Functionality**

This is the most critical aspect. Before all else, the code must be correct.

*   **Logical Errors:** Scrutinize the code for any logical flaws, off-by-one errors, incorrect assumptions, or potential bugs.
*   **Runtime Safety:** Identify potential runtime errors. Pay close attention to possible `NullReferenceException`s, race conditions in concurrent code, and improper resource management (e.g., un-disposed `IDisposable` objects).
*   **Compilation:** While the IDE often catches this, double-check that the code is syntactically correct and should compile without errors.

#### **Priority 2: Purpose and Intent**

Once correctness is established, verify that the code is doing the *right* thing.

*   **Fulfillment of Purpose:** Based on the method/class names and surrounding code, assess whether the selected code appears to achieve its intended goal. Does it correctly implement the feature or solve the problem it is meant to address?
*   **Edge Cases:** Consider if the code handles common edge cases, invalid inputs, or empty collections gracefully.

#### **Priority 3: C# Best Practices and Style**

If the code is correct and fulfills its purpose, focus on its quality, readability, and adherence to our conventions.

*   **Naming Conventions:**
    *   Ensure all identifiers (class, method, property, variable names) are descriptive and clear.
    *   Verify adherence to standard C# casing: **PascalCase** for public members and types, and **camelCase** for local variables and parameters.
    *   Confirm that no underscores (`_`) are used, except for private backing fields (e.g., `_fieldName`).

*   **Documentation:**
    *   Check for the presence of XML documentation comments (`///`) on all public and protected members.
    *   The `<summary>` should clearly explain the member's purpose.
    *   Ensure methods have `<param>` and `<returns>` tags where appropriate.

*   **Immutability and Data Types:**
    *   Review how data is being modeled. Suggest the use of immutable types like `record` or `readonly struct` where appropriate to prevent unintended side effects and make state easier to reason about.

*   **Modern C# Usage:**
    *   Identify opportunities to simplify the code using modern C# features, such as LINQ, pattern matching, expression-bodied members, or file-scoped namespaces, but only where it improves clarity.

*   **Simplicity and Readability:**
    *   Flag any code that is overly complex or difficult to understand. Suggest simpler, more straightforward alternatives if they exist.
