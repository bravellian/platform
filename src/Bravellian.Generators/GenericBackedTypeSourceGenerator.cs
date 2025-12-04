namespace Bravellian.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

[Generator(LanguageNames.CSharp)]
public sealed class GenericBackedTypeSourceGenerator : IIncrementalGenerator
{
    private static readonly string[] CandidateSuffixes = System.Array.Empty<string>();

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
                    GeneratorDiagnostics.ReportSkipped(productionContext, $"No output generated for '{file.Path}'. Ensure required <GenericBacked> elements are present.");
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
                GeneratorDiagnostics.ReportError(productionContext, $"GenericBackedTypeSourceGenerator failed for '{file.Path}'", ex);
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
        return null;
    }
}
