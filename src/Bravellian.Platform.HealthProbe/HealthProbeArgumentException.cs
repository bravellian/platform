using System.Runtime.Serialization;

namespace Bravellian.Platform.HealthProbe;

/// <summary>
/// Exception thrown for invalid health probe arguments.
/// </summary>
[Serializable]
public sealed class HealthProbeArgumentException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HealthProbeArgumentException"/> class.
    /// </summary>
    public HealthProbeArgumentException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthProbeArgumentException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public HealthProbeArgumentException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthProbeArgumentException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public HealthProbeArgumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    #pragma warning disable SYSLIB0051
    private HealthProbeArgumentException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
    #pragma warning restore SYSLIB0051
}
