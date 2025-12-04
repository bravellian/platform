namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;

[Generator(LanguageNames.CSharp)]
public sealed class StringBackedTypeSourceGenerator : IIncrementalGenerator
{
    private static readonly string[] CandidateSuffixes = new[]
    {
        ".string.json",
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
                    GeneratorDiagnostics.ReportSkipped(productionContext, 
                        "No output generated. Ensure required 'name' and 'namespace' properties are present and valid.", 
                        file.Path);
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
                GeneratorDiagnostics.ReportError(productionContext, 
                    "StringBackedTypeSourceGenerator failed to generate code.", 
                    ex, 
                    file.Path);
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

    private static IEnumerable<(string fileName, string source)>? GenerateFromJson(string fileContent, string? filePath, string licenseHeader, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(fileContent);
            var root = document.RootElement;

            if (!root.TryGetProperty("name", out var nameElement))
            {
                throw new InvalidDataException($"Required property 'name' is missing. Each type definition must have a 'name' property.");
            }

            if (!root.TryGetProperty("namespace", out var namespaceElement))
            {
                throw new InvalidDataException($"Required property 'namespace' is missing. Each type definition must have a 'namespace' property.");
            }

            var name = nameElement.GetString();
            var namespaceName = namespaceElement.GetString();
            
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException($"Property 'name' must be a non-empty string. Current value: '{name ?? "null"}'");
            }
            
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                throw new InvalidDataException($"Property 'namespace' must be a non-empty string. Current value: '{namespaceName ?? "null"}'");
            }

            string? regex = null;
            if (root.TryGetProperty("regex", out var regexElement))
            {
                regex = regexElement.GetString();
            }

            string? regexConst = null;
            if (root.TryGetProperty("regexConst", out var regexConstElement))
            {
                regexConst = regexConstElement.GetString();
            }

            var additionalProperties = new List<(string Type, string Name)>();
            if (root.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var prop in propertiesElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var propName = prop.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var propType = prop.TryGetProperty("type", out var t) ? t.GetString() : null;
                    
                    // Both must be present and non-empty, or both must be absent/empty
                    var hasName = !string.IsNullOrWhiteSpace(propName);
                    var hasType = !string.IsNullOrWhiteSpace(propType);
                    
                    if (hasName && hasType)
                    {
                        additionalProperties.Add((propType!, propName!));
                    }
                    else if (hasName != hasType)
                    {
                        // Exactly one is set - this is an error
                        throw new InvalidDataException($"Property definition incomplete. Both 'name' and 'type' must be specified for additional properties. Got name: '{propName ?? "null"}', type: '{propType ?? "null"}'");
                    }
                    // else: both are empty/null, which is valid - just skip this entry
                }
            }

            var genParams = new StringBackedTypeGenerator.GeneratorParams(
                name!,
                namespaceName!,
                true,
                regex,
                regexConst,
                additionalProperties,
                filePath,
                licenseHeader
            );

            var generatedCode = StringBackedTypeGenerator.Generate(genParams, null);
            if (string.IsNullOrEmpty(generatedCode))
            {
                // This shouldn't happen with valid parameters, but handle gracefully
                return null;
            }

            var fileName = $"{namespaceName!}.{name!}.{Path.GetFileName(filePath)}.g.cs";
            var results = new List<(string fileName, string source)> { (fileName, generatedCode!) };

            // Generate ValueConverter if path is configured
            if (ValueConverterConfig.IsEnabled)
            {
                var converterCode = StringBackedTypeGenerator.GenerateValueConverter(genParams, null);
                if (!string.IsNullOrEmpty(converterCode))
                {
                    var converterFileName = $"{namespaceName!}.{name!}ValueConverter.{Path.GetFileName(filePath)}.g.cs";
                    results.Add((converterFileName, converterCode!));
                }
            }

            return results;
        }
        catch (JsonException jsonEx)
        {
            throw new InvalidDataException($"Failed to parse JSON. Ensure the file is valid JSON. Details: {jsonEx.Message}", jsonEx);
        }
        catch (InvalidDataException)
        {
            // Re-throw validation errors as-is
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unexpected error during code generation. Details: {ex.Message}", ex);
        }
    }
}
