namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;

[Generator(LanguageNames.CSharp)]
public sealed class StringBackedEnumTypeSourceGenerator : IIncrementalGenerator
{
    private static readonly string[] CandidateSuffixes = new[]
    {
        ".enum.json",
        ".string_enum.json",
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
                    GeneratorDiagnostics.ReportSkipped(productionContext, $"No output generated for '{file.Path}'. Ensure required <StringEnum> elements or JSON fields are present.");
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
                GeneratorDiagnostics.ReportError(productionContext, $"StringBackedEnumTypeSourceGenerator failed for '{file.Path}'", ex);
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

    private static IEnumerable<(string fileName, string source)>? GenerateFromJson(string fileContent, string sourceFilePath, string licenseHeader, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(fileContent);
            var root = document.RootElement;

            if (!root.TryGetProperty("name", out var nameElement) ||
                !root.TryGetProperty("namespace", out var namespaceElement) ||
                !root.TryGetProperty("values", out var valuesElement))
            {
                throw new InvalidDataException($"Required properties 'name', 'namespace', or 'values' are missing in '{sourceFilePath}'.");
            }

            var name = nameElement.GetString();
            var namespaceName = namespaceElement.GetString();

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(namespaceName))
            {
                throw new InvalidDataException($"Properties 'name' and 'namespace' must be non-empty in '{sourceFilePath}'.");
            }

            var enumValues = new List<(string Value, string Name, string? DisplayName, string? Documentation)>();

            foreach (var valueProperty in valuesElement.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var valueName = valueProperty.Name;
                var valueObj = valueProperty.Value;

                if (!valueObj.TryGetProperty("value", out var valueElement))
                {
                    continue;
                }

                var value = valueElement.GetString();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var displayName = valueObj.TryGetProperty("display", out var displayElement)
                    ? displayElement.GetString()
                    : valueName;

                var documentation = valueObj.TryGetProperty("documentation", out var docElement)
                    ? docElement.GetString()
                    : null;

                enumValues.Add((value!, valueName, displayName, documentation));
            }

            if (!enumValues.Any())
            {
                throw new InvalidDataException($"No enum values were found under 'values' in '{sourceFilePath}'.");
            }

            // Parse additional properties
            var additionalProperties = new List<(string Type, string Name)>();
            if (root.TryGetProperty("properties", out var propertiesElement))
            {
                foreach (var property in propertiesElement.EnumerateObject())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var propertyName = property.Name;
                    var propertyObj = property.Value;

                    if (propertyObj.TryGetProperty("type", out var typeElement))
                    {
                        var typeName = typeElement.GetString();
                        if (!string.IsNullOrEmpty(typeName))
                        {
                            additionalProperties.Add((typeName!, propertyName));
                        }
                    }
                }
            }

            var genParams = new StringBackedEnumTypeGenerator.GeneratorParams(
                name!,
                namespaceName!,
                true,
                enumValues,
                additionalProperties,
                sourceFilePath,
                licenseHeader
            );

            var generatedCode = StringBackedEnumTypeGenerator.Generate(genParams, null);
            if (string.IsNullOrEmpty(generatedCode))
            {
                return null;
            }

            var fileName = $"{namespaceName!}.{name!}.{Path.GetFileName(sourceFilePath)}.g.cs";
            var results = new List<(string fileName, string source)> { (fileName, generatedCode!) };

            // Generate ValueConverter if path is configured
            if (ValueConverterConfig.IsEnabled)
            {
                var converterCode = StringBackedEnumTypeGenerator.GenerateValueConverter(genParams, null);
                if (!string.IsNullOrEmpty(converterCode))
                {
                    var converterFileName = $"{namespaceName!}.{name!}ValueConverter.{Path.GetFileName(sourceFilePath)}.g.cs";
                    results.Add((converterFileName, converterCode!));
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to process JSON for '{sourceFilePath}'.", ex);
        }
    }
}
