namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;

[Generator(LanguageNames.CSharp)]
public sealed class MultiValueBackedTypeSourceGenerator : IIncrementalGenerator
{
    private static readonly string[] CandidateSuffixes = new[]
    {
        ".multi.json",
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
                    GeneratorDiagnostics.ReportSkipped(productionContext, $"No output generated for '{file.Path}'. Ensure required <MultiValueString> elements or JSON fields are present.");
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
                GeneratorDiagnostics.ReportError(productionContext, $"MultiValueBackedTypeSourceGenerator failed for '{file.Path}'", ex);
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

            string? separator = null;
            if (root.TryGetProperty("separator", out var separatorElement))
            {
                separator = separatorElement.GetString();
            }

            string? format = null;
            if (root.TryGetProperty("format", out var formatElement))
            {
                format = formatElement.GetString();
            }

            string? regex = null;
            if (root.TryGetProperty("regex", out var regexElement))
            {
                regex = regexElement.GetString();
            }

            string? bookend = null;
            if (root.TryGetProperty("bookend", out var bookendElement))
            {
                bookend = bookendElement.GetString();
            }

            string? nullIdentifier = null;
            if (root.TryGetProperty("nullIdentifier", out var nullIdentifierElement))
            {
                nullIdentifier = nullIdentifierElement.GetString();
            }

            var fields = new List<MultiValueBackedTypeGenerator.FieldInfo>();
            if (root.TryGetProperty("parts", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in partsElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fieldName = part.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var fieldType = part.TryGetProperty("type", out var t) ? t.GetString() : "string";
                    var serializedName = part.TryGetProperty("serializedName", out var sn) ? sn.GetString() : null;
                    var constantValue = part.TryGetProperty("constantValue", out var cv) ? cv.GetString() : null;
                    var constantTypeValue = part.TryGetProperty("constantTypeValue", out var ctv) ? ctv.GetString() : null;
                    var isNullable = part.TryGetProperty("isNullable", out var inull) && inull.GetBoolean();
                    var partNullIdentifier = part.TryGetProperty("nullIdentifier", out var pni) ? pni.GetString() : null;

                    if (!string.IsNullOrEmpty(fieldName))
                    {
                        fields.Add(new MultiValueBackedTypeGenerator.FieldInfo(
                            fieldName!,
                            fieldType!,
                            serializedName,
                            constantValue,
                            constantTypeValue,
                            isNullable,
                            partNullIdentifier ?? nullIdentifier
                        ));
                    }
                }
            }

            var genParams = new MultiValueBackedTypeGenerator.GeneratorParams(
                name!,
                namespaceName!,
                true,
                separator ?? "|",
                format ?? string.Empty,
                regex ?? string.Empty,
                bookend ?? string.Empty,
                fields,
                sourceFilePath,
                licenseHeader
            );

            var generatedCode = MultiValueBackedTypeGenerator.Generate(genParams, null);
            if (string.IsNullOrEmpty(generatedCode))
            {
                return null;
            }

            var fileName = $"{namespaceName!}.{name!}.g.cs";
            var results = new List<(string fileName, string source)> { (fileName, generatedCode!) };

            // Generate ValueConverter if path is configured
            if (ValueConverterConfig.IsEnabled)
            {
                var converterCode = MultiValueBackedTypeGenerator.GenerateValueConverter(genParams, null);
                if (!string.IsNullOrEmpty(converterCode))
                {
                    var converterFileName = $"{namespaceName!}.{name!}ValueConverter.g.cs";
                    results.Add((converterFileName, converterCode!));
                }
            }

            return results;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
