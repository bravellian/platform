using Microsoft.CodeAnalysis;

namespace Bravellian.Platform.Observability.Analyzers;

internal static class Diagnostics
{
    public const string AuditEventNameFormatId = "OBS001";

    public static readonly DiagnosticDescriptor AuditEventNameFormat = new(
        AuditEventNameFormatId,
        "Audit event name should be lowercase and dot-separated",
        "Audit event name '{0}' should be lowercase and dot-separated",
        "Observability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
