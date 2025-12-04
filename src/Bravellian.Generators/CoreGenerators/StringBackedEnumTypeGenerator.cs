// Licensed under the Apache License, Version 2.0.
// See LICENSE file in the project root for full license information.

namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

public static class StringBackedEnumTypeGenerator
{
    public static GeneratorParams? GetParams(XElement xml, IBgLogger? logger, string sourceFilePath)
    {
        IReadOnlyDictionary<string, string> attributes = xml.GetAttributeDict();
        var enumValues = xml.Elements("Value").Select(e => e.GetAttributeDict()).Select(a => (a["value"].ToString(), a["name"].ToString(), a.TryGetValue("display") ?? a["name"].ToString(), a.TryGetValue("documentation"))).ToList();
        var additionalProperties = xml.Elements("Property").Select(e => e.GetAttributeDict()).Select(a => (a["type"].ToString(), a["name"].ToString())).ToList();
        return new(attributes.TryGetValue("name"), attributes.TryGetValue("namespace"), true, enumValues, additionalProperties, sourceFilePath);
    }

    public static string? Generate(GeneratorParams? structToGenerate, IBgLogger? logger)
    {
        if (structToGenerate is { } sg)
        {
            return GenerateClass(sg);
        }

        return null;
    }

    public static string? GenerateValueConverter(GeneratorParams? structToGenerate, IBgLogger? logger)
    {
        if (structToGenerate.HasValue)
        {
            return ValueConverterGenerator.GenerateStringBackedEnumConverter(
                structToGenerate.Value.Name,
                structToGenerate.Value.Namespace);
        }
        else
        {
            return null;
        }
    }

    private static string GenerateClass(GeneratorParams relatedClass)
    {
        var constValues = string.Join(
            "\r\n",
            relatedClass.EnumValues.Select(p => $"    public const string {p.Name}Value = \"{p.Value}\";").Concat(
            relatedClass.EnumValues.Select(p => $"    public const string {p.Name}DisplayName = \"{p.DisplayName}\";")));

        var enumDefinitions = string.Join("\r\n\r\n", relatedClass.EnumValues.Select(p =>
        {
            var documentation = p.Documentation != null ? $"/// <summary>\r\n    /// {p.Documentation}\r\n    /// </summary>\r\n" : string.Empty;
            return $"{documentation}    public static readonly {relatedClass.Name} {p.Name} = new({p.Name}Value, {p.Name}DisplayName);";
        }));

        var tryParse = string.Join("\r\n\r\n", relatedClass.EnumValues.Select(p => $$"""
                    _ when string.Equals(value, {{p.Name}}Value, StringComparison.OrdinalIgnoreCase) => {{p.Name}},
        """));
        var allValuesLine = string.Join("\r\n", relatedClass.EnumValues.Select(p => $"        {p.Name},"));

        var matchCases = string.Join("\r\n", relatedClass.EnumValues.Select(p => $$"""
                        case {{p.Name}}Value:
                            case{{p.Name}}();
                            return;
            """));
        var matchTCases = string.Join("\r\n", relatedClass.EnumValues.Select(p => $$"""
                        case {{p.Name}}Value:
                            return case{{p.Name}}();
            """));
        var tryMatchCases = string.Join("\r\n", relatedClass.EnumValues.Select(p => $$"""
                        case {{p.Name}}Value:
                            case{{p.Name}}();
                            return true;
            """));
        var tryMatchTCases = string.Join("\r\n", relatedClass.EnumValues.Select(p => $$"""
                        case {{p.Name}}Value:
                            result = case{{p.Name}}();
                            return true;
            """));
        var matchParams = string.Join(", ", relatedClass.EnumValues.Select(p => $"Action case{p.Name}"));
        var matchTParams = string.Join(", ", relatedClass.EnumValues.Select(p => $"Func<T> case{p.Name}"));

        var additionalProperties = string.Empty;
        var constructorInit = "        ProcessValue(value);";
        var processValueSignature = "static partial void ProcessValue(string value);";
        if (relatedClass.AdditionalProperties is { Count: > 0 })
        {
            additionalProperties = "\r\n\r\n" + string.Join("\r\n\r\n", relatedClass.AdditionalProperties.Select(p => $"    public {p.Type} {p.Name} {{ get; init; }}"));
            var outParams = string.Join(", ", relatedClass.AdditionalProperties.Select(p => $"out {p.Type} {p.Name.ToLower()}"));
            processValueSignature = $"private static partial void ProcessValue(string value, {outParams});";
            constructorInit = $$"""
                    ProcessValue(value, {{string.Join(", ", relatedClass.AdditionalProperties.Select(p => $"out {p.Type} {p.Name.ToLower()}"))}});
            {{string.Join("\r\n", relatedClass.AdditionalProperties.Select(p => $"        this.{p.Name} = {p.Name.ToLower()};"))}}
            """;
        }

        var licenseHeader = relatedClass.LicenseHeader ?? string.Empty;

        return $$"""
// <auto-generated/>
{{licenseHeader}}

#nullable enable

namespace {{relatedClass.Namespace}};

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Diagnostics;

[JsonConverter(typeof({{relatedClass.Name}}JsonConverter))]
[TypeConverter(typeof({{relatedClass.Name}}TypeConverter))]
public readonly partial record struct {{relatedClass.Name}}
        : IComparable,
          IComparable<{{relatedClass.Name}}>,
          IEquatable<{{relatedClass.Name}}>,
          IParsable<{{relatedClass.Name}}>
{
{{constValues}}

{{enumDefinitions}}

{{additionalProperties}}

    private {{relatedClass.Name}}([ConstantExpected] string value, [ConstantExpected] string displayName)
    {
        this.Value = value;
        this.DisplayName = displayName;
{{constructorInit}}
    }

    public static IReadOnlySet<{{relatedClass.Name}}> AllValues { get; } = new HashSet<{{relatedClass.Name}}>
    {
{{allValuesLine}}
    };

    public string Value { get; init; }

    public string DisplayName { get; init; }

    public static {{relatedClass.Name}} From(string value) => Parse(value);

    {{processValueSignature}}

    public override string ToString() => this.Value;

    public bool Equals({{relatedClass.Name}} other)
    {
        return string.Equals(this.Value, other.Value);
    }

    public override int GetHashCode()
    {
        return this.Value?.GetHashCode() ?? 0;
    }

    public int CompareTo({{relatedClass.Name}} other)
    {
        return string.Compare(this.Value, other.Value);
    }

    public int CompareTo(object? obj)
    {
        return obj is {{relatedClass.Name}} id ? this.Value.CompareTo(id.Value) : this.Value.CompareTo(obj);
    }

    public static bool operator <({{relatedClass.Name}} left, {{relatedClass.Name}} right) => left.CompareTo(right) < 0;

    public static bool operator <=({{relatedClass.Name}} left, {{relatedClass.Name}} right) => left.CompareTo(right) <= 0;

    public static bool operator >({{relatedClass.Name}} left, {{relatedClass.Name}} right) => left.CompareTo(right) > 0;

    public static bool operator >=({{relatedClass.Name}} left, {{relatedClass.Name}} right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Matches the current enum value against all possible cases and executes the corresponding delegate.
    /// Throws <see cref="Bravellian.UnhandledEnumValueException"/> if no match is found.
    /// </summary>
{{string.Join("\r\n", relatedClass.EnumValues.Select(p => $"    /// <param name=\"case{p.Name}\">The delegate to execute for the {p.Name} case.</param>"))}}
    /// <exception cref="Bravellian.UnhandledEnumValueException">Thrown when the current value is not handled by any case.</exception>
    public void Match({{matchParams}})
    {
        switch (this.Value)
        {
{{matchCases}}
            default:
                throw new Bravellian.UnhandledEnumValueException(this);
        }
    }

    /// <summary>
    /// Matches the current enum value against all possible cases and returns the result of executing the corresponding delegate.
    /// Throws <see cref="Bravellian.UnhandledEnumValueException"/> if no match is found.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
{{string.Join("\r\n", relatedClass.EnumValues.Select(p => $"    /// <param name=\"case{p.Name}\">The delegate to execute for the {p.Name} case.</param>"))}}
    /// <returns>The result of executing the matching delegate.</returns>
    /// <exception cref="Bravellian.UnhandledEnumValueException">Thrown when the current value is not handled by any case.</exception>
    public T Match<T>({{matchTParams}})
    {
        switch (this.Value)
        {
{{matchTCases}}
            default:
                throw new Bravellian.UnhandledEnumValueException(this);
        }
    }

    /// <summary>
    /// Attempts to match the current enum value against all possible cases and executes the corresponding delegate.
    /// Returns false if no match is found.
    /// </summary>
{{string.Join("\r\n", relatedClass.EnumValues.Select(p => $"    /// <param name=\"case{p.Name}\">The delegate to execute for the {p.Name} case.</param>"))}}
    /// <returns>True if a match was found and the corresponding delegate was executed, false otherwise.</returns>
    public bool TryMatch({{matchParams}})
    {
        switch (this.Value)
        {
{{tryMatchCases}}
            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to match the current enum value against all possible cases and returns the result of executing the corresponding delegate.
    /// Returns false if no match is found.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
{{string.Join("\r\n", relatedClass.EnumValues.Select(p => $"    /// <param name=\"case{p.Name}\">The delegate to execute for the {p.Name} case.</param>"))}}
    /// <param name="result">The result of executing the matching delegate, if a match was found.</param>
    /// <returns>True if a match was found and the corresponding delegate was executed, false otherwise.</returns>
    public bool TryMatch<T>({{matchTParams}}, out T result)
    {
        switch (this.Value)
        {
{{tryMatchTCases}}
            default:
                result = default!;
                return false;
        }
    }

    public static {{relatedClass.Name}}? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value switch
        {
{{tryParse}}
            _ => null,
        };
    }

    public static bool TryParse(string? value, out {{relatedClass.Name}} parsed) => TryParse(value, null, out parsed);

    public static {{relatedClass.Name}} Parse(string value) => Parse(value, null);

    public static {{relatedClass.Name}} Parse(string s, IFormatProvider? provider)
    {
        Guard.IsNotNull(s);

        if (TryParse(s, provider, out {{relatedClass.Name}} parsed))
        {
            return parsed;
        }
        else
        {
            throw new ArgumentOutOfRangeException($"The value {s} is not a valid {{relatedClass.Name}}.");
        }
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out {{relatedClass.Name}} result)
    {
        {{relatedClass.Name}}? parsed = TryParse(s);
        if (parsed.HasValue)
        {
            result = parsed.Value;
            return true;
        }

        result = default;
        return false;
    }

    public class {{relatedClass.Name}}JsonConverter : JsonConverter<{{relatedClass.Name}}>
    {
        public override {{relatedClass.Name}} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();

            if (!string.IsNullOrEmpty(s) && {{relatedClass.Name}}.TryParse(s, out {{relatedClass.Name}} result))
            {
                return result;
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, {{relatedClass.Name}} value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, {{relatedClass.Name}} value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.Value);

        public override {{relatedClass.Name}} ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return this.Read(ref reader, typeToConvert, options);
        }
    }

    // TypeConverter for {{relatedClass.Name}} to and from string
    public class {{relatedClass.Name}}TypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string s)
            {
                return {{relatedClass.Name}}.TryParse(s) ?? default;
            }

            return base.ConvertFrom(context, culture, value) ?? default;
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (value is {{relatedClass.Name}} type && destinationType == typeof(string))
            {
                return type.ToString();
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

""";
    }

    public readonly record struct GeneratorParams
    {
        public readonly string Name;
        public readonly string FullyQualifiedName;
        public readonly string Namespace;
        public readonly bool IsPublic;
        public readonly IReadOnlyList<(string Value, string Name, string? DisplayName, string? Documentation)>? EnumValues;
        public readonly IReadOnlyList<(string Type, string Name)>? AdditionalProperties;
        public readonly string? SourceFilePath;
        public readonly string? LicenseHeader;

        public GeneratorParams(string name, string ns, bool isPublic, IReadOnlyList<(string Value, string Name, string? DisplayName, string? Documentation)>? enumValues, IReadOnlyList<(string Type, string Name)>? additionalProperties, string? sourceFilePath, string? licenseHeader = null)
        {
            this.Name = name;
            this.Namespace = ns;
            this.IsPublic = isPublic;
            this.FullyQualifiedName = string.Join(".", ns, name);
            this.EnumValues = enumValues;
            this.AdditionalProperties = additionalProperties;
            this.SourceFilePath = sourceFilePath;
            this.LicenseHeader = licenseHeader;
        }
    }
}
