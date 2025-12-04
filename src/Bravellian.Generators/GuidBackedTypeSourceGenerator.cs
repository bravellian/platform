namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;

[Generator(LanguageNames.CSharp)]
public sealed class GuidBackedTypeSourceGenerator : IIncrementalGenerator
{
    private static readonly string[] CandidateSuffixes = new[]
    {
        ".guid.json",
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
                    GeneratorDiagnostics.ReportSkipped(productionContext, $"No output generated for '{file.Path}'. Ensure required <GuidBacked> elements or JSON fields are present.");
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
                GeneratorDiagnostics.ReportError(productionContext, $"GuidBackedTypeSourceGenerator failed for '{file.Path}'", ex);
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
                !root.TryGetProperty("namespace", out var namespaceElement))
            {
                throw new InvalidDataException($"Required properties 'name' and 'namespace' are missing in '{sourceFilePath}'.");
            }

            var name = nameElement.GetString();
            var namespaceName = namespaceElement.GetString();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(namespaceName))
            {
                throw new InvalidDataException($"Properties 'name' and 'namespace' must be non-empty in '{sourceFilePath}'.");
            }

            bool useDefaultFormat = false;
            if (root.TryGetProperty("defaultFormat", out var defaultFormatElement))
            {
                useDefaultFormat = defaultFormatElement.ValueKind == JsonValueKind.True;
                if (defaultFormatElement.ValueKind == JsonValueKind.False)
                {
                    useDefaultFormat = false;
                }

                if (defaultFormatElement.ValueKind == JsonValueKind.String && bool.TryParse(defaultFormatElement.GetString(), out var parsed))
                {
                    useDefaultFormat = parsed;
                }
            }

            var genParams = new GuidBackedTypeGenerator.GeneratorParams(
                name!,
                namespaceName!,
                true,
                useDefaultFormat,
                sourceFilePath,
                licenseHeader
            );

            var generatedCode = GuidBackedTypeGenerator.Generate(genParams, null);
            if (string.IsNullOrEmpty(generatedCode))
            {
                return null;
            }

            var fileName = $"{namespaceName!}.{name!}.g.cs";
            var results = new List<(string fileName, string source)> { (fileName, generatedCode!) };

            // Generate ValueConverter if path is configured
            if (ValueConverterConfig.IsEnabled)
            {
                var converterCode = GuidBackedTypeGenerator.GenerateValueConverter(genParams, null);
                if (!string.IsNullOrEmpty(converterCode))
                {
                    var converterFileName = $"{namespaceName!}.{name!}ValueConverter.g.cs";
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
