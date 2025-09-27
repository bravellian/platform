---
applyTo: '**'
---

### **Comprehensive Instruction File for GitHub Copilot: C# SQL Code Generator**

#### **Part 1: Vision and Architectural Principles**

**1.1. The Guiding Philosophy: "Convention over Configuration"**

The primary goal is to create a tool that is immediately useful with zero configuration but allows for complete control when needed. The generator must be able to parse a directory of SQL DDL files and produce fully functional C# entity and data access classes based on sensible conventions. The `generator.config.json` file is to be treated as an **override and enhancement layer**, not a required definition file. We are explicitly avoiding the pitfalls of a previous system where C# code generation was tightly coupled to physical database structures like indexes.

**1.2. The Core Architecture: A Multi-Phase Pipeline**

The generator's internal logic must be structured as a distinct, sequential, and testable pipeline. This separation of concerns is critical for maintainability.

1.  **Phase 1: Ingestion & Raw Model Creation:**
    *   **Action:** Parse all `.sql` files.
    *   **Output:** An in-memory object graph representing the "raw" database schema. This includes tables, views, columns with their inferred SQL types and nullability, primary key constraints, and all indexes.

2.  **Phase 2: Schema Model Refinement:**
    *   **Action:** Load `generator.config.json`. Scan for `columnOverrides` that specify `sqlType` or `isNullable`.
    *   **Purpose:** This phase specifically exists to **solve the "SQL View Problem."** Where the parser cannot determine a view column's type or nullability, these config values are used to "patch" and complete the in-memory schema model.
    *   **Output:** A complete and accurate in-memory representation of the database schema, ready for transformation.

3.  **Phase 3: C# Model Transformation:**
    *   **Action:** Determine the final C# type for every single column. This is where the complex type mapping logic lives.
    *   **Process:** Apply C# type rules in a strict order of precedence (detailed in Part 3).
    *   **Output:** A final, "C#-ready" model containing all information needed for code generation (final class names, property names, C# types, method definitions, etc.).

4.  **Phase 4: Code Generation:**
    *   **Action:** Iterate through the final C#-ready model.
    *   **Logic:** This phase should be as "dumb" as possible. It simply translates the rich model from Phase 3 into C# code strings and writes them to `.cs` files. All complex decisions have already been made.

#### **Part 2: Baseline Functionality (Convention-Based Generation)**

This section defines the generator's behavior when **no `generator.config.json` file is present.**

*   **Entity Class Generation:**
    *   A table `dbo.Products` generates a class named `Products`.
    *   Each column becomes a public property (e.g., `ProductName` -> `public string ProductName { get; set; }`).
    *   SQL types are mapped to default C# types (`int` -> `int`, `nvarchar` -> `string`, `bit` -> `bool`, `datetime2` -> `DateTime`).
    *   Nullability is respected (`int NULL` -> `int?`, `varchar(50) NOT NULL` -> `string`).

*   **Data Access Method Generation:**
    *   **Read (by PK):** A method to fetch a single record is created using the columns from the `PRIMARY KEY` constraint as parameters.
    *   **Read (by Index):** For every other `INDEX` on the table, a method to fetch a list of records is created using the indexed columns as parameters. This ensures all indexed lookups are supported by default.
    *   **Update:** An `Update` method is created. Its `SET` clause includes all columns **except** the members of the primary key.
    *   **Create/Delete:** Standard `Create` and `Delete` methods are generated, with the `Delete` method using the primary key for record identification.

#### **Part 3: Advanced Functionality (Configuration-Driven Overrides)**

This section details the features enabled by the `generator.config.json` file, following the schema we have defined.

**3.1. Global Type Mappings (`globalTypeMappings`)**

*   **Purpose:** To define broad, reusable rules for mapping columns to custom or specific C# types. This prevents repeating the same logic for many tables.
*   **Implementation:**
    *   Each entry is an object with `match` and `apply` properties.
    *   `match`: Defines conditions. It can contain `columnNameRegex`, `tableNameRegex`, `schemaNameRegex`, and `sqlType`. *All* conditions must be met for a rule to match.
    *   `apply`: Defines the outcome, primarily setting the `csharpType`.
    *   **`priority` - The Rule for Conflict Resolution:** This is critical. If a column matches multiple global rules, the rule with the **highest integer `priority` wins**. This allows for creating broad, low-priority default rules (e.g., "any column ending in 'Amount' is a `Money` type") and then overriding them with specific, high-priority exception rules (e.g., "any column of SQL type `int` is a C# `int`, even if its name ends in 'Amount'").

**3.2. Table-Specific Overrides (`tables`)**

*   **Purpose:** To provide fine-grained control for a specific table or view. The key is the schema-qualified name (e.g., `"dbo.PurchaseOrder"`).
*   **Features:**
    *   **`csharpClassName`**: Overrides the generated C# class name (e.g., `dbo.PurchaseOrder` -> `Order`).
    *   **`primaryKeyOverride`**: An array of column names to be treated as the primary key. **This decouples the generator's logic from the physical DDL.** It directly dictates the parameters for the `Read(key)`, `Update`, and `Delete` methods.
    *   **`updateConfig.ignoreColumns`**: An array of column names to exclude from the `UPDATE` statement's `SET` clause. This is for capturing business logic, such as ensuring columns like `CreatedOn` or `TenantId` are immutable.
    *   **`readMethods`**: An array for defining custom data access methods. This is a key feature for **separating the application's API from the database's physical structure.** A developer can define a `GetByStatusAndDate` method here without needing a corresponding index to exist in the database.

**3.3. Column-Specific Overrides (`columnOverrides`)**

*   **Purpose:** To target a single column within a specific table. This is the highest level of precedence and serves two distinct purposes based on the generator phase.
*   **Features for Phase 2 (Schema Refinement):**
    *   **`sqlType`**: Provides the SQL data type (e.g., `"nvarchar(100)"`) when the parser cannot infer it, primarily for view columns.
    *   **`isNullable`**: A boolean to explicitly set nullability when it cannot be inferred from a view.
*   **Feature for Phase 3 (C# Model Transformation):**
    *   **`csharpType`**: Defines the final C# type for the property. This is the **ultimate override**. If this value is present, it is used unconditionally, ignoring any global mapping rules or default conventions for that specific column.

#### **Part 4: The Role of `generator.schema.json`**

*   **Purpose:** This file is **not used by the generator at runtime**. Its purpose is purely for the **developer experience**.
*   **Benefits:**
    1.  **Validation:** It prevents typos and structural errors in the `generator.config.json` file.
    2.  **IntelliSense:** When referenced via the `$schema` key, IDEs like VS Code will provide autocompletion for property names and values.
    3.  **Self-Documentation:** It serves as the formal contract for what constitutes a valid configuration file.


schemas/sql-config.schema.json