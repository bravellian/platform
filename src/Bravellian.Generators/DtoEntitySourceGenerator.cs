namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;

[Generator(LanguageNames.CSharp)]
public sealed class DtoEntitySourceGenerator : IIncrementalGenerator
{
    private static readonly string[] CandidateSuffixes = new[]
    {
        ".dto.json",
        ".entity.json",
    };

    private readonly record struct InputFile
    {
        public string Path { get; }
        public string? Content { get; }

        public InputFile(string path, string? content)
        {
            Path = path;
            Content = content;
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Get license header from MSBuild property
        var licenseHeaderProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.GeneratedCodeLicenseHeader", out var header);
                return header ?? string.Empty;
            });

        var candidateFiles = context.AdditionalTextsProvider
            .Where(static text => IsCandidateFile(text.Path))
            .Select(static (text, cancellationToken) => new InputFile(text.Path, text.GetText(cancellationToken)?.ToString()))
            .Where(static input => !string.IsNullOrWhiteSpace(input.Content));

        // Combine files with license header
        var filesWithLicense = candidateFiles.Combine(licenseHeaderProvider);

        context.RegisterSourceOutput(filesWithLicense, static (productionContext, input) =>
        {
            var (file, licenseHeader) = input;
            try
            {
                var generated = Generate(file.Path, file.Content!, licenseHeader, productionContext.CancellationToken);
                if (generated == null || !generated.Any())
                {
                    GeneratorDiagnostics.ReportSkipped(productionContext, $"No output generated for '{file.Path}'. Ensure required DTO elements or JSON fields are present.");
                    return;
                }

                var addedHintNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (fileName, source) in generated)
                {
                    productionContext.CancellationToken.ThrowIfCancellationRequested();
                    if (!addedHintNames.Add(fileName))
                    {
                        GeneratorDiagnostics.ReportDuplicateHintName(productionContext, fileName);
                        continue;
                    }
                    productionContext.AddSource(fileName, source);
                }
            }
            catch (Exception ex)
            {
                GeneratorDiagnostics.ReportError(productionContext, $"DtoEntitySourceGenerator failed for '{file.Path}'", ex);
            }
        });
    }

    /// <summary>
    /// Public wrapper for CLI usage
    /// </summary>
    public IEnumerable<(string fileName, string source)>? GenerateFromFiles(string filePath, string fileContent, CancellationToken cancellationToken = default)
    {
        return Generate(filePath, fileContent, string.Empty, cancellationToken);
    }

    private static bool IsCandidateFile(string path)
    {
        for (var i = 0; i < CandidateSuffixes.Length; i++)
        {
            if (path.EndsWith(CandidateSuffixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<(string fileName, string source)>? Generate(string filePath, string fileContent, string licenseHeader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GenerateFromJson(fileContent, filePath, licenseHeader, cancellationToken);
    }

    private static IEnumerable<(string fileName, string source)>? GenerateFromJson(string fileContent, string filePath, string licenseHeader, CancellationToken cancellationToken)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(fileContent);
            var root = jsonDoc.RootElement;

            var genParams = ParseGeneratorParamsFromJson(root, filePath, licenseHeader, cancellationToken, parentNamespace: null);
            if (genParams == null)
            {
                return null;
            }

            List<(string fileName, string source)> generated = new();
            GenerateCodeRecursive(genParams, generated, cancellationToken);

            return generated;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DtoEntityGenerator.GeneratorParams? ParseGeneratorParamsFromJson(JsonElement root, string sourceFilePath, string licenseHeader, CancellationToken cancellationToken, string? parentNamespace)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!root.TryGetProperty("name", out var nameElement) ||
            (!root.TryGetProperty("namespace", out var namespaceElement) && string.IsNullOrEmpty(parentNamespace)))
        {
            return null;
        }

        var name = nameElement.GetString();
        var ns = root.TryGetProperty("namespace", out var nsElement) ? nsElement.GetString() : parentNamespace;

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ns))
        {
            return null;
        }

        var documentation = root.TryGetProperty("documentation", out var docElement) ? docElement.GetString() : null;
        var inherits = root.TryGetProperty("inherits", out var inheritsElement) ? inheritsElement.GetString() : null;
        var accessibility = root.TryGetProperty("accessibility", out var accessibilityElement) ? accessibilityElement.GetString() : "public";
        var isAbstract = root.TryGetProperty("abstract", out var abstractElement) && abstractElement.GetBoolean();
        var classOnly = root.TryGetProperty("classOnly", out var classOnlyElement) && classOnlyElement.GetBoolean();
        var isStrict = root.TryGetProperty("strict", out var strictElement) && strictElement.GetBoolean();
        var useParentValidator = !root.TryGetProperty("useParentValidator", out var useParentValidatorElement) || useParentValidatorElement.GetBoolean();
        var noCreateMethod = root.TryGetProperty("noCreateMethod", out var noCreateMethodElement) && noCreateMethodElement.GetBoolean();
        var isRecordStruct = root.TryGetProperty("isRecordStruct", out var isRecordStructElement) && isRecordStructElement.GetBoolean();

        if (isAbstract && isStrict)
        {
            return null;
        }

        if (isRecordStruct)
        {
            if (isAbstract)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(inherits))
            {
                // Records cannot inherit from classes
                return null;
            }
        }

        var properties = new List<DtoEntityGenerator.PropertyDescriptor>();
        if (root.TryGetProperty("properties", out var propsElement) && propsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var prop in propsElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!prop.TryGetProperty("name", out var propNameElement) || !prop.TryGetProperty("type", out var propTypeElement))
                {
                    continue;
                }

                var propName = propNameElement.GetString();
                var propType = propTypeElement.GetString();

                if (string.IsNullOrEmpty(propName) || string.IsNullOrEmpty(propType))
                {
                    continue;
                }

                var propDocumentation = prop.TryGetProperty("documentation", out var propDocElement) ? propDocElement.GetString() : null;
                var isRequired = prop.TryGetProperty("required", out var requiredElement) ? requiredElement.GetBoolean() : true;
                var isNullable = prop.TryGetProperty("nullable", out var nullableElement) && nullableElement.GetBoolean();
                var max = prop.TryGetProperty("max", out var maxElement) ? maxElement.GetString() : null;
                var min = prop.TryGetProperty("min", out var minElement) ? minElement.GetString() : null;
                var regex = prop.TryGetProperty("regex", out var regexElement) ? regexElement.GetString() : null;
                var jsonProperty = prop.TryGetProperty("jsonProperty", out var jsonPropertyElement) ? jsonPropertyElement.GetString() : null;
                var noDefault = prop.TryGetProperty("noDefault", out var noDefaultElement) && noDefaultElement.GetBoolean();
                var isSettable = prop.TryGetProperty("settable", out var settableElement) && settableElement.GetBoolean();
                var expression = prop.TryGetProperty("expression", out var expressionElement) ? expressionElement.GetString() : null;

                properties.Add(new DtoEntityGenerator.PropertyDescriptor(
                    propName, propType, isRequired, isNullable, max, min, regex, jsonProperty, noDefault, isSettable, expression, propDocumentation));
            }
        }

        var nestedEntities = new List<DtoEntityGenerator.GeneratorParams>();
        if (root.TryGetProperty("nestedEntities", out var nestedElement) && nestedElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var nested in nestedElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nestedParams = ParseGeneratorParamsFromJson(nested, sourceFilePath, licenseHeader, cancellationToken, ns);
                if (nestedParams != null)
                {
                    nestedEntities.Add(nestedParams);
                }
            }
        }

        var genParams = new DtoEntityGenerator.GeneratorParams(
            name: name,
            ns: ns,
            parentName: parentNamespace,
            inherits: inherits,
            isAbstract: isAbstract,
            accessibility: accessibility,
            sourceFilePath: sourceFilePath,
            properties: properties,
            nestedEntities: nestedEntities.Count > 0 ? nestedEntities : null,
            documentation: documentation,
            classOnly: classOnly,
            isStrict: isStrict,
            useParentValidator: useParentValidator,
            noCreateMethod: noCreateMethod,
            isRecordStruct: isRecordStruct)
        {
            LicenseHeader = licenseHeader
        };

        return genParams;
    }

    private static void GenerateCodeRecursive(DtoEntityGenerator.GeneratorParams genParams, List<(string fileName, string source)> generated, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var generatedCode = DtoEntityGenerator.Generate(genParams, null);
        if (!string.IsNullOrEmpty(generatedCode))
        {
            var fileName = $"{genParams.Namespace}.{genParams.Name}.g.cs";
            generated.Add((fileName, generatedCode!));
        }

        if (genParams.NestedEntities != null)
        {
            foreach (var nested in genParams.NestedEntities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GenerateCodeRecursive(nested, generated, cancellationToken);
            }
        }
    }
}
