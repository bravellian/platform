namespace Bravellian.Platform.Modularity;

/// <summary>
/// Categories supported by the module registry.
/// </summary>
public enum ModuleCategory
{
    /// <summary>
    /// Background-only modules with no HTTP surface area.
    /// </summary>
    Background,

    /// <summary>
    /// API-first modules that expose HTTP endpoints.
    /// </summary>
    Api,

    /// <summary>
    /// Full stack modules that bundle UI in addition to APIs.
    /// </summary>
    FullStack,
}
