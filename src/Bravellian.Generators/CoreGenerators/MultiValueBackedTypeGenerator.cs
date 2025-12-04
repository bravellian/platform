// Licensed under the Apache License, Version 2.0.
// See LICENSE file in the project root for full license information.

namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public static class MultiValueBackedTypeGenerator
{
    public static GeneratorParams? GetParams(XElement xml, IBgLogger? logger, string sourceFilePath)
    {
        IReadOnlyDictionary<string, string> attributes = xml.GetAttributeDict();

        var fields = xml.Elements()
            .Select(e =>
            {
                var a = e.GetAttributeDict();
                if (e.Name == "StringProperty")
                {
                    return new FieldInfo(
                        a["name"].ToString(),
                        "string",
                        a.TryGetValue("serializedName"),
                        a.TryGetValue("constantValue"),
                        a.TryGetValue("constantTypeValue"),
                        bool.TryParse(a.TryGetValue("isNullable"), out bool isNullable) && isNullable,
                        a.TryGetValue("nullIdentifier"));
                }
                else // TypeProperty
                {
                    return new FieldInfo(
                        a["name"].ToString(),
                        a["type"].ToString(),
                        a.TryGetValue("serializedName"),
                        a.TryGetValue("constantValue"),
                        a.TryGetValue("constantTypeValue"),
                        bool.TryParse(a.TryGetValue("isNullable"), out bool isNullable) && isNullable,
                        a.TryGetValue("nullIdentifier"));
                }
            })
            .ToList();
        return new(attributes.TryGetValue("name") ?? string.Empty, attributes.TryGetValue("namespace") ?? string.Empty, true, attributes.TryGetValue("separator") ?? "|", attributes.TryGetValue("format") ?? string.Empty, attributes.TryGetValue("regex") ?? string.Empty, attributes.TryGetValue("bookend") ?? string.Empty, fields, sourceFilePath);
    }

    public static string? Generate(GeneratorParams? structToGenerate, IBgLogger? logger)
    {
        if (structToGenerate.HasValue)
        {
            return GenerateWithPattern(structToGenerate.Value);
        }
        else
        {
            return null;
        }
    }

    public static string? GenerateValueConverter(GeneratorParams? structToGenerate, IBgLogger? logger)
    {
        if (structToGenerate.HasValue)
        {
            return ValueConverterGenerator.GenerateMultiValueBackedConverter(
                structToGenerate.Value.Name,
                structToGenerate.Value.Namespace);
        }
        else
        {
            return null;
        }
    }

    private static string GenerateTypedConstructor(in GeneratorParams relatedClass)
    {
        bool useSeparator = string.IsNullOrWhiteSpace(relatedClass.Format);
        bool hasSerializedNames = relatedClass.Fields.Any(f => f.SerializedName != null);
        bool hasConstantValues = relatedClass.Fields.Any(f => f.ConstantValue != null || f.ConstantTypeValue != null);
        bool hasBookend = !string.IsNullOrWhiteSpace(relatedClass.Bookend);

        var nonConstantFields = relatedClass.Fields.Where(f => f.ConstantValue == null && f.ConstantTypeValue == null).ToList();
        var ctorParameters = string.Join(", ", nonConstantFields.Select(f => $"{f.FieldType}{(f.IsNullable ? "?" : "")} {f.FieldName.ToLower()}"));
        var ctorPropertyAssignments = string.Join("\r\n", relatedClass.Fields.Select(f =>
        {
            if (f.ConstantValue != null)
            {
                if (f.IsString)
                {
                    return $"        this.{f.FieldName} = \"{f.ConstantValue}\";";
                }

                return $"        this.{f.FieldName} = {f.FieldType}.Parse(\"{f.ConstantValue}\");";
            }

            if (f.ConstantTypeValue != null)
            {
                return $"        this.{f.FieldName} = {f.ConstantTypeValue};";
            }

            return $"        this.{f.FieldName} = {f.FieldName.ToLower()};";
        }));

        if (useSeparator && !hasSerializedNames)
        {
            var ctorValue = "$\""
                + (hasBookend ? "{Bookend}" : "")
                + string.Join("{Separator}", relatedClass.Fields.Select(f =>
                {
                    if (f.ConstantValue != null)
                    {
                        return f.ConstantValue;
                    }

                    if (f.ConstantTypeValue != null)
                    {
                        return f.ConstantTypeValue;
                    }

                    if (f.IsNullable)
                    {
                        return $"{{{f.FieldName.ToLower()}?.ToString() ?? \"{f.NullIdentifier}\"}}";
                    }

                    return $"{{{f.FieldName.ToLower()}}}";
                }))
                + (hasBookend ? "{Bookend}" : "")
                + "\"";

            var bookendConst = hasBookend ? $"\r\n    public const string Bookend = \"{relatedClass.Bookend}\";" : string.Empty;

            return $$"""
    public const string Separator = "{{relatedClass.Separator ?? "|"}}";{{bookendConst}}

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public {{relatedClass.Name}}({{ctorParameters}})
    {
{{ctorPropertyAssignments}}

        this.internalValue = {{ctorValue}};
    }

""";
        }
        else
        {
            var formatParts = new List<string>();
            var regexParts = new List<string>();
            foreach (var field in relatedClass.Fields)
            {
                string prefix = string.IsNullOrWhiteSpace(field.SerializedName) ? string.Empty : $"{field.SerializedName}=";
                string value;

                if (field.ConstantValue != null)
                {
                    formatParts.Add(prefix + field.ConstantValue);
                    regexParts.Add(prefix + field.ConstantValue);
                }
                else if (field.ConstantTypeValue != null)
                {
                    formatParts.Add(prefix + $"{{{field.FieldName}}}");
                    regexParts.Add(prefix + $"{{{field.FieldName}}}");
                }
                else
                {
                    if (field.IsNullable)
                    {
                        formatParts.Add(prefix + $"{{{field.FieldName}?.ToString() ?? \"{field.NullIdentifier}\"}}");
                        regexParts.Add(prefix + $"{{{field.FieldName}}}");
                    }
                    else
                    {
                        formatParts.Add(prefix + $"{{{field.FieldName}}}");
                        regexParts.Add(prefix + $"{{{field.FieldName}}}");
                    }
                }
            }

            var formatPattern = string.Join(relatedClass.Separator ?? "|", formatParts);
            var regexTempPattern = string.Join(relatedClass.Separator ?? "|", regexParts);
            var ctorValue = "$\""
                + (hasBookend ? "{Bookend}" : "")
                + formatPattern
                + (hasBookend ? "{Bookend}" : "")
                + "\"";

            string regexPattern = relatedClass.Regex;
            if (string.IsNullOrWhiteSpace(regexPattern))
            {
                regexPattern = Regex.Escape(regexTempPattern);
                var lastField = relatedClass.Fields.Last();
                foreach (var field in relatedClass.Fields)
                {
                    if (field.ConstantValue != null)
                    {
                        regexPattern = Regex.Replace(regexPattern, $"\\\\{{{field.FieldName}}}", Regex.Escape(field.ConstantValue), RegexOptions.Compiled, TimeSpan.FromSeconds(2));
                    }
                    else
                    {
                        if (field == lastField)
                        {
                            if (hasBookend)
                            {
                                regexPattern = Regex.Replace(regexPattern, $"\\\\{{{field.FieldName}}}", $"(?<{field.FieldName.ToLower()}>[^{relatedClass.Bookend}]*)", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
                            }
                            else
                            {
                                regexPattern = Regex.Replace(regexPattern, $"\\\\{{{field.FieldName}}}", $"(?<{field.FieldName.ToLower()}>.*)", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
                            }
                        }
                        else
                        {
                            regexPattern = Regex.Replace(regexPattern, $"\\\\{{{field.FieldName}}}", $"(?<{field.FieldName.ToLower()}>[^{relatedClass.Separator ?? "|"}]*)", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
                        }
                    }
                }
            }

            var bookendConst = hasBookend ? $"    public const string Bookend = \"{relatedClass.Bookend}\";\r\n" : string.Empty;
            var regexString = hasBookend ? $"@$\"(?:{{Bookend}})?{regexPattern}(?:{{Bookend}})?\"" : $"@\"{regexPattern}\"";

            return $$"""
{{bookendConst}}    public const string RegexString = {{regexString}};
    public const string ExactRegexString = $"^{RegexString}$";
    public static Regex ValidationRegex = new Regex(ExactRegexString, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(1000));

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public {{relatedClass.Name}}({{ctorParameters}})
    {
{{ctorPropertyAssignments}}

        this.internalValue = {{ctorValue}};
    }

""";
        }
    }

    private static string GenerateTryParse(in GeneratorParams relatedClass)
    {
        bool useSeparator = string.IsNullOrWhiteSpace(relatedClass.Format);
        bool hasSerializedNames = relatedClass.Fields.Any(f => f.SerializedName != null);
        bool hasConstantValues = relatedClass.Fields.Any(f => f.ConstantValue != null || f.ConstantTypeValue != null);

        if (useSeparator && !hasSerializedNames)
        {
            var nonConstantFields = relatedClass.Fields.Where(f => f.ConstantValue == null && f.ConstantTypeValue == null).ToList();
            var numFields = nonConstantFields.Count;
            var hasNonStringFields = nonConstantFields.Any(f => !f.IsString);

            var tryParse = hasNonStringFields
                ? string.Join("\r\n                  && ", nonConstantFields.Where(f => !f.IsString).Select((f, i) => f.IsNullable ? $"TryParseNullable(split[{i}], \"{f.NullIdentifier}\", out {f.FieldType}? {f.FieldName.ToLower()})" : $"{f.FieldType}.TryParse(split[{i}], out {f.FieldType} {f.FieldName.ToLower()})"))
                : null;

            var tryParseConstructorParams = string.Join(", ", relatedClass.Fields.Where(f => f.ConstantValue == null && f.ConstantTypeValue == null).Select(f =>
            {
                if (f.IsString)
                {
                    if (f.IsNullable)
                    {
                        return $"NullableString(split[{nonConstantFields.IndexOf(f)}], \"{f.NullIdentifier}\")";
                    }

                    return $"split[{nonConstantFields.IndexOf(f)}]";
                }
                else
                {
                    return f.FieldName.ToLower();
                }
            }));

            string parseLogic = hasNonStringFields
                ? $$"""
                if ({{tryParse}})
                {
                    result = new({{tryParseConstructorParams}});
                    return true;
                }
"""
                : $$"""
                result = new({{tryParseConstructorParams}});
                return true;
""";

            return $$"""

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out {{relatedClass.Name}} result)
    {
        if (!string.IsNullOrWhiteSpace(s))
        {
            string[] split = s.Split(Separator, {{numFields}}, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length == {{numFields}})
            {
                {{parseLogic}}
            }
        }

        result = default;
        return false;
    }

""";
        }
        else
        {
            var parseVars = new List<string>();
            var validationChecks = new List<string>();
            foreach (var field in relatedClass.Fields)
            {
                if (field.ConstantValue != null)
                {
                    // {
                    //     validationChecks.Add($"            if (match.Groups[\"{field.FieldName.ToLower()}\"].Value != \"{field.ConstantValue}\") return false;");
                    // }
                    // else
                    if (!field.IsString)
                    {
                        parseVars.Add($"            if (!{field.FieldType}.TryParse(match.Groups[\"{field.FieldName.ToLower()}\"].Value, out {field.FieldType} {field.FieldName.ToLower()})) return false;");
                        validationChecks.Add($"            if ({field.FieldName.ToLower()} != {field.FieldType}.Parse(\"{field.ConstantValue}\")) return false;");
                    }
                }
                else if (field.ConstantTypeValue != null)
                {
                    parseVars.Add($"            if (!{field.FieldType}.TryParse(match.Groups[\"{field.FieldName.ToLower()}\"].Value, out {field.FieldType} {field.FieldName.ToLower()})) return false;");
                    validationChecks.Add($"            if ({field.FieldName.ToLower()} != {field.ConstantTypeValue}) return false;");
                }
                else
                {
                    if (field.IsString)
                    {
                        if (field.IsNullable)
                        {
                            parseVars.Add($"            var {field.FieldName.ToLower()} = NullableString(match.Groups[\"{field.FieldName.ToLower()}\"].Value, \"{field.NullIdentifier}\");");
                        }
                        else
                        {
                            parseVars.Add($"            var {field.FieldName.ToLower()} = match.Groups[\"{field.FieldName.ToLower()}\"].Value;");
                        }
                    }
                    else
                    {
                        if (field.IsNullable)
                        {
                            parseVars.Add($"            if (!TryParseNullable(match.Groups[\"{field.FieldName.ToLower()}\"].Value, \"{field.NullIdentifier}\", out {field.FieldType}? {field.FieldName.ToLower()})) return false;");
                        }
                        else
                        {
                            parseVars.Add($"            if (!{field.FieldType}.TryParse(match.Groups[\"{field.FieldName.ToLower()}\"].Value, out {field.FieldType} {field.FieldName.ToLower()})) return false;");
                        }
                    }
                }
            }

            var initParams = string.Join(", ", relatedClass.Fields.Where(f => f.ConstantValue == null && f.ConstantTypeValue == null).Select(f => f.FieldName.ToLower()));

            return $$"""

    public static bool TryParse(string? value, IFormatProvider? provider, [NotNullWhen(true)] out {{relatedClass.Name}} result)
    {
        result = default;
        try
        {
            if (value is not null)
            {
                var match = ValidationRegex.Match(value);
                if (match.Success)
                {
        {{string.Join("\r\n    ", parseVars)}}

        {{string.Join("\r\n    ", validationChecks)}}

                    result = new {{relatedClass.Name}}({{initParams}});
                    return true;
                }
            }
        }
        catch
        {
            // Fall through to return false
        }

        return false;
    }

    private static void ValidateValueOrThrow(string value)
    {
        if (!ValidationRegex.IsMatch(value))
        {
            throw new ArgumentOutOfRangeException($"The value {value} does not match the expected regular expression: " + $"'{RegexString}'");
        }
    }

""";
        }
    }

    private static string GenerateWithPattern(in GeneratorParams relatedClass)
    {
        var properties = string.Join("\r\n\r\n", relatedClass.Fields.Select(f => $"    public {f.FieldType}{(f.IsNullable ? "?" : "")} {f.FieldName} {{ get; }}"));

        var fieldTypes = string.Join(", ", relatedClass.Fields.Select(f => f.FieldType));

        var nullableHelpers = relatedClass.Fields.Any(f => f.IsNullable)
            ? $$"""

    private static string? NullableString(string s, string nullIdentifier) => s == nullIdentifier ? null : s;

    private static bool TryParseNullable<T>(string s, string nullIdentifier, out T? result) where T : struct, IParsable<T>
    {
        if (s == nullIdentifier)
        {
            result = null;
            return true;
        }
        if (T.TryParse(s, null, out T parsed))
        {
            result = parsed;
            return true;
        }
        result = null;
        return false;
    }
"""
            : string.Empty;

        var licenseHeader = relatedClass.LicenseHeader ?? string.Empty;

        return $$"""
// <auto-generated/>
{{licenseHeader}}

#nullable enable

namespace {{relatedClass.Namespace}};


using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

[JsonConverter(typeof({{relatedClass.Name}}JsonConverter))]
[TypeConverter(typeof({{relatedClass.Name}}TypeConverter))]
public readonly partial record struct {{relatedClass.Name}}
        : IComparable,
          IComparable<{{relatedClass.Name}}>,
          IEquatable<{{relatedClass.Name}}>,
          IParsable<{{relatedClass.Name}}>
{
    private readonly string internalValue;

{{GenerateTypedConstructor(relatedClass)}}

{{properties}}

    public string Value => this.ToString();

    public static {{relatedClass.Name}} From(string value) => Parse(value);

    public override string ToString() => this.internalValue;

    public bool Equals({{relatedClass.Name}} other)
    {
        return this.internalValue.Equals(other.internalValue);
    }

    public override int GetHashCode()
    {
        return this.internalValue.GetHashCode();
    }

    public int CompareTo({{relatedClass.Name}} other)
    {
        return this.internalValue.CompareTo(other.internalValue);
    }

    public int CompareTo(object? obj)
    {
        return obj is {{relatedClass.Name}} id ? this.internalValue.CompareTo(id.internalValue) : this.internalValue.CompareTo(obj);
    }

    public static bool operator <({{relatedClass.Name}} left, {{relatedClass.Name}} right) => left.CompareTo(right) < 0;

    public static bool operator <=({{relatedClass.Name}} left, {{relatedClass.Name}} right) => left.CompareTo(right) <= 0;

    public static bool operator >({{relatedClass.Name}} left, {{relatedClass.Name}} right) => left.CompareTo(right) > 0;

    public static bool operator >=({{relatedClass.Name}} left, {{relatedClass.Name}} right) => left.CompareTo(right) >= 0;

    public static {{relatedClass.Name}}? TryParse(string? value)
    {
        if (TryParse(value, out {{relatedClass.Name}} result))
        {
            return result;
        }

        return null;
    }

    public static bool TryParse(string? value, out {{relatedClass.Name}} id) => TryParse(value, null, out id);

    public static {{relatedClass.Name}} Parse(string s) => Parse(s, null);

    public static {{relatedClass.Name}} Parse(string s, IFormatProvider? provider)
    {
        if (TryParse(s, provider, out {{relatedClass.Name}} id))
        {
            return id;
        }

        throw new ArgumentOutOfRangeException(nameof(s), s, $"{{relatedClass.Name}} is not in a valid format.");
    }

{{GenerateTryParse(relatedClass)}}
{{nullableHelpers}}

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
            writer.WriteStringValue(value.internalValue);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, {{relatedClass.Name}} value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.internalValue);

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
        public readonly string Separator;
        public readonly string Format;
        public readonly string Regex;
        public readonly string Bookend;
        public readonly IReadOnlyList<FieldInfo> Fields;
        public readonly string? SourceFilePath;
        public readonly string? LicenseHeader;

        public GeneratorParams(string name, string ns, bool isPublic, string separator, string format, string regex, string bookend, IReadOnlyList<FieldInfo> fields, string? sourceFilePath, string? licenseHeader = null)
        {
            this.Name = name;
            this.Namespace = ns;
            this.IsPublic = isPublic;
            this.Separator = separator;
            this.Format = format;
            this.Regex = regex;
            this.Bookend = bookend;
            this.FullyQualifiedName = string.Join(".", ns, name);
            this.Fields = fields;
            this.SourceFilePath = sourceFilePath;
            this.LicenseHeader = licenseHeader;
        }
    }

    public readonly record struct FieldInfo
    {
        public readonly string FieldName;
        public readonly string FieldType;
        public readonly bool IsString;
        public readonly bool IsNullable;
        public readonly string NullIdentifier;
        public readonly string? SerializedName;
        public readonly string? ConstantValue;
        public readonly string? ConstantTypeValue;

        public FieldInfo(string fieldName, string fieldType, string? serializedName = null, string? constantValue = null, string? constantTypeValue = null, bool isNullable = false, string? nullIdentifier = null)
        {
            this.FieldName = fieldName;
            this.FieldType = fieldType;
            this.IsString = string.Equals(this.FieldType, "string", StringComparison.OrdinalIgnoreCase);
            this.IsNullable = isNullable;
            this.NullIdentifier = nullIdentifier ?? "~";
            this.SerializedName = serializedName;
            this.ConstantValue = constantValue;
            this.ConstantTypeValue = constantTypeValue;
        }
    }
}
