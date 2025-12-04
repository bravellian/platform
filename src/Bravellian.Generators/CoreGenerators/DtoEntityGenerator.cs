// Licensed under the Apache License, Version 2.0.
// See LICENSE file in the project root for full license information.

namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class DtoEntityGenerator
{
    public static string? Generate(GeneratorParams? entityToGenerate, IBgLogger? logger)
    {
        if (entityToGenerate != null)
        {
            return GenerateEntityClass(entityToGenerate, isNested: entityToGenerate?.ClassOnly ?? false);
        }

        return null;
    }

    public static string GenerateEntityClass(GeneratorParams entity, bool isNested = false)
    {
        var indentation = isNested ? "        " : "    ";
        var classIndentation = isNested ? "    " : "";

        IEnumerable<string> propertyStrings = entity.Properties.Select(p =>
        {
            var nullableSymbol = (p.IsNullable && !p.Type.EndsWith("?", StringComparison.OrdinalIgnoreCase)) ? "?" : string.Empty;
            var requiredAttribute = (p.IsRequired && string.IsNullOrEmpty(p.Expression)) ? $"\r\n{indentation}[Required]" : string.Empty;
            var validationAttributes = new List<string>();

#pragma warning disable MA0127 // Use String.Equals instead of is pattern
            if (string.Equals(p.Type, "string", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(p.Max))
                {
                    validationAttributes.Add($"[StringLength({p.Max})]");
                }

                if (!string.IsNullOrEmpty(p.Min))
                {
                    validationAttributes.Add($"[MinLength({p.Min})]");
                }
                else if (p.NonWhitespace)
                {
                    // For non-whitespace strings without explicit Min, add MinLength(1)
                    validationAttributes.Add($"[MinLength(1)]");
                }

                if (!string.IsNullOrEmpty(p.Regex))
                {
                    validationAttributes.Add($"[RegularExpression(@\"{p.Regex}\")]");
                }
            }
            else if (p.Type is "int" or "long" or "decimal" or "float" or "double")
            {
                if (!string.IsNullOrEmpty(p.Max) || !string.IsNullOrEmpty(p.Min))
                {
                    validationAttributes.Add($"[Range({p.Min ?? "null"}, {p.Max ?? "null"})]");
                }
            }
            else if (string.Equals(p.Type, "DateTime", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(p.Min) || !string.IsNullOrEmpty(p.Max))
                {
                    var minDate = p.Min ?? "\"1900-01-01\"";
                    var maxDate = p.Max ?? "\"2100-12-31\"";
                    validationAttributes.Add($"[Range(typeof(DateTime), {minDate}, {maxDate})]");
                }
            }
#pragma warning restore MA0127 // Use String.Equals instead of is pattern

            var validationString = validationAttributes.Count > 0
                ? string.Join($"\r\n{indentation}", validationAttributes) + $"\r\n{indentation}"
                : string.Empty;

            var requiredString = (p.IsRequired && string.IsNullOrEmpty(p.Expression))
                ? "required "
                : string.Empty;

            var jsonPropertyAttributeString = !string.IsNullOrEmpty(p.JsonProperty)
                ? $"[JsonPropertyName(\"{p.JsonProperty}\")]\r\n{indentation}"
                : string.Empty;

            var jsonIgnoreAttribute = !string.IsNullOrEmpty(p.Expression) && string.IsNullOrEmpty(p.JsonProperty)
                ? $"[JsonIgnore]\r\n{indentation}"
                : string.Empty;

            var accessorType = p.IsSettable ? "set" : "init";

            var documentationString = !string.IsNullOrEmpty(p.Documentation)
                ? $"\r\n{indentation}/// <summary>\r\n{indentation}/// {(p.IsSettable ? "Gets or sets" : "Gets")} the {p.Name} property.\r\n{indentation}/// </summary>\r\n{indentation}/// <remarks>\r\n{indentation}/// {p.Documentation}\r\n{indentation}/// </remarks>"
                : $"\r\n{indentation}/// <summary>\r\n{indentation}/// {(p.IsSettable ? "Gets or sets" : "Gets")} the {p.Name} property.\r\n{indentation}/// </summary>";

            if (!string.IsNullOrEmpty(p.Expression))
            {
                return $@"{documentationString}{requiredAttribute}
{indentation}{jsonIgnoreAttribute}{validationString}public {requiredString}{p.Type}{nullableSymbol} {p.Name} => {p.Expression};";
            }

            // Build property initializer for default values
            var propertyInitializer = string.Empty;
            if (!string.IsNullOrEmpty(p.DefaultValue) && !p.NoDefault)
            {
                propertyInitializer = $" = {p.DefaultValue};";
            }

            return $@"{documentationString}{requiredAttribute}
{indentation}{jsonPropertyAttributeString}{jsonIgnoreAttribute}{validationString}public {requiredString}{p.Type}{nullableSymbol} {p.Name} {{ get; {accessorType}; }}{propertyInitializer}";
        });

        var propertiesString = string.Join("\r\n", propertyStrings);
        var validatorClass = GenerateValidatorClass(entity, isNested);

        IEnumerable<string> nestedEntityClasses = entity.NestedEntities.Select(e => GenerateEntityClass(e, isNested: true));
        var nestedEntitiesString = string.Join("\r\n\r\n", nestedEntityClasses);

        var classDocumentation = !string.IsNullOrEmpty(entity.Documentation)
            ? $"\r\n{classIndentation}/// <summary>\r\n{classIndentation}/// {entity.Documentation}\r\n{classIndentation}/// </summary>\r\n"
            : string.Empty;

        var accessibility = string.IsNullOrEmpty(entity.Accessibility) ? "public" : entity.Accessibility;
        var abstractModifier = entity.Abstract ?? string.Empty;
        var inheritsClause = entity.Inherits ?? string.Empty;
        var structModifier = entity.IsRecordStruct ? "struct " : string.Empty;

        // Generate a factory method unless the DTO is abstract or generation is explicitly suppressed.
        var factoryMethod = string.Empty;
        if (string.IsNullOrEmpty(entity.Abstract) && !entity.NoCreateMethod)
        {
            factoryMethod = GenerateDtoFactoryMethod(entity, classIndentation);
        }

        var classDefinition = $$"""
{{classIndentation}}{{classDocumentation}}{{accessibility}}{{abstractModifier}} partial record {{structModifier}}{{entity.Name}}{{inheritsClause}}
{{classIndentation}}{
{{propertiesString}}

{{factoryMethod}}
{{classIndentation}}    // Static validator instance
{{classIndentation}}    public static readonly {{entity.Name}}Validator Validator = new {{entity.Name}}Validator();

{{classIndentation}}    // Instance method for validation
{{classIndentation}}    public void Validate()
{{classIndentation}}    {
{{classIndentation}}        var validationResult = Validator.Validate(this);
{{classIndentation}}        if (!validationResult.IsValid)
{{classIndentation}}        {
{{classIndentation}}            throw new FluentValidation.ValidationException(validationResult.ToString());
{{classIndentation}}        }
{{classIndentation}}    }

{{classIndentation}}    // Method to check validation without throwing exceptions
{{classIndentation}}    public FluentValidation.Results.ValidationResult GetValidationResult()
{{classIndentation}}    {
{{classIndentation}}        return Validator.Validate(this);
{{classIndentation}}    }

{{validatorClass}}

{{nestedEntitiesString}}
{{classIndentation}}}
""";

        if (!isNested)
        {
            var licenseHeader = entity.LicenseHeader ?? string.Empty;

            return $$"""
// <auto-generated/>
{{licenseHeader}}

#nullable enable

namespace {{entity.Namespace}};

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FluentValidation;

{{classDefinition}}
""";
        }
        else
        {
            return classDefinition;
        }
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name ?? string.Empty;
        }

        if (name.Length == 1)
        {
            return name.ToLowerInvariant();
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string GenerateDtoFactoryMethod(GeneratorParams entity, string classIndentation)
    {
        // Get properties that should be parameters (non-expression properties)
        var factoryProperties = entity.Properties
            .Where(p => string.IsNullOrEmpty(p.Expression))
            .ToList();

        static bool HasParameterDefault(PropertyDescriptor p) =>
            !string.IsNullOrEmpty(p.DefaultValue) && !p.NoDefault;

        // Keep parameters without defaults before any optional/defaulted parameters to satisfy C# rules
        var requiredProperties = factoryProperties
            .Where(p => p.IsRequired && !HasParameterDefault(p))
            .ToList();
        var optionalWithoutDefaults = factoryProperties
            .Where(p => !p.IsRequired && !HasParameterDefault(p))
            .ToList();
        var optionalWithDefaults = factoryProperties
            .Where(HasParameterDefault)
            .ToList();

        var orderedProperties = requiredProperties
            .Concat(optionalWithoutDefaults)
            .Concat(optionalWithDefaults)
            .ToList();

        // Generate parameters
        var parameters = orderedProperties.Select(p =>
        {
            var nullableSymbol = (p.IsNullable && !p.Type.EndsWith("?", StringComparison.OrdinalIgnoreCase)) ? "?" : string.Empty;
            var camelCaseName = ToCamelCase(p.Name);
            var hasParameterDefault = HasParameterDefault(p);

            // Optional parameter with default value
            if (hasParameterDefault)
            {
                return $"{p.Type}{nullableSymbol} {camelCaseName} = {p.DefaultValue}";
            }

            // Required or no default
            return $"{p.Type}{nullableSymbol} {camelCaseName}";
        });

        var parametersString = string.Join($",\r\n{classIndentation}        ", parameters);

        // Generate property assignments
        var assignments = orderedProperties.Select(p =>
        {
            var camelCaseName = ToCamelCase(p.Name);
            return $"{p.Name} = {camelCaseName}";
        });

        var assignmentsString = string.Join($",\r\n{classIndentation}            ", assignments);

        var privateConstructor = entity.IsStrict
            ? $"    // Private constructor to enforce strict DTO pattern\r\n{classIndentation}    private {entity.Name}() {{ }}\r\n"
            : string.Empty;

        return $$"""

{{classIndentation}}{{privateConstructor}}
{{classIndentation}}    /// <summary>
{{classIndentation}}    /// Factory method to create a validated instance of {{entity.Name}}.
{{classIndentation}}    /// </summary>
{{classIndentation}}    public static {{entity.Name}} Create(
{{classIndentation}}        {{parametersString}})
{{classIndentation}}    {
{{classIndentation}}        var instance = new {{entity.Name}}
{{classIndentation}}        {
{{classIndentation}}            {{assignmentsString}}
{{classIndentation}}        };

{{classIndentation}}        instance.Validate();
{{classIndentation}}        return instance;
{{classIndentation}}    }
""";
    }

    private static string GenerateValidatorClass(GeneratorParams entity, bool isNested = false)
    {
        var indentation = isNested ? "        " : "    ";
        var validatorIndentation = indentation;
        var constructorIndentation = validatorIndentation + "    ";
        var ruleIndentation = constructorIndentation + "    ";

        var validatorClassName = $"{entity.Name}Validator";
        var inheritsFrom = !string.IsNullOrWhiteSpace(entity.Inherits) && entity.UseParentValidator
            ? $"{entity.Inherits.Trim().TrimStart(':').Trim()}Validator"
            : $"AbstractValidator<{entity.Name}>";

        var sb = new StringBuilder();
        sb.AppendLine($"{indentation}/// <summary>");
        sb.AppendLine($"{indentation}/// Internal FluentValidator class for validation rules.");
        sb.AppendLine($"{indentation}/// </summary>");
        sb.AppendLine($"{indentation}public partial class {validatorClassName} : {inheritsFrom}");
        sb.AppendLine($"{indentation}{{");

        var baseConstructorCall = entity.Inherits != null ? " : base()" : "";
        sb.AppendLine($"{constructorIndentation}public {validatorClassName}(){baseConstructorCall}");
        sb.AppendLine($"{constructorIndentation}{{");

        if (entity.Inherits == null)
        {
            foreach (var p in entity.Properties)
            {
                if (p.IsRequired)
                {
                    sb.AppendLine($"{ruleIndentation}RuleFor(x => x.{p.Name}).NotNull().WithMessage(\"'{{{p.Name}}}' is required.\");");
                }
            }
        }

        foreach (var p in entity.Properties)
        {
            if (p.IsRequired)
            {
                sb.AppendLine($"{ruleIndentation}RuleFor(x => x.{p.Name}).NotNull().WithMessage(\"'{{{p.Name}}}' is required.\");");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"{ruleIndentation}// Hooks for additional validation");
        sb.AppendLine($"{ruleIndentation}AddCustomValidation();");
        sb.AppendLine($"{constructorIndentation}}}");
        sb.AppendLine();
        sb.AppendLine($"{constructorIndentation}// Partial method for additional validation hooks");
        sb.AppendLine($"{constructorIndentation}partial void AddCustomValidation();");
        sb.AppendLine($"{indentation}}}");

        return sb.ToString();
    }

    public record class GeneratorParams
    {
        public string Name { get; set; } = string.Empty; // Use init for immutability after creation
        public string FullyQualifiedName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string? ParentName { get; set; } // Added ParentName
        public string? Inherits { get; set; }
        public string? Abstract { get; set; }
        public string Accessibility { get; set; } = "public";
        public List<PropertyDescriptor> Properties { get; set; } = new();
        public List<GeneratorParams> NestedEntities { get; set; } = new(); // Use init
        public string? Documentation { get; set; }
        public bool ClassOnly { get; set; } // Indicates if it should only generate the class content
        public string? SourceFilePath { get; set; } // Path to the source XML file that generated this
        public string? LicenseHeader { get; set; } // Custom license header for generated code

        /// <summary>
        /// Gets or sets a value indicating whether indicates if this DTO definition originated from a <ViewModelOwnedType>
        /// in the XML definition, signifying it should be treated as a nested type
        /// within its containing ViewModel for reflection/typeof purposes (Namespace.Outer+Inner).
        /// </summary>
        public bool IsNestedViewModelOwnedType { get; set; } = false; // Default to false

        /// <summary>
        /// Gets or sets a value indicating whether this DTO is strict.
        /// When true, the generator emits a private constructor and a static Create(...) factory method
        /// that enforces validation before returning the instance.
        /// </summary>
        public bool IsStrict { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the generated validator should inherit from the parent's validator.
        /// </summary>
        public bool UseParentValidator { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to suppress the generation of the Create() factory method.
        /// This is useful when inheriting from a base class with its own constructor parameters.
        /// </summary>
        public bool NoCreateMethod { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the generated type should be a record struct.
        /// </summary>
        public bool IsRecordStruct { get; set; } = false;

        public GeneratorParams(
            string name,
            string ns,
            string? parentName, // Added parentName parameter
            string? inherits,
            bool isAbstract,
            string? accessibility,
            string sourceFilePath,
            List<PropertyDescriptor> properties,
            List<GeneratorParams>? nestedEntities = null,
            string? documentation = null,
            bool classOnly = false, // Added classOnly parameter
            bool isStrict = false,
            bool useParentValidator = true,
            bool noCreateMethod = false,
            bool isRecordStruct = false
            )
        {
            this.Name = name;
            this.Namespace = ns;
            this.ParentName = parentName; // Store parentName
            this.Inherits = !string.IsNullOrWhiteSpace(inherits) ? $" : {inherits}" : string.Empty;
            this.Abstract = isAbstract ? " abstract" : string.Empty;
            this.Accessibility = string.IsNullOrEmpty(accessibility) ? "public" : accessibility!;
            this.Properties = properties;
            this.NestedEntities = nestedEntities ?? []; // Initialize if null
            // Update FullyQualifiedName calculation
            this.FullyQualifiedName = string.IsNullOrEmpty(parentName)
                ? string.Join(".", ns, name)
                : string.Join(".", ns, parentName, name); // Include parent name
            this.Documentation = documentation;
            this.ClassOnly = classOnly; // Store classOnly
            this.SourceFilePath = sourceFilePath; // Store sourceFilePath
            this.IsStrict = isStrict;
            this.UseParentValidator = useParentValidator;
            this.NoCreateMethod = noCreateMethod;
            this.IsRecordStruct = isRecordStruct;

            if (!string.IsNullOrEmpty(parentName))
            {
                this.IsNestedViewModelOwnedType = true;
            }
        }
    }

    public readonly record struct PropertyDescriptor
    {
        public readonly string Name;
        public readonly string Type;
        public readonly bool IsRequired;
        public readonly bool IsNullable;
        public readonly string? Max;
        public readonly string? Min;
        public readonly string? Regex;
        public readonly string? JsonProperty;
        public readonly bool NoDefault;
        public readonly bool IsSettable;
        public readonly string? Expression;
        public readonly string? Documentation;
        public readonly string? DefaultValue;
        public readonly bool NonWhitespace;

        public PropertyDescriptor(
            string name,
            string type,
            bool isRequired,
            bool isNullable,
            string? max,
            string? min,
            string? regex,
            string? jsonProperty,
            bool noDefault,
            bool isSettable,
            string? expression = null,
            string? documentation = null,
            string? defaultValue = null,
            bool nonWhitespace = false)
        {
            this.Name = name;
            this.Type = type;
            this.IsRequired = isRequired;
            this.IsNullable = isNullable;
            this.Max = max;
            this.Min = min;
            this.Regex = regex;
            this.JsonProperty = jsonProperty;
            this.NoDefault = noDefault;
            this.IsSettable = isSettable;
            this.Expression = expression;
            this.Documentation = documentation;
            this.DefaultValue = defaultValue;
            this.NonWhitespace = nonWhitespace;
        }
    }
}
